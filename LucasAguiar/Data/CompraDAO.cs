using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class CompraDAO
    {
        private readonly Conexao _conexao;

        public CompraDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Grava a compra, lança a despesa correspondente e dá entrada no
        /// estoque (se já recebida) — tudo numa transação. Uma compra é saída de dinheiro:
        /// sem a despesa, o valor não apareceria em nenhum relatório financeiro.
        /// </summary>
        public int Inserir(Compra compra, int? idCaixaAberto = null, bool gerarDespesa = true, bool pago = true)
        {
            Preparar(compra);

            if (!FormaPagamento.Todas.Any(f => f.Valor == compra.FormaPagamento))
                throw new Exception("Selecione a forma de pagamento.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                int? idDespesa = null;

                if (gerarDespesa)
                {
                    var vencimento = (compra.VencimentoPrimeira ?? compra.DataCompra).Date;

                    using var cmdDespesa = new MySqlCommand(@"
                        INSERT INTO despesa
                            (data_desp, descricao, categoria, valor, forma_pagamento,
                             pago, id_forn_fk, id_caixa_fk, observacao,
                             data_vencimento, data_pagamento, parcelas)
                        VALUES
                            (@data, @descricao, 'PRODUTOS', @valor, @forma,
                             @pago, @idFornecedor, @idCaixa, 'Gerada automaticamente pela tela de Compras',
                             @vencimento, @pagamento, @parcelas);
                        SELECT LAST_INSERT_ID();", conexao, transacao);

                    // Mesma regra da despesa: paga vale o dia do pagamento, em aberto o vencimento.
                    cmdDespesa.Parameters.AddWithValue("@data", pago ? compra.DataCompra.Date : vencimento);
                    cmdDespesa.Parameters.AddWithValue("@descricao", DescricaoDespesa(compra));
                    cmdDespesa.Parameters.AddWithValue("@valor", compra.ValorCompra);
                    cmdDespesa.Parameters.AddWithValue("@forma", compra.FormaPagamento);
                    cmdDespesa.Parameters.AddWithValue("@pago", pago ? 1 : 0);
                    cmdDespesa.Parameters.AddWithValue("@idFornecedor",
                        compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
                    // Só amarra ao caixa se o dinheiro sair mesmo da gaveta.
                    cmdDespesa.Parameters.AddWithValue("@idCaixa",
                        (pago && compra.FormaPagamento == FormaPagamento.Dinheiro && idCaixaAberto > 0)
                            ? idCaixaAberto!.Value : (object)DBNull.Value);
                    cmdDespesa.Parameters.AddWithValue("@vencimento", vencimento);
                    cmdDespesa.Parameters.AddWithValue("@pagamento", pago ? compra.DataCompra.Date : DBNull.Value);
                    cmdDespesa.Parameters.AddWithValue("@parcelas", compra.Parcelas);

                    idDespesa = Convert.ToInt32(cmdDespesa.ExecuteScalar());
                }

                int idCompra;
                using (var cmdCompra = new MySqlCommand(@"
                    INSERT INTO compras
                        (data_comp, valor_comp, item_comp, quantidade,
                         id_prod_fk, id_forn_fk, id_fun_fk, id_desp_fk,
                         numero_nota, frete, desconto, observacao,
                         forma_pagamento, parcelas, vencimento_primeira,
                         recebida, data_recebimento)
                    VALUES
                        (@data, @valor, @item, @quantidade,
                         @idProduto, @idFornecedor, @idFuncionario, @idDespesa,
                         @nota, @frete, @desconto, @observacao,
                         @forma, @parcelas, @vencimento,
                         @recebida, @dataRecebimento);
                    SELECT LAST_INSERT_ID();", conexao, transacao))
                {
                    PreencherCabecalho(cmdCompra, compra);
                    cmdCompra.Parameters.AddWithValue("@idDespesa", (object?)idDespesa ?? DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@forma", compra.FormaPagamento);
                    cmdCompra.Parameters.AddWithValue("@recebida", compra.Recebida ? 1 : 0);
                    cmdCompra.Parameters.AddWithValue("@dataRecebimento",
                        compra.Recebida ? compra.DataCompra.Date : DBNull.Value);

                    idCompra = Convert.ToInt32(cmdCompra.ExecuteScalar());
                }

                // Compra de produto dá entrada no estoque; aguardando entrega, só no recebimento.
                GravarItens(idCompra, compra.Itens, conexao, transacao, compra.Recebida);

                transacao.Commit();
                return idCompra;
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao registrar a compra: " + ex.Message);
            }
        }

        public Compra? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT c.*, p.nome_prod, f.nome_forn, u.nome_fun
                FROM compras c
                LEFT JOIN produto p ON c.id_prod_fk = p.id_prod
                LEFT JOIN fornecedor f ON c.id_forn_fk = f.id_forn
                LEFT JOIN funcionario u ON c.id_fun_fk = u.id_fun
                WHERE c.id_comp = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);

            Compra? compra;
            using (var leitor = comando.ExecuteReader())
            {
                compra = leitor.Read() ? Ler(leitor) : null;
            }

            if (compra != null) compra.Itens = ListarItens(id, conexao, null);
            return compra;
        }

        public List<CompraItem> ListarItens(int idCompra)
        {
            using var conexao = _conexao.GetConnection();
            return ListarItens(idCompra, conexao, null);
        }

        /// <summary>
        /// Regrava a compra acertando o que ela movimentou: tira do estoque
        /// a entrada antiga, dá a nova e acompanha a despesa gerada.
        /// </summary>
        public void Atualizar(Compra compra)
        {
            Preparar(compra);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var anterior = LerParaAlterar(compra.IdCompra, conexao, transacao)
                    ?? throw new Exception("Compra não encontrada.");

                if (anterior.IdDespesa.HasValue && compra.ValorCompra != anterior.ValorCompra
                    && DespesaEmCaixaFechado(anterior.IdDespesa.Value, conexao, transacao))
                {
                    throw new Exception(
                        "A despesa desta compra já entrou num caixa fechado: o valor não pode mais ser alterado.");
                }

                // Recebimento nao muda aqui: e feito pelo Receber.
                if (anterior.Recebida)
                {
                    foreach (var item in ListarItens(compra.IdCompra, conexao, transacao))
                        MoverEstoque(item.IdProduto ?? 0, -item.Quantidade, conexao, transacao);
                }

                using (var limpar = new MySqlCommand(
                    "DELETE FROM compra_item WHERE id_comp_fk = @id", conexao, transacao))
                {
                    limpar.Parameters.AddWithValue("@id", compra.IdCompra);
                    limpar.ExecuteNonQuery();
                }

                GravarItens(compra.IdCompra, compra.Itens, conexao, transacao, anterior.Recebida);

                // Forma de pagamento nao muda aqui: ja esta na despesa.
                using (var cmdCompra = new MySqlCommand(@"
                    UPDATE compras SET
                        data_comp = @data, valor_comp = @valor, item_comp = @item,
                        quantidade = @quantidade, id_prod_fk = @idProduto,
                        id_forn_fk = @idFornecedor, id_fun_fk = @idFuncionario,
                        numero_nota = @nota, frete = @frete, desconto = @desconto,
                        observacao = @observacao, parcelas = @parcelas, vencimento_primeira = @vencimento
                    WHERE id_comp = @id", conexao, transacao))
                {
                    PreencherCabecalho(cmdCompra, compra);
                    cmdCompra.Parameters.AddWithValue("@id", compra.IdCompra);
                    cmdCompra.ExecuteNonQuery();
                }

                if (anterior.IdDespesa.HasValue)
                {
                    var vencimento = (compra.VencimentoPrimeira ?? compra.DataCompra).Date;

                    using var cmdDespesa = new MySqlCommand(@"
                        UPDATE despesa SET
                            data_desp = CASE WHEN pago = 1 THEN @data ELSE @vencimento END,
                            data_pagamento = CASE WHEN pago = 1 THEN @data ELSE data_pagamento END,
                            data_vencimento = @vencimento, parcelas = @parcelas,
                            descricao = @descricao, valor = @valor, id_forn_fk = @idFornecedor
                        WHERE id_desp = @idDespesa", conexao, transacao);

                    cmdDespesa.Parameters.AddWithValue("@data", compra.DataCompra.Date);
                    cmdDespesa.Parameters.AddWithValue("@vencimento", vencimento);
                    cmdDespesa.Parameters.AddWithValue("@parcelas", compra.Parcelas);
                    cmdDespesa.Parameters.AddWithValue("@descricao", DescricaoDespesa(compra));
                    cmdDespesa.Parameters.AddWithValue("@valor", compra.ValorCompra);
                    cmdDespesa.Parameters.AddWithValue("@idFornecedor",
                        compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
                    cmdDespesa.Parameters.AddWithValue("@idDespesa", anterior.IdDespesa.Value);
                    cmdDespesa.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao atualizar a compra: " + ex.Message);
            }
        }

        /// <summary>
        /// Desfaz a compra inteira: estorna a entrada no estoque e apaga a
        /// despesa que ela lançou.
        /// </summary>
        public void Excluir(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var compra = LerParaAlterar(id, conexao, transacao)
                    ?? throw new Exception("Compra não encontrada.");

                if (compra.IdDespesa.HasValue
                    && DespesaEmCaixaFechado(compra.IdDespesa.Value, conexao, transacao))
                {
                    throw new Exception(
                        "A despesa desta compra já entrou num caixa fechado: a compra não pode mais ser excluída.");
                }

                // Aguardando entrega nao entrou no estoque: nada a estornar.
                if (compra.Recebida)
                {
                    foreach (var item in ListarItens(id, conexao, transacao))
                        MoverEstoque(item.IdProduto ?? 0, -item.Quantidade, conexao, transacao);
                }

                // A compra sai antes da despesa: compras.id_desp_fk aponta para ela.
                // Os itens saem junto, pelo ON DELETE CASCADE de compra_item.
                using (var cmdCompra = new MySqlCommand(
                    "DELETE FROM compras WHERE id_comp = @id", conexao, transacao))
                {
                    cmdCompra.Parameters.AddWithValue("@id", id);
                    cmdCompra.ExecuteNonQuery();
                }

                if (compra.IdDespesa.HasValue)
                {
                    using var cmdDespesa = new MySqlCommand(
                        "DELETE FROM despesa WHERE id_desp = @idDespesa", conexao, transacao);
                    cmdDespesa.Parameters.AddWithValue("@idDespesa", compra.IdDespesa.Value);
                    cmdDespesa.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao excluir a compra: " + ex.Message);
            }
        }

        /// <summary>
        /// Recebe a mercadoria de uma compra aguardando entrega: dá a
        /// entrada no estoque dos itens e marca a data do recebimento.
        /// </summary>
        public void Receber(int id, string? usuario = null)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var compra = LerParaAlterar(id, conexao, transacao)
                    ?? throw new Exception("Compra não encontrada.");

                if (compra.Recebida)
                    throw new Exception("Esta compra já foi recebida.");

                foreach (var item in ListarItens(id, conexao, transacao))
                {
                    MoverEstoque(
                        item.IdProduto ?? 0, item.Quantidade, conexao, transacao,
                        $"Compra #{id}: {item.Descricao}", id, usuario);
                }

                using (var comando = new MySqlCommand(@"
                    UPDATE compras SET recebida = 1, data_recebimento = CURDATE()
                    WHERE id_comp = @id", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@id", id);
                    comando.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao receber a compra: " + ex.Message);
            }
        }

        /// <summary>
        /// Confere a grade e calcula o cabecalho: total da nota e o resumo
        /// (item_comp, quantidade, id_prod_fk) que a listagem e as
        /// exportacoes leem.
        /// </summary>
        private static void Preparar(Compra compra)
        {
            if (compra.Itens.Count == 0)
                throw new Exception("Adicione ao menos um item à compra.");

            foreach (var item in compra.Itens)
            {
                if (string.IsNullOrWhiteSpace(item.Descricao)) throw new Exception("Informe a descrição de cada item.");
                if (item.Quantidade <= 0) throw new Exception($"A quantidade de {item.Descricao} deve ser maior que zero.");
                if (item.CustoUnitario < 0) throw new Exception($"O custo de {item.Descricao} não pode ser negativo.");
                item.Descricao = item.Descricao.Trim();
            }

            if (compra.Frete < 0) throw new Exception("O frete não pode ser negativo.");
            if (compra.Desconto < 0) throw new Exception("O desconto não pode ser negativo.");
            if (compra.Desconto > compra.TotalItens + compra.Frete)
                throw new Exception("O desconto é maior que o valor da compra.");
            if (compra.Parcelas is < 1 or > 120) throw new Exception("As parcelas devem ficar entre 1 e 120.");

            compra.ValorCompra = compra.TotalNota;
            if (compra.ValorCompra <= 0)
                throw new Exception("O valor da compra deve ser maior que zero.");

            var resumo = string.Join(", ", compra.Itens.Select(i =>
                i.Quantidade > 1 ? $"{i.Quantidade}x {i.Descricao}" : i.Descricao));
            compra.ItemCompra = resumo.Length <= 200 ? resumo : resumo[..197] + "...";
            compra.Quantidade = compra.Itens.Sum(i => i.Quantidade);
            compra.IdProduto = compra.Itens.FirstOrDefault(i => i.IdProduto > 0)?.IdProduto ?? 0;
        }

        private static string DescricaoDespesa(Compra compra) =>
            (string.IsNullOrWhiteSpace(compra.NumeroNota) ? "Compra: " : $"Compra NF {compra.NumeroNota.Trim()}: ")
            + (string.IsNullOrWhiteSpace(compra.ItemCompra) ? "mercadoria" : compra.ItemCompra);

        private static void PreencherCabecalho(MySqlCommand comando, Compra compra)
        {
            comando.Parameters.AddWithValue("@data", compra.DataCompra.Date);
            comando.Parameters.AddWithValue("@valor", compra.ValorCompra);
            comando.Parameters.AddWithValue("@item",
                string.IsNullOrWhiteSpace(compra.ItemCompra) ? (object)DBNull.Value : compra.ItemCompra);
            comando.Parameters.AddWithValue("@quantidade", compra.Quantidade);
            comando.Parameters.AddWithValue("@idProduto",
                compra.IdProduto > 0 ? compra.IdProduto : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@idFornecedor",
                compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@idFuncionario",
                compra.IdFuncionario > 0 ? compra.IdFuncionario : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@nota",
                string.IsNullOrWhiteSpace(compra.NumeroNota) ? (object)DBNull.Value : compra.NumeroNota.Trim());
            comando.Parameters.AddWithValue("@frete", compra.Frete);
            comando.Parameters.AddWithValue("@desconto", compra.Desconto);
            comando.Parameters.AddWithValue("@observacao",
                string.IsNullOrWhiteSpace(compra.Observacao) ? (object)DBNull.Value : compra.Observacao.Trim());
            comando.Parameters.AddWithValue("@parcelas", compra.Parcelas);
            comando.Parameters.AddWithValue("@vencimento", compra.VencimentoPrimeira?.Date ?? (object)DBNull.Value);
        }

        /// <summary>
        /// Grava os itens e da entrada no estoque. O custo do item vira o
        /// preco de custo do produto: e o ultimo preco pago a ele.
        /// Sem <paramref name="moverEstoque"/>, a entrada fica para o Receber.
        /// </summary>
        private static void GravarItens(
            int idCompra, IEnumerable<CompraItem> itens, MySqlConnection conexao, MySqlTransaction transacao,
            bool moverEstoque = true)
        {
            foreach (var item in itens)
            {
                using (var inserir = new MySqlCommand(@"
                    INSERT INTO compra_item (id_comp_fk, id_prod_fk, descricao, quantidade, custo_unit)
                    VALUES (@idCompra, @idProduto, @descricao, @quantidade, @custo)", conexao, transacao))
                {
                    inserir.Parameters.AddWithValue("@idCompra", idCompra);
                    inserir.Parameters.AddWithValue("@idProduto", item.IdProduto > 0 ? item.IdProduto : (object)DBNull.Value);
                    inserir.Parameters.AddWithValue("@descricao", item.Descricao);
                    inserir.Parameters.AddWithValue("@quantidade", item.Quantidade);
                    inserir.Parameters.AddWithValue("@custo", item.CustoUnitario);
                    inserir.ExecuteNonQuery();
                }

                if (item.IdProduto is not > 0) continue;

                // Com o numero da compra: no extrato do produto, a entrada
                // diz de onde veio em vez de so "compra recebida".
                if (moverEstoque)
                {
                    MoverEstoque(
                        item.IdProduto.Value, item.Quantidade, conexao, transacao,
                        $"Compra #{idCompra}: {item.Descricao}", idCompra);
                }

                using var custo = new MySqlCommand(
                    "UPDATE produto SET preco_custo = @custo WHERE id_prod = @idProduto", conexao, transacao);
                custo.Parameters.AddWithValue("@custo", item.CustoUnitario);
                custo.Parameters.AddWithValue("@idProduto", item.IdProduto.Value);
                custo.ExecuteNonQuery();
            }
        }

        private static List<CompraItem> ListarItens(int idCompra, MySqlConnection conexao, MySqlTransaction? transacao)
        {
            var lista = new List<CompraItem>();

            using var comando = new MySqlCommand(
                "SELECT * FROM compra_item WHERE id_comp_fk = @id ORDER BY id_comp_item", conexao, transacao);
            comando.Parameters.AddWithValue("@id", idCompra);

            using var leitor = comando.ExecuteReader();
            var iProduto = leitor.GetOrdinal("id_prod_fk");

            while (leitor.Read())
            {
                lista.Add(new CompraItem
                {
                    IdItem = leitor.GetInt32("id_comp_item"),
                    IdProduto = leitor.IsDBNull(iProduto) ? null : leitor.GetInt32(iProduto),
                    Descricao = leitor.GetString("descricao"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    CustoUnitario = leitor.GetDecimal("custo_unit")
                });
            }

            return lista;
        }

        /// <summary>Lê a compra travando a linha até o fim da transação.</summary>
        private static Compra? LerParaAlterar(int id, MySqlConnection conexao, MySqlTransaction transacao)
        {
            using var comando = new MySqlCommand(@"
                SELECT c.*, NULL AS nome_prod, NULL AS nome_forn, NULL AS nome_fun
                FROM compras c WHERE c.id_comp = @id FOR UPDATE", conexao, transacao);
            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        private static bool DespesaEmCaixaFechado(int idDespesa, MySqlConnection conexao, MySqlTransaction transacao)
        {
            using var comando = new MySqlCommand(@"
                SELECT cx.status
                FROM despesa d
                JOIN caixa cx ON cx.id_caixa = d.id_caixa_fk
                WHERE d.id_desp = @idDespesa", conexao, transacao);
            comando.Parameters.AddWithValue("@idDespesa", idDespesa);

            return comando.ExecuteScalar() is string status && status == "FECHADO";
        }

        /// <summary>
        /// Entrada e correcao de entrada pela compra.
        ///
        /// Era um UPDATE direto no saldo. Passou pelo EstoqueDAO para a
        /// linha ficar no extrato: sem ela, o produto aparecia na
        /// prateleira sem que nada dissesse de onde veio.
        /// </summary>
        private static void MoverEstoque(
            int idProduto, int quantidade,
            MySqlConnection conexao, MySqlTransaction transacao,
            string? motivo = null, int? idCompra = null, string? usuario = null)
        {
            if (idProduto <= 0 || quantidade == 0) return;

            EstoqueDAO.RegistrarNaTransacao(
                conexao, transacao, idProduto,
                quantidade > 0 ? TipoMovimentoEstoque.Entrada : TipoMovimentoEstoque.Ajuste,
                quantidade,
                motivo ?? "Compra recebida", idCompra, usuario);
        }

        public List<Compra> ListarTodos(DateTime? de = null, DateTime? ate = null)
        {
            var lista = new List<Compra>();

            var condicoes = "";
            if (de.HasValue) condicoes += " AND c.data_comp >= @de";
            if (ate.HasValue) condicoes += " AND c.data_comp < @ate";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT c.*, p.nome_prod, f.nome_forn, u.nome_fun,
                       d.forma_pagamento AS forma_desp, d.pago AS pago_desp,
                       (SELECT GROUP_CONCAT(CAST(ci.id_prod_fk AS CHAR))
                          FROM compra_item ci
                         WHERE ci.id_comp_fk = c.id_comp AND ci.id_prod_fk IS NOT NULL) AS ids_prod
                FROM compras c
                LEFT JOIN produto p ON c.id_prod_fk = p.id_prod
                LEFT JOIN fornecedor f ON c.id_forn_fk = f.id_forn
                LEFT JOIN funcionario u ON c.id_fun_fk = u.id_fun
                LEFT JOIN despesa d ON c.id_desp_fk = d.id_desp
                WHERE 1 = 1 {condicoes}
                ORDER BY c.data_comp DESC, c.id_comp DESC", conexao);

            if (de.HasValue) comando.Parameters.AddWithValue("@de", de.Value.Date);
            if (ate.HasValue) comando.Parameters.AddWithValue("@ate", ate.Value.Date);

            using var leitor = comando.ExecuteReader();
            var iForma = leitor.GetOrdinal("forma_desp");
            var iPago = leitor.GetOrdinal("pago_desp");
            var iIds = leitor.GetOrdinal("ids_prod");

            while (leitor.Read())
            {
                var compra = Ler(leitor);
                compra.FormaPagamentoDespesa = leitor.IsDBNull(iForma) ? null : leitor.GetString(iForma);
                compra.Pago = leitor.IsDBNull(iPago) ? null : leitor.GetBoolean(iPago);
                compra.IdsProdutos = leitor.IsDBNull(iIds)
                    ? new()
                    : leitor.GetString(iIds).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
                lista.Add(compra);
            }
            return lista;
        }

        private static Compra Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            int Inteiro(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? 0 : leitor.GetInt32(i);
            }

            var iData = leitor.GetOrdinal("data_comp");
            var iValor = leitor.GetOrdinal("valor_comp");
            var iDesp = leitor.GetOrdinal("id_desp_fk");

            return new Compra
            {
                IdCompra = leitor.GetInt32("id_comp"),
                DataCompra = leitor.IsDBNull(iData) ? DateTime.MinValue : leitor.GetDateTime(iData),
                ValorCompra = leitor.IsDBNull(iValor) ? 0m : leitor.GetDecimal(iValor),
                ItemCompra = Texto("item_comp"),
                Quantidade = Inteiro("quantidade"),
                IdProduto = Inteiro("id_prod_fk"),
                IdFornecedor = Inteiro("id_forn_fk"),
                IdFuncionario = Inteiro("id_fun_fk"),
                IdDespesa = leitor.IsDBNull(iDesp) ? null : leitor.GetInt32(iDesp),
                NumeroNota = Texto("numero_nota"),
                Frete = leitor.GetDecimal("frete"),
                Desconto = leitor.GetDecimal("desconto"),
                Observacao = Texto("observacao"),
                FormaPagamento = Texto("forma_pagamento") ?? FormaPagamento.Dinheiro,
                Parcelas = Inteiro("parcelas"),
                VencimentoPrimeira = DAOHelper.GetDateTime(leitor, "vencimento_primeira"),
                Recebida = leitor.GetBoolean("recebida"),
                DataRecebimento = DAOHelper.GetDateTime(leitor, "data_recebimento"),
                NomeProduto = Texto("nome_prod"),
                NomeFornecedor = Texto("nome_forn"),
                NomeFuncionario = Texto("nome_fun")
            };
        }
    }
}
