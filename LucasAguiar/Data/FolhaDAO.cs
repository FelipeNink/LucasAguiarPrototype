using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Folha de pagamento: salário fixo somado às comissões do mês.
    /// O pagamento só acontece aqui, e gera a despesa correspondente.
    /// </summary>
    public class FolhaDAO
    {
        private readonly Conexao _conexao;
        private readonly CaixaDAO _caixaDAO;

        public FolhaDAO(Conexao conexao, CaixaDAO caixaDAO)
        {
            _conexao = conexao;
            _caixaDAO = caixaDAO;
        }

        public class LinhaFolha
        {
            public int IdFuncionario { get; set; }
            public string NomeFuncionario { get; set; } = "";

            public decimal SalarioFixo { get; set; }
            public decimal Comissoes { get; set; }

            /// <summary>Serviços executados no mês — produtos não contam.</summary>
            public int ServicosRealizados { get; set; }

            /// <summary>
            /// Produtos vendidos pelo profissional. Registrado mesmo quando
            /// não gera comissão, para acompanhar quem vende balcão.
            /// </summary>
            public int ProdutosVendidos { get; set; }
            public decimal ValorProdutos { get; set; }

            public decimal ValorServicos { get; set; }

            public int Atendimentos { get; set; }
            public decimal TotalVendido { get; set; }

            public bool Pago { get; set; }
            public DateTime? DataPagamento { get; set; }
            public decimal ValorPago { get; set; }

            public decimal TotalAReceber => SalarioFixo + Comissoes;
        }

        /// <summary>Competência no formato AAAA-MM.</summary>
        public static string Competencia(DateTime mes) => mes.ToString("yyyy-MM");

        public List<LinhaFolha> Montar(DateTime mes)
        {
            var inicio = new DateTime(mes.Year, mes.Month, 1);
            var fim = inicio.AddMonths(1);
            var competencia = Competencia(inicio);

            var linhas = new List<LinhaFolha>();

            using var conexao = _conexao.GetConnection();

            // Todos os funcionarios entram, mesmo sem venda no mes: quem tem
            // salario fixo precisa aparecer para ser pago.
            using (var comando = new MySqlCommand(@"
                SELECT f.id_fun, f.nome_fun, f.salario_fixo
                FROM funcionario f
                ORDER BY f.nome_fun", conexao))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    linhas.Add(new LinhaFolha
                    {
                        IdFuncionario = leitor.GetInt32("id_fun"),
                        NomeFuncionario = leitor.IsDBNull(leitor.GetOrdinal("nome_fun"))
                            ? "(sem nome)" : leitor.GetString("nome_fun"),
                        SalarioFixo = leitor.GetDecimal("salario_fixo")
                    });
                }
            }

            foreach (var linha in linhas)
            {
                using (var comando = new MySqlCommand(@"
                    SELECT
                      COALESCE(SUM(vi.comissao * vi.quantidade), 0) comissoes,
                      COALESCE(SUM(CASE WHEN vi.tipo_item <> 'PRODUTO' THEN vi.quantidade ELSE 0 END), 0) servicos,
                      COALESCE(SUM(CASE WHEN vi.tipo_item <> 'PRODUTO' THEN vi.valor_total ELSE 0 END), 0) valor_servicos,
                      COALESCE(SUM(CASE WHEN vi.tipo_item = 'PRODUTO' THEN vi.quantidade ELSE 0 END), 0) produtos,
                      COALESCE(SUM(CASE WHEN vi.tipo_item = 'PRODUTO' THEN vi.valor_total ELSE 0 END), 0) valor_produtos,
                      COALESCE(SUM(vi.valor_total), 0) vendido,
                      COUNT(DISTINCT v.id_vend) atendimentos
                    FROM venda v
                    JOIN venda_item vi ON vi.id_vend_fk = v.id_vend
                    WHERE v.id_fun_fk = @id
                      AND v.data_vend >= @inicio AND v.data_vend < @fim
                      AND v.status_vend <> 'CANCELADA'", conexao))
                {
                    comando.Parameters.AddWithValue("@id", linha.IdFuncionario);
                    comando.Parameters.AddWithValue("@inicio", inicio);
                    comando.Parameters.AddWithValue("@fim", fim);

                    using var leitor = comando.ExecuteReader();
                    if (leitor.Read())
                    {
                        linha.Comissoes = leitor.GetDecimal("comissoes");
                        linha.ServicosRealizados = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("servicos")));
                        linha.ValorServicos = leitor.GetDecimal("valor_servicos");
                        linha.ProdutosVendidos = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("produtos")));
                        linha.ValorProdutos = leitor.GetDecimal("valor_produtos");
                        linha.TotalVendido = leitor.GetDecimal("vendido");
                        linha.Atendimentos = leitor.GetInt32("atendimentos");
                    }
                }

                using (var comando = new MySqlCommand(@"
                    SELECT data_pgto, total FROM folha_pagamento
                    WHERE id_fun_fk = @id AND competencia = @competencia", conexao))
                {
                    comando.Parameters.AddWithValue("@id", linha.IdFuncionario);
                    comando.Parameters.AddWithValue("@competencia", competencia);

                    using var leitor = comando.ExecuteReader();
                    if (leitor.Read())
                    {
                        linha.Pago = true;
                        linha.DataPagamento = leitor.GetDateTime("data_pgto");
                        linha.ValorPago = leitor.GetDecimal("total");
                    }
                }
            }

            return linhas;
        }

        /// <summary>
        /// Registra o pagamento e lança a despesa na categoria Salários,
        /// numa transação. A restrição de unicidade impede pagar duas vezes
        /// a mesma competência.
        /// </summary>
        public void Pagar(LinhaFolha linha, DateTime mes, string formaPagamento,
                          int? idCaixaAberto, string? observacao, string? usuario)
        {
            if (linha.TotalAReceber <= 0)
                throw new Exception("Não há valor a pagar para este profissional.");

            // Pagamento em dinheiro sai da gaveta, igual a sangria. Conferir
            // antes de abrir a transacao: barrar aqui devolve uma mensagem
            // clara em vez de deixar o caixa fechar com falta.
            if (formaPagamento == FormaPagamento.Dinheiro && idCaixaAberto > 0)
            {
                _caixaDAO.GarantirSaldoParaSaida(
                    idCaixaAberto!.Value, linha.TotalAReceber, "O pagamento");
            }

            var competencia = Competencia(mes);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                var descricao = $"Salário e comissões — {linha.NomeFuncionario} ({competencia})";

                using var cmdDespesa = new MySqlCommand(@"
                    INSERT INTO despesa
                        (data_desp, descricao, categoria, valor, forma_pagamento,
                         pago, id_caixa_fk, observacao)
                    VALUES
                        (@data, @descricao, 'SALARIO', @valor, @forma,
                         1, @idCaixa, 'Gerada pela folha de pagamento');
                    SELECT LAST_INSERT_ID();", conexao, transacao);

                cmdDespesa.Parameters.AddWithValue("@data", DateTime.Today);
                cmdDespesa.Parameters.AddWithValue("@descricao", descricao);
                cmdDespesa.Parameters.AddWithValue("@valor", linha.TotalAReceber);
                cmdDespesa.Parameters.AddWithValue("@forma", formaPagamento);
                // Só amarra ao caixa se o dinheiro sair mesmo da gaveta.
                cmdDespesa.Parameters.AddWithValue("@idCaixa",
                    (formaPagamento == FormaPagamento.Dinheiro && idCaixaAberto > 0)
                        ? idCaixaAberto!.Value : (object)DBNull.Value);

                var idDespesa = Convert.ToInt32(cmdDespesa.ExecuteScalar());

                using var cmdFolha = new MySqlCommand(@"
                    INSERT INTO folha_pagamento
                        (id_fun_fk, competencia, salario_fixo, comissoes, total,
                         servicos, valor_servicos, produtos, valor_produtos,
                         data_pgto, id_desp_fk, observacao, usuario)
                    VALUES
                        (@id, @competencia, @salario, @comissoes, @total,
                         @servicos, @valorServicos, @produtos, @valorProdutos,
                         @dataPgto, @idDespesa, @observacao, @usuario)",
                    conexao, transacao);

                cmdFolha.Parameters.AddWithValue("@valorServicos", linha.ValorServicos);
                cmdFolha.Parameters.AddWithValue("@produtos", linha.ProdutosVendidos);
                cmdFolha.Parameters.AddWithValue("@valorProdutos", linha.ValorProdutos);

                cmdFolha.Parameters.AddWithValue("@id", linha.IdFuncionario);
                cmdFolha.Parameters.AddWithValue("@competencia", competencia);
                cmdFolha.Parameters.AddWithValue("@salario", linha.SalarioFixo);
                cmdFolha.Parameters.AddWithValue("@comissoes", linha.Comissoes);
                cmdFolha.Parameters.AddWithValue("@total", linha.TotalAReceber);
                cmdFolha.Parameters.AddWithValue("@servicos", linha.ServicosRealizados);
                cmdFolha.Parameters.AddWithValue("@dataPgto", DateTime.Now);
                cmdFolha.Parameters.AddWithValue("@idDespesa", idDespesa);
                cmdFolha.Parameters.AddWithValue("@observacao",
                    string.IsNullOrWhiteSpace(observacao) ? (object)DBNull.Value : observacao.Trim());
                cmdFolha.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);

                cmdFolha.ExecuteNonQuery();
                transacao.Commit();
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                transacao.Rollback();
                throw new Exception(
                    $"{linha.NomeFuncionario} já foi pago na competência {competencia}.");
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao registrar o pagamento: " + ex.Message);
            }
        }

        public List<(DateTime Data, string Profissional, string Competencia, decimal Total)> Historico(int limite = 60)
        {
            var lista = new List<(DateTime, string, string, decimal)>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT p.data_pgto, f.nome_fun, p.competencia, p.total
                FROM folha_pagamento p
                JOIN funcionario f ON f.id_fun = p.id_fun_fk
                ORDER BY p.data_pgto DESC
                LIMIT @limite", conexao);

            comando.Parameters.AddWithValue("@limite", limite);

            using var leitor = comando.ExecuteReader();
            var iNome = leitor.GetOrdinal("nome_fun");

            while (leitor.Read())
            {
                lista.Add((
                    leitor.GetDateTime("data_pgto"),
                    leitor.IsDBNull(iNome) ? "(sem nome)" : leitor.GetString(iNome),
                    leitor.GetString("competencia"),
                    leitor.GetDecimal("total")));
            }

            return lista;
        }
    }
}
