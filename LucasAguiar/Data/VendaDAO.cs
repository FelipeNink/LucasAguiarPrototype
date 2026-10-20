using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class VendaDAO
    {
        private readonly Conexao _conexao;

        public VendaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Grava a venda e seus itens numa única transação: ou entra tudo,
        /// ou nada. Devolve o id gerado.
        /// </summary>
        public int Inserir(Venda venda)
        {
            if (venda.Itens == null || venda.Itens.Count == 0)
                throw new Exception("A venda precisa de pelo menos um item.");

            // O negocio trabalha com comissao: sem profissional nao ha a quem pagar.
            if (!venda.IdFuncionario.HasValue || venda.IdFuncionario.Value <= 0)
                throw new Exception("Selecione o profissional que realizou o atendimento.");

            Preparar(venda);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var total = venda.Total;
                var qtdServicos = venda.Itens.Where(i => i.TipoItem != TipoItem.Produto).Sum(i => i.Quantidade);
                var qtdProdutos = venda.Itens.Where(i => i.TipoItem == TipoItem.Produto).Sum(i => i.Quantidade);

                int idVenda;
                using (var comando = new MySqlCommand(@"
                    INSERT INTO venda
                        (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend,
                         forma_pagamento_vend, desconto_vend, quant_parcela_vend,
                         descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
                    VALUES
                        (@valor, @data, @qtdProd, @qtdServ,
                         @forma, @desconto, @parcelas,
                         @descricao, @status, @idFuncionario, @idCliente, @idCaixa);
                    SELECT LAST_INSERT_ID();", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@valor", total);
                    comando.Parameters.AddWithValue("@data", venda.DataVenda);
                    comando.Parameters.AddWithValue("@qtdProd", qtdProdutos);
                    comando.Parameters.AddWithValue("@qtdServ", qtdServicos);
                    comando.Parameters.AddWithValue("@forma", venda.FormaPagamentoVenda);
                    comando.Parameters.AddWithValue("@desconto", venda.Desconto);
                    comando.Parameters.AddWithValue("@parcelas", venda.QuantidadeParcelas ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@descricao", MontarDescricao(venda));
                    comando.Parameters.AddWithValue("@status", venda.Status);
                    comando.Parameters.AddWithValue("@idFuncionario",
                        venda.IdFuncionario.HasValue && venda.IdFuncionario > 0
                            ? venda.IdFuncionario.Value : (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@idCliente",
                        venda.IdCliente.HasValue && venda.IdCliente > 0
                            ? venda.IdCliente.Value : (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@idCaixa",
                        venda.IdCaixa.HasValue && venda.IdCaixa > 0
                            ? venda.IdCaixa.Value : (object)DBNull.Value);

                    idVenda = Convert.ToInt32(comando.ExecuteScalar());
                }

                GravarItens(conexao, transacao, idVenda, venda.Itens);
                GravarPagamentos(conexao, transacao, idVenda, venda.Pagamentos);

                // Venda de plano gera a assinatura com os creditos.
                if (venda.PlanoVendido != null && venda.IdCliente.HasValue)
                {
                    AssinaturaDAO.CriarNaTransacao(
                        conexao, transacao,
                        venda.IdCliente.Value,
                        venda.PlanoVendido.IdPlano,
                        venda.PlanoVendido.ValorPago,
                        idVenda,
                        venda.PlanoVendido.Componentes,
                        venda.PlanoVendido.ValidadeDias);
                }

                transacao.Commit();
                return idVenda;
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao registrar a venda: " + ex.Message);
            }
        }

        /// <summary>
        /// Cancela a venda e devolve o que ela consumiu: crédito de plano volta
        /// para a assinatura. Tudo numa transação — cancelamento pela metade
        /// deixaria o cliente sem o crédito que pagou.
        /// </summary>
        public void Cancelar(int idVenda, string motivo, string? usuario)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new Exception("Informe o motivo do cancelamento.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                // A condição de status impede cancelar duas vezes.
                using (var comando = new MySqlCommand(@"
                    UPDATE venda
                    SET status_vend = 'CANCELADA',
                        data_cancelamento = @data,
                        motivo_cancelamento = @motivo,
                        usuario_cancelamento = @usuario
                    WHERE id_vend = @id AND status_vend <> 'CANCELADA'", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@data", DateTime.Now);
                    comando.Parameters.AddWithValue("@motivo", motivo.Trim());
                    comando.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
                    comando.Parameters.AddWithValue("@id", idVenda);

                    if (comando.ExecuteNonQuery() == 0)
                        throw new Exception("Esta venda já está cancelada.");
                }

                DevolverConsumo(conexao, transacao, idVenda);

                // Venda que criou uma assinatura: encerra a assinatura junto.
                using (var comando = new MySqlCommand(@"
                    UPDATE assinatura SET status = 'ENCERRADA'
                    WHERE id_vend_fk = @id AND status = 'ATIVA'", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@id", idVenda);
                    comando.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao cancelar a venda: " + ex.Message);
            }
        }

        /// <summary>
        /// Grava os itens da venda e o que eles movimentam: abate o credito de
        /// plano e baixa o estoque do produto. Usado ao registrar e ao editar.
        /// </summary>
        private static void GravarItens(
            MySqlConnection conexao, MySqlTransaction transacao, int idVenda, IEnumerable<VendaItem> itens)
        {
            foreach (var item in itens)
            {
                using (var comando = new MySqlCommand(@"
                    INSERT INTO venda_item
                        (id_vend_fk, tipo_item, id_ref, descricao, quantidade,
                         valor_unit, valor_total, comissao, coberto_plano, id_saldo_fk,
                         id_fun_fk, desconto)
                    VALUES
                        (@idVenda, @tipo, @idRef, @descricao, @qtd,
                         @unit, @total, @comissao, @coberto, @idSaldo,
                         @idFuncionario, @desconto)", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@idFuncionario", item.IdFuncionario ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@desconto", item.Desconto);
                    comando.Parameters.AddWithValue("@idVenda", idVenda);
                    comando.Parameters.AddWithValue("@tipo", item.TipoItem);
                    comando.Parameters.AddWithValue("@idRef", item.IdReferencia ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@descricao", item.Descricao);
                    comando.Parameters.AddWithValue("@qtd", item.Quantidade);
                    comando.Parameters.AddWithValue("@unit", item.ValorUnitario);
                    comando.Parameters.AddWithValue("@total", item.ValorTotal);
                    comando.Parameters.AddWithValue("@comissao", item.ComissaoUnitaria);
                    comando.Parameters.AddWithValue("@coberto", item.CobertoPlano ? 1 : 0);
                    comando.Parameters.AddWithValue("@idSaldo", item.IdSaldo ?? (object)DBNull.Value);
                    comando.ExecuteNonQuery();
                }

                // Abate o credito na mesma transacao: se a venda falhar,
                // o saldo do cliente volta ao que era.
                if (item.CobertoPlano && item.IdSaldo.HasValue)
                    AssinaturaDAO.ConsumirNaTransacao(conexao, transacao, item.IdSaldo.Value, item.Quantidade);

                // Produto vendido sai do estoque. Servico nao tem estoque.
                if (item.TipoItem == TipoItem.Produto && item.IdReferencia.HasValue)
                {
                    using var baixa = new MySqlCommand(@"
                        UPDATE produto
                        SET quantidade_prod = GREATEST(0, COALESCE(quantidade_prod, 0) - @qtd)
                        WHERE id_prod = @idProduto", conexao, transacao);

                    baixa.Parameters.AddWithValue("@qtd", item.Quantidade);
                    baixa.Parameters.AddWithValue("@idProduto", item.IdReferencia.Value);
                    baixa.ExecuteNonQuery();
                }
            }
        }

        private static void GravarPagamentos(
            MySqlConnection conexao, MySqlTransaction transacao, int idVenda, IEnumerable<VendaPagamento> pagamentos)
        {
            foreach (var p in pagamentos)
            {
                using var comando = new MySqlCommand(@"
                    INSERT INTO venda_pagamento (id_vend_fk, forma, valor, parcelas)
                    VALUES (@idVenda, @forma, @valor, @parcelas)", conexao, transacao);
                comando.Parameters.AddWithValue("@idVenda", idVenda);
                comando.Parameters.AddWithValue("@forma", p.Forma);
                comando.Parameters.AddWithValue("@valor", p.Valor);
                comando.Parameters.AddWithValue("@parcelas", p.Parcelas ?? (object)DBNull.Value);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Confere e completa a venda antes de gravar: cada item fica com o
        /// seu profissional (o da venda, quando nao escolhido) e os pagamentos
        /// tem de fechar o total. Sem grade, a forma unica vira um pagamento
        /// so do total -- e o caminho de quem ainda manda so a forma.
        /// </summary>
        private static void Preparar(Venda venda)
        {
            foreach (var item in venda.Itens)
            {
                if (item.Desconto < 0)
                    throw new Exception($"O desconto de {item.Descricao} não pode ser negativo.");
                if (item.Desconto > Math.Round(item.ValorUnitario * item.Quantidade, 2))
                    throw new Exception($"O desconto de {item.Descricao} é maior que o valor do item.");

                if (!(item.IdFuncionario > 0)) item.IdFuncionario = venda.IdFuncionario;
            }

            var total = venda.Total;

            if (venda.Pagamentos.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(venda.FormaPagamentoVenda))
                    throw new Exception("Selecione a forma de pagamento.");

                venda.Pagamentos.Add(new VendaPagamento
                {
                    Forma = venda.FormaPagamentoVenda,
                    Valor = total,
                    Parcelas = venda.QuantidadeParcelas
                });
            }

            // Linha zerada numa venda com valor e so sobra da grade.
            if (total > 0) venda.Pagamentos.RemoveAll(p => p.Valor == 0);

            var formasValidas = FormaPagamento.Todas.Select(f => f.Valor).Append(FormaPagamento.Plano);
            foreach (var p in venda.Pagamentos)
            {
                if (!formasValidas.Contains(p.Forma)) throw new Exception("Forma de pagamento inválida.");
                if (p.Valor < 0) throw new Exception("O valor do pagamento não pode ser negativo.");
                p.Parcelas = p.Forma == FormaPagamento.Credito ? Math.Clamp(p.Parcelas ?? 1, 1, 12) : null;
            }

            if (venda.Pagamentos.Count == 0)
                throw new Exception("Informe o pagamento.");

            var pago = venda.Pagamentos.Sum(p => p.Valor);
            if (pago != total)
                throw new Exception($"Os pagamentos somam {pago:C}, mas o total da venda é {total:C}.");

            var formas = venda.Pagamentos.Select(p => p.Forma).Distinct().ToList();
            venda.FormaPagamentoVenda = formas.Count == 1 ? formas[0] : FormaPagamento.Misto;
            venda.QuantidadeParcelas = venda.Pagamentos.Where(p => p.Parcelas.HasValue).Max(p => p.Parcelas);
        }

        /// <summary>
        /// Devolve o que os itens gravados da venda consumiram: credito de
        /// plano volta para a assinatura e produto volta para o estoque.
        /// Usado ao cancelar e ao editar (antes de regravar os itens).
        /// </summary>
        private static void DevolverConsumo(MySqlConnection conexao, MySqlTransaction transacao, int idVenda)
        {
            // Somado por credito e por produto: UPDATE com JOIN altera cada
            // linha uma vez so, e duas linhas do mesmo produto na venda
            // devolviam so uma ao estoque.

            // Devolve os créditos de plano consumidos nesta venda.
            using (var comando = new MySqlCommand(@"
                UPDATE assinatura_saldo s
                JOIN (SELECT id_saldo_fk, SUM(quantidade) AS qtd
                        FROM venda_item
                       WHERE id_vend_fk = @id AND coberto_plano = 1 AND id_saldo_fk IS NOT NULL
                       GROUP BY id_saldo_fk) u ON u.id_saldo_fk = s.id_saldo
                SET s.quantidade_usada = GREATEST(0, s.quantidade_usada - u.qtd)", conexao, transacao))
            {
                comando.Parameters.AddWithValue("@id", idVenda);
                comando.ExecuteNonQuery();
            }

            // Devolve ao estoque os produtos que a venda tinha baixado.
            using (var comando = new MySqlCommand(@"
                UPDATE produto p
                JOIN (SELECT id_ref, SUM(quantidade) AS qtd
                        FROM venda_item
                       WHERE id_vend_fk = @id AND tipo_item = 'PRODUTO' AND id_ref IS NOT NULL
                       GROUP BY id_ref) b ON b.id_ref = p.id_prod
                SET p.quantidade_prod = COALESCE(p.quantidade_prod, 0) + b.qtd", conexao, transacao))
            {
                comando.Parameters.AddWithValue("@id", idVenda);
                comando.ExecuteNonQuery();
            }
        }

        // ---------- edicao ----------

        public enum ModoEdicao { Completa, Restrita, Bloqueada }

        /// <summary>
        /// O quanto uma venda ainda pode mudar, e por que.
        /// Completa: itens, valores, pagamento, cliente e profissional.
        /// Restrita: so cliente e, quando a comissao nao foi paga, profissional.
        /// </summary>
        public record SituacaoEdicao(ModoEdicao Modo, string? Motivo, bool PodeTrocarProfissional);

        public SituacaoEdicao SituacaoParaEditar(int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            return SituacaoParaEditar(conexao, null, idVenda);
        }

        private static SituacaoEdicao SituacaoParaEditar(
            MySqlConnection conexao, MySqlTransaction? transacao, int idVenda)
        {
            using var comando = new MySqlCommand(@"
                SELECT v.status_vend, v.id_caixa_fk, c.status AS status_caixa,
                       DATE_FORMAT(v.data_vend, '%m/%Y') AS mes,
                       EXISTS (SELECT 1 FROM folha_pagamento fp
                                WHERE (fp.id_fun_fk = v.id_fun_fk
                                       OR fp.id_fun_fk IN (SELECT vi.id_fun_fk FROM venda_item vi
                                                            WHERE vi.id_vend_fk = v.id_vend))
                                  AND fp.competencia = DATE_FORMAT(v.data_vend, '%Y-%m')) AS folha_paga,
                       EXISTS (SELECT 1 FROM venda_item vi
                                WHERE vi.id_vend_fk = v.id_vend AND vi.tipo_item = 'PLANO') AS tem_plano
                FROM venda v
                LEFT JOIN caixa c ON c.id_caixa = v.id_caixa_fk
                WHERE v.id_vend = @id" + (transacao != null ? " FOR UPDATE" : ""), conexao, transacao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read())
                return new(ModoEdicao.Bloqueada, "Venda não encontrada.", false);

            if (DAOHelper.GetString(leitor, "status_vend") == StatusVenda.Cancelada)
                return new(ModoEdicao.Bloqueada, "Venda cancelada não pode ser editada.", false);

            var mes = DAOHelper.GetString(leitor, "mes");
            var folhaPaga = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("folha_paga"))) == 1;
            var temPlano = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("tem_plano"))) == 1;
            var caixaFechado = DAOHelper.GetString(leitor, "status_caixa") == StatusCaixa.Fechado;

            if (folhaPaga)
                return new(ModoEdicao.Restrita,
                    $"A comissão desta venda já foi paga na folha de {mes}: só o cliente pode mudar. "
                    + "Para corrigir valores, cancele a venda e lance outra.", false);

            if (caixaFechado)
                return new(ModoEdicao.Restrita,
                    "O caixa desta venda já foi fechado e conferido: dá para trocar cliente e profissional. "
                    + "Para corrigir itens, valores ou pagamento, cancele a venda e lance outra.", true);

            if (temPlano)
                return new(ModoEdicao.Restrita,
                    "Esta venda gerou uma assinatura de plano: dá para trocar cliente e profissional. "
                    + "Para mudar o resto, cancele a venda (a assinatura é encerrada junto) e lance outra.", true);

            return new(ModoEdicao.Completa, null, true);
        }

        /// <summary>
        /// Regrava a venda inteira: itens, valores, pagamento, cliente e
        /// profissional. Numa transacao, devolve o que a versao anterior
        /// consumiu (estoque e credito de plano) e consome de novo pelos itens
        /// novos. Data e caixa da venda nao mudam; os totais do caixa sao
        /// apurados das vendas, entao acompanham sozinhos.
        /// </summary>
        public void Atualizar(Venda venda)
        {
            if (venda.Itens == null || venda.Itens.Count == 0)
                throw new Exception("A venda precisa de pelo menos um item.");

            if (!venda.IdFuncionario.HasValue || venda.IdFuncionario.Value <= 0)
                throw new Exception("Selecione o profissional que realizou o atendimento.");

            if (venda.Itens.Any(i => i.TipoItem == TipoItem.Plano))
                throw new Exception("Venda de plano não pode ser editada por aqui.");

            Preparar(venda);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                // Confere de novo dentro da transacao, com a linha travada: o
                // caixa pode ter fechado enquanto a tela estava aberta.
                var situacao = SituacaoParaEditar(conexao, transacao, venda.IdVenda);
                if (situacao.Modo != ModoEdicao.Completa)
                    throw new Exception(situacao.Motivo ?? "Esta venda não pode mais ser editada.");

                DevolverConsumo(conexao, transacao, venda.IdVenda);

                using (var limpar = new MySqlCommand(@"
                    DELETE FROM venda_item WHERE id_vend_fk = @id;
                    DELETE FROM venda_pagamento WHERE id_vend_fk = @id;", conexao, transacao))
                {
                    limpar.Parameters.AddWithValue("@id", venda.IdVenda);
                    limpar.ExecuteNonQuery();
                }

                using (var comando = new MySqlCommand(@"
                    UPDATE venda SET
                        valor_vend = @valor, quantidade_prod_vend = @qtdProd, quantidade_serv_vend = @qtdServ,
                        forma_pagamento_vend = @forma, desconto_vend = @desconto, quant_parcela_vend = @parcelas,
                        descricao_vend = @descricao, id_fun_fk = @idFuncionario, id_cli_fk = @idCliente
                    WHERE id_vend = @id", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@valor", venda.Total);
                    comando.Parameters.AddWithValue("@qtdProd",
                        venda.Itens.Where(i => i.TipoItem == TipoItem.Produto).Sum(i => i.Quantidade));
                    comando.Parameters.AddWithValue("@qtdServ",
                        venda.Itens.Where(i => i.TipoItem != TipoItem.Produto).Sum(i => i.Quantidade));
                    comando.Parameters.AddWithValue("@forma", venda.FormaPagamentoVenda);
                    comando.Parameters.AddWithValue("@desconto", venda.Desconto);
                    comando.Parameters.AddWithValue("@parcelas", venda.QuantidadeParcelas ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@descricao", MontarDescricao(venda));
                    comando.Parameters.AddWithValue("@idFuncionario", venda.IdFuncionario.Value);
                    comando.Parameters.AddWithValue("@idCliente",
                        venda.IdCliente.HasValue && venda.IdCliente > 0
                            ? venda.IdCliente.Value : (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@id", venda.IdVenda);
                    comando.ExecuteNonQuery();
                }

                GravarItens(conexao, transacao, venda.IdVenda, venda.Itens);
                GravarPagamentos(conexao, transacao, venda.IdVenda, venda.Pagamentos);

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao salvar a venda: " + ex.Message);
            }
        }

        /// <summary>
        /// Edicao restrita: so cliente e profissional. Valores, itens e
        /// pagamento ficam intactos, entao o caixa nao e afetado.
        /// </summary>
        public void AtualizarClienteEProfissional(int idVenda, int? idCliente, int idFuncionario)
        {
            if (idFuncionario <= 0) throw new Exception("Selecione o profissional.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var situacao = SituacaoParaEditar(conexao, transacao, idVenda);
                if (situacao.Modo == ModoEdicao.Bloqueada)
                    throw new Exception(situacao.Motivo);

                // Os itens que eram do profissional da venda vao com ele; item
                // feito por outro profissional fica com quem o fez. Roda antes
                // do UPDATE da venda, que ainda guarda o profissional antigo.
                if (situacao.PodeTrocarProfissional)
                {
                    using var itens = new MySqlCommand(@"
                        UPDATE venda_item vi
                          JOIN venda v ON v.id_vend = vi.id_vend_fk
                           SET vi.id_fun_fk = @idFuncionario
                         WHERE vi.id_vend_fk = @id AND vi.id_fun_fk <=> v.id_fun_fk", conexao, transacao);
                    itens.Parameters.AddWithValue("@idFuncionario", idFuncionario);
                    itens.Parameters.AddWithValue("@id", idVenda);
                    itens.ExecuteNonQuery();
                }

                using var comando = new MySqlCommand(@"
                    UPDATE venda SET id_cli_fk = @idCliente"
                    + (situacao.PodeTrocarProfissional ? ", id_fun_fk = @idFuncionario" : "") + @"
                    WHERE id_vend = @id", conexao, transacao);
                comando.Parameters.AddWithValue("@idCliente",
                    idCliente is > 0 ? idCliente.Value : (object)DBNull.Value);
                comando.Parameters.AddWithValue("@idFuncionario", idFuncionario);
                comando.Parameters.AddWithValue("@id", idVenda);
                comando.ExecuteNonQuery();

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao salvar a venda: " + ex.Message);
            }
        }

        /// <summary>Critérios de filtragem do histórico de vendas.</summary>
        public class Filtro
        {
            public DateTime? De { get; set; }
            public DateTime? Ate { get; set; }
            public string? Cliente { get; set; }
            public int? IdFuncionario { get; set; }
            public string? FormaPagamento { get; set; }
            public int Limite { get; set; } = 500;

            public bool TemAlgum =>
                De.HasValue || Ate.HasValue
                || !string.IsNullOrWhiteSpace(Cliente)
                || (IdFuncionario ?? 0) > 0
                || !string.IsNullOrWhiteSpace(FormaPagamento);

            /// <summary>Descrição legível, usada no cabeçalho do relatório.</summary>
            public string Resumo()
            {
                if (!TemAlgum) return "Todas as vendas";

                var partes = new List<string>();
                if (De.HasValue && Ate.HasValue)
                    partes.Add($"de {De.Value:dd/MM/yyyy} a {Ate.Value.AddDays(-1):dd/MM/yyyy}");
                else if (De.HasValue) partes.Add($"a partir de {De.Value:dd/MM/yyyy}");
                else if (Ate.HasValue) partes.Add($"até {Ate.Value.AddDays(-1):dd/MM/yyyy}");

                if (!string.IsNullOrWhiteSpace(Cliente)) partes.Add($"cliente \"{Cliente}\"");
                if (!string.IsNullOrWhiteSpace(FormaPagamento))
                    partes.Add(Models.FormaPagamento.Rotulo(FormaPagamento));

                return string.Join(" · ", partes);
            }
        }

        /// <summary>Histórico, da venda mais recente para a mais antiga.</summary>
        public List<Venda> ListarTodas(Filtro? filtro = null)
        {
            filtro ??= new Filtro();
            var lista = new List<Venda>();

            var condicoes = "";
            if (filtro.De.HasValue) condicoes += " AND v.data_vend >= @de";
            if (filtro.Ate.HasValue) condicoes += " AND v.data_vend < @ate";
            if (!string.IsNullOrWhiteSpace(filtro.Cliente)) condicoes += " AND c.nome_cli LIKE @cliente";
            if ((filtro.IdFuncionario ?? 0) > 0)
                condicoes += @" AND (v.id_fun_fk = @idFuncionario
                                 OR EXISTS (SELECT 1 FROM venda_item fi
                                             WHERE fi.id_vend_fk = v.id_vend AND fi.id_fun_fk = @idFuncionario))";
            // Venda mista aparece no filtro de cada forma que ela usou.
            if (!string.IsNullOrWhiteSpace(filtro.FormaPagamento))
                condicoes += @" AND EXISTS (SELECT 1 FROM venda_pagamento fp
                                             WHERE fp.id_vend_fk = v.id_vend AND fp.forma = @forma)";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT v.*, c.nome_cli, f.nome_fun,
                       (SELECT COALESCE(SUM(vi.comissao * vi.quantidade), 0)
                          FROM venda_item vi WHERE vi.id_vend_fk = v.id_vend) AS comissao_total
                FROM venda v
                LEFT JOIN cliente c ON c.id_cli = v.id_cli_fk
                LEFT JOIN funcionario f ON f.id_fun = v.id_fun_fk
                WHERE 1 = 1 {condicoes}
                ORDER BY v.data_vend DESC, v.id_vend DESC
                LIMIT @limite", conexao);

            if (filtro.De.HasValue) comando.Parameters.AddWithValue("@de", filtro.De.Value);
            if (filtro.Ate.HasValue) comando.Parameters.AddWithValue("@ate", filtro.Ate.Value);
            if (!string.IsNullOrWhiteSpace(filtro.Cliente))
                comando.Parameters.AddWithValue("@cliente", $"%{filtro.Cliente.Trim()}%");
            if ((filtro.IdFuncionario ?? 0) > 0)
                comando.Parameters.AddWithValue("@idFuncionario", filtro.IdFuncionario!.Value);
            if (!string.IsNullOrWhiteSpace(filtro.FormaPagamento))
                comando.Parameters.AddWithValue("@forma", filtro.FormaPagamento);
            comando.Parameters.AddWithValue("@limite", filtro.Limite);

            using var leitor = comando.ExecuteReader();
            var iComissao = leitor.GetOrdinal("comissao_total");

            while (leitor.Read())
            {
                var venda = Ler(leitor);
                venda.ComissaoGravada = leitor.IsDBNull(iComissao) ? 0m : leitor.GetDecimal(iComissao);
                lista.Add(venda);
            }

            return lista;
        }

        public Venda? BuscarPorId(int id)
        {
            Venda? venda = null;

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(@"
                SELECT v.*, c.nome_cli, c.telefone_cli, f.nome_fun
                FROM venda v
                LEFT JOIN cliente c ON c.id_cli = v.id_cli_fk
                LEFT JOIN funcionario f ON f.id_fun = v.id_fun_fk
                WHERE v.id_vend = @id", conexao))
            {
                comando.Parameters.AddWithValue("@id", id);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read())
                {
                    venda = Ler(leitor);
                    venda.TelefoneCliente = DAOHelper.GetString(leitor, "telefone_cli");
                }
            }

            if (venda != null)
            {
                venda.Itens = ListarItens(conexao, id);
                venda.Pagamentos = ListarPagamentos(conexao, id);
            }
            return venda;
        }

        private static List<VendaPagamento> ListarPagamentos(MySqlConnection conexao, int idVenda)
        {
            var lista = new List<VendaPagamento>();

            using var comando = new MySqlCommand(
                "SELECT forma, valor, parcelas FROM venda_pagamento WHERE id_vend_fk = @id ORDER BY id_vend_pag",
                conexao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new VendaPagamento
                {
                    Forma = leitor.GetString(0),
                    Valor = leitor.GetDecimal(1),
                    Parcelas = leitor.IsDBNull(2) ? null : leitor.GetInt32(2)
                });
            }

            return lista;
        }

        public List<VendaItem> ListarItens(int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            return ListarItens(conexao, idVenda);
        }

        private static List<VendaItem> ListarItens(MySqlConnection conexao, int idVenda)
        {
            var itens = new List<VendaItem>();

            using var comando = new MySqlCommand(@"
                SELECT vi.*, f.nome_fun
                FROM venda_item vi
                LEFT JOIN funcionario f ON f.id_fun = vi.id_fun_fk
                WHERE vi.id_vend_fk = @id ORDER BY vi.id_vend_item", conexao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            var iRef = leitor.GetOrdinal("id_ref");
            var iSaldo = leitor.GetOrdinal("id_saldo_fk");
            var iFun = leitor.GetOrdinal("id_fun_fk");
            var iNomeFun = leitor.GetOrdinal("nome_fun");

            while (leitor.Read())
            {
                itens.Add(new VendaItem
                {
                    IdVendaItem = leitor.GetInt32("id_vend_item"),
                    IdVenda = leitor.GetInt32("id_vend_fk"),
                    TipoItem = leitor.GetString("tipo_item"),
                    IdReferencia = leitor.IsDBNull(iRef) ? null : leitor.GetInt32(iRef),
                    Descricao = leitor.GetString("descricao"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    ValorUnitario = leitor.GetDecimal("valor_unit"),
                    ComissaoUnitaria = leitor.GetDecimal("comissao"),
                    CobertoPlano = leitor.GetBoolean("coberto_plano"),
                    IdSaldo = leitor.IsDBNull(iSaldo) ? null : leitor.GetInt32(iSaldo),
                    IdFuncionario = leitor.IsDBNull(iFun) ? null : leitor.GetInt32(iFun),
                    NomeFuncionario = leitor.IsDBNull(iNomeFun) ? null : leitor.GetString(iNomeFun),
                    Desconto = leitor.GetDecimal("desconto")
                });
            }

            return itens;
        }

        /// <summary>Resumo dos itens, para caber na coluna descricao_vend.</summary>
        private static string MontarDescricao(Venda venda)
        {
            var texto = string.Join(", ", venda.Itens.Select(i =>
                i.Quantidade > 1 ? $"{i.Quantidade}x {i.Descricao}" : i.Descricao));

            return texto.Length <= 200 ? texto : texto[..197] + "...";
        }

        private static Venda Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            int? Inteiro(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetInt32(i);
            }

            decimal Decimal(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? 0m : leitor.GetDecimal(i);
            }

            var iData = leitor.GetOrdinal("data_vend");

            return new Venda
            {
                IdVenda = leitor.GetInt32("id_vend"),
                DataVenda = leitor.IsDBNull(iData) ? DateTime.MinValue : leitor.GetDateTime(iData),
                ValorVenda = Decimal("valor_vend"),
                Desconto = Decimal("desconto_vend"),
                FormaPagamentoVenda = Texto("forma_pagamento_vend"),
                QuantidadeParcelas = Inteiro("quant_parcela_vend"),
                Descricao = Texto("descricao_vend"),
                Status = Texto("status_vend") ?? StatusVenda.Confirmada,
                IdCliente = Inteiro("id_cli_fk"),
                IdFuncionario = Inteiro("id_fun_fk"),
                IdCaixa = Inteiro("id_caixa_fk"),
                NomeCliente = Texto("nome_cli"),
                NomeFuncionario = Texto("nome_fun")
            };
        }
    }
}
