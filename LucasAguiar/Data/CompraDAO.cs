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
        /// estoque — tudo numa transação. Uma compra é saída de dinheiro:
        /// sem a despesa, o valor não apareceria em nenhum relatório financeiro.
        /// </summary>
        public int Inserir(Compra compra, int? idCaixaAberto = null, bool gerarDespesa = true,
                           string formaPagamento = FormaPagamento.Dinheiro, bool pago = true)
        {
            if (compra.ValorCompra <= 0)
                throw new Exception("O valor da compra deve ser maior que zero.");

            if (compra.Quantidade <= 0)
                throw new Exception("A quantidade deve ser maior que zero.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                int? idDespesa = null;

                if (gerarDespesa)
                {
                    var descricao = string.IsNullOrWhiteSpace(compra.ItemCompra)
                        ? "Compra de mercadoria"
                        : $"Compra: {compra.ItemCompra}";

                    using var cmdDespesa = new MySqlCommand(@"
                        INSERT INTO despesa
                            (data_desp, descricao, categoria, valor, forma_pagamento,
                             pago, id_forn_fk, id_caixa_fk, observacao)
                        VALUES
                            (@data, @descricao, 'PRODUTOS', @valor, @forma,
                             @pago, @idFornecedor, @idCaixa, 'Gerada automaticamente pela tela de Compras');
                        SELECT LAST_INSERT_ID();", conexao, transacao);

                    cmdDespesa.Parameters.AddWithValue("@data", compra.DataCompra.Date);
                    cmdDespesa.Parameters.AddWithValue("@descricao", descricao);
                    cmdDespesa.Parameters.AddWithValue("@valor", compra.ValorCompra);
                    cmdDespesa.Parameters.AddWithValue("@forma", formaPagamento);
                    cmdDespesa.Parameters.AddWithValue("@pago", pago ? 1 : 0);
                    cmdDespesa.Parameters.AddWithValue("@idFornecedor",
                        compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
                    // Só amarra ao caixa se o dinheiro sair mesmo da gaveta.
                    cmdDespesa.Parameters.AddWithValue("@idCaixa",
                        (pago && formaPagamento == FormaPagamento.Dinheiro && idCaixaAberto > 0)
                            ? idCaixaAberto!.Value : (object)DBNull.Value);

                    idDespesa = Convert.ToInt32(cmdDespesa.ExecuteScalar());
                }

                int idCompra;
                using (var cmdCompra = new MySqlCommand(@"
                    INSERT INTO compras
                        (data_comp, valor_comp, item_comp, quantidade,
                         id_prod_fk, id_forn_fk, id_fun_fk, id_desp_fk)
                    VALUES
                        (@data, @valor, @item, @quantidade,
                         @idProduto, @idFornecedor, @idFuncionario, @idDespesa);
                    SELECT LAST_INSERT_ID();", conexao, transacao))
                {
                    cmdCompra.Parameters.AddWithValue("@data", compra.DataCompra.Date);
                    cmdCompra.Parameters.AddWithValue("@valor", compra.ValorCompra);
                    cmdCompra.Parameters.AddWithValue("@item",
                        string.IsNullOrWhiteSpace(compra.ItemCompra) ? (object)DBNull.Value : compra.ItemCompra);
                    cmdCompra.Parameters.AddWithValue("@quantidade", compra.Quantidade);
                    cmdCompra.Parameters.AddWithValue("@idProduto",
                        compra.IdProduto > 0 ? compra.IdProduto : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@idFornecedor",
                        compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@idFuncionario",
                        compra.IdFuncionario > 0 ? compra.IdFuncionario : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@idDespesa", (object?)idDespesa ?? DBNull.Value);

                    idCompra = Convert.ToInt32(cmdCompra.ExecuteScalar());
                }

                // Compra de produto dá entrada no estoque.
                if (compra.IdProduto > 0)
                {
                    using var cmdEstoque = new MySqlCommand(@"
                        UPDATE produto
                        SET quantidade_prod = COALESCE(quantidade_prod, 0) + @quantidade
                        WHERE id_prod = @idProduto", conexao, transacao);

                    cmdEstoque.Parameters.AddWithValue("@quantidade", compra.Quantidade);
                    cmdEstoque.Parameters.AddWithValue("@idProduto", compra.IdProduto);
                    cmdEstoque.ExecuteNonQuery();
                }

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

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>
        /// Regrava a compra acertando o que ela movimentou: tira do estoque
        /// a entrada antiga, dá a nova e acompanha a despesa gerada.
        /// </summary>
        public void Atualizar(Compra compra)
        {
            if (compra.ValorCompra <= 0)
                throw new Exception("O valor da compra deve ser maior que zero.");

            if (compra.Quantidade <= 0)
                throw new Exception("A quantidade deve ser maior que zero.");

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

                MoverEstoque(anterior.IdProduto, -anterior.Quantidade, conexao, transacao);
                MoverEstoque(compra.IdProduto, compra.Quantidade, conexao, transacao);

                using (var cmdCompra = new MySqlCommand(@"
                    UPDATE compras SET
                        data_comp = @data, valor_comp = @valor, item_comp = @item,
                        quantidade = @quantidade, id_prod_fk = @idProduto,
                        id_forn_fk = @idFornecedor, id_fun_fk = @idFuncionario
                    WHERE id_comp = @id", conexao, transacao))
                {
                    cmdCompra.Parameters.AddWithValue("@data", compra.DataCompra.Date);
                    cmdCompra.Parameters.AddWithValue("@valor", compra.ValorCompra);
                    cmdCompra.Parameters.AddWithValue("@item",
                        string.IsNullOrWhiteSpace(compra.ItemCompra) ? (object)DBNull.Value : compra.ItemCompra);
                    cmdCompra.Parameters.AddWithValue("@quantidade", compra.Quantidade);
                    cmdCompra.Parameters.AddWithValue("@idProduto",
                        compra.IdProduto > 0 ? compra.IdProduto : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@idFornecedor",
                        compra.IdFornecedor > 0 ? compra.IdFornecedor : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@idFuncionario",
                        compra.IdFuncionario > 0 ? compra.IdFuncionario : (object)DBNull.Value);
                    cmdCompra.Parameters.AddWithValue("@id", compra.IdCompra);
                    cmdCompra.ExecuteNonQuery();
                }

                if (anterior.IdDespesa.HasValue)
                {
                    using var cmdDespesa = new MySqlCommand(@"
                        UPDATE despesa SET
                            data_desp = @data, descricao = @descricao,
                            valor = @valor, id_forn_fk = @idFornecedor
                        WHERE id_desp = @idDespesa", conexao, transacao);

                    cmdDespesa.Parameters.AddWithValue("@data", compra.DataCompra.Date);
                    cmdDespesa.Parameters.AddWithValue("@descricao",
                        string.IsNullOrWhiteSpace(compra.ItemCompra)
                            ? "Compra de mercadoria"
                            : $"Compra: {compra.ItemCompra}");
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

                MoverEstoque(compra.IdProduto, -compra.Quantidade, conexao, transacao);

                // A compra sai antes da despesa: compras.id_desp_fk aponta para ela.
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

        private static void MoverEstoque(int idProduto, int quantidade, MySqlConnection conexao, MySqlTransaction transacao)
        {
            if (idProduto <= 0 || quantidade == 0) return;

            using var comando = new MySqlCommand(@"
                UPDATE produto
                SET quantidade_prod = COALESCE(quantidade_prod, 0) + @quantidade
                WHERE id_prod = @idProduto", conexao, transacao);

            comando.Parameters.AddWithValue("@quantidade", quantidade);
            comando.Parameters.AddWithValue("@idProduto", idProduto);
            comando.ExecuteNonQuery();
        }

        public List<Compra> ListarTodos(DateTime? de = null, DateTime? ate = null)
        {
            var lista = new List<Compra>();

            var condicoes = "";
            if (de.HasValue) condicoes += " AND c.data_comp >= @de";
            if (ate.HasValue) condicoes += " AND c.data_comp < @ate";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT c.*, p.nome_prod, f.nome_forn, u.nome_fun
                FROM compras c
                LEFT JOIN produto p ON c.id_prod_fk = p.id_prod
                LEFT JOIN fornecedor f ON c.id_forn_fk = f.id_forn
                LEFT JOIN funcionario u ON c.id_fun_fk = u.id_fun
                WHERE 1 = 1 {condicoes}
                ORDER BY c.data_comp DESC, c.id_comp DESC", conexao);

            if (de.HasValue) comando.Parameters.AddWithValue("@de", de.Value.Date);
            if (ate.HasValue) comando.Parameters.AddWithValue("@ate", ate.Value.Date);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
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
                NomeProduto = Texto("nome_prod"),
                NomeFornecedor = Texto("nome_forn"),
                NomeFuncionario = Texto("nome_fun")
            };
        }
    }
}
