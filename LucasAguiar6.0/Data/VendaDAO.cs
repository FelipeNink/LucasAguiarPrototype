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

            if (string.IsNullOrWhiteSpace(venda.FormaPagamentoVenda))
                throw new Exception("Selecione a forma de pagamento.");

            // O negocio trabalha com comissao: sem profissional nao ha a quem pagar.
            if (!venda.IdFuncionario.HasValue || venda.IdFuncionario.Value <= 0)
                throw new Exception("Selecione o profissional que realizou o atendimento.");

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

                foreach (var item in venda.Itens)
                {
                    using (var comando = new MySqlCommand(@"
                        INSERT INTO venda_item
                            (id_vend_fk, tipo_item, id_ref, descricao, quantidade,
                             valor_unit, valor_total, comissao, coberto_plano, id_saldo_fk)
                        VALUES
                            (@idVenda, @tipo, @idRef, @descricao, @qtd,
                             @unit, @total, @comissao, @coberto, @idSaldo)", conexao, transacao))
                    {
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

                // Venda de plano gera a assinatura com os creditos.
                if (venda.PlanoVendido != null && venda.IdCliente.HasValue)
                {
                    AssinaturaDAO.CriarNaTransacao(
                        conexao, transacao,
                        venda.IdCliente.Value,
                        venda.PlanoVendido.IdPlano,
                        venda.PlanoVendido.ValorPago,
                        idVenda,
                        venda.PlanoVendido.Componentes);
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

                // Devolve os créditos de plano consumidos nesta venda.
                using (var comando = new MySqlCommand(@"
                    UPDATE assinatura_saldo s
                    JOIN venda_item vi ON vi.id_saldo_fk = s.id_saldo
                    SET s.quantidade_usada = GREATEST(0, s.quantidade_usada - vi.quantidade)
                    WHERE vi.id_vend_fk = @id AND vi.coberto_plano = 1", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@id", idVenda);
                    comando.ExecuteNonQuery();
                }

                // Devolve ao estoque os produtos que a venda tinha baixado.
                using (var comando = new MySqlCommand(@"
                    UPDATE produto p
                    JOIN venda_item vi ON vi.id_ref = p.id_prod
                    SET p.quantidade_prod = COALESCE(p.quantidade_prod, 0) + vi.quantidade
                    WHERE vi.id_vend_fk = @id AND vi.tipo_item = 'PRODUTO'", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@id", idVenda);
                    comando.ExecuteNonQuery();
                }

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
            if ((filtro.IdFuncionario ?? 0) > 0) condicoes += " AND v.id_fun_fk = @idFuncionario";
            if (!string.IsNullOrWhiteSpace(filtro.FormaPagamento)) condicoes += " AND v.forma_pagamento_vend = @forma";

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
                SELECT v.*, c.nome_cli, f.nome_fun
                FROM venda v
                LEFT JOIN cliente c ON c.id_cli = v.id_cli_fk
                LEFT JOIN funcionario f ON f.id_fun = v.id_fun_fk
                WHERE v.id_vend = @id", conexao))
            {
                comando.Parameters.AddWithValue("@id", id);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read()) venda = Ler(leitor);
            }

            if (venda != null) venda.Itens = ListarItens(conexao, id);
            return venda;
        }

        public List<VendaItem> ListarItens(int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            return ListarItens(conexao, idVenda);
        }

        private static List<VendaItem> ListarItens(MySqlConnection conexao, int idVenda)
        {
            var itens = new List<VendaItem>();

            using var comando = new MySqlCommand(
                "SELECT * FROM venda_item WHERE id_vend_fk = @id ORDER BY id_vend_item", conexao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            var iRef = leitor.GetOrdinal("id_ref");
            var iSaldo = leitor.GetOrdinal("id_saldo_fk");

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
                    IdSaldo = leitor.IsDBNull(iSaldo) ? null : leitor.GetInt32(iSaldo)
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
                NomeCliente = Texto("nome_cli"),
                NomeFuncionario = Texto("nome_fun")
            };
        }
    }
}
