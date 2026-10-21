using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Lê o que as outras telas gravaram para montar o fluxo de caixa (regime
    /// de caixa) e o DRE (competência). As contas ficam em FluxoCaixa; aqui
    /// é só banco, e a configuração de taxas e prazos por forma.
    /// </summary>
    public class FluxoCaixaDAO
    {
        private readonly Conexao _conexao;

        /// <summary>
        /// Até onde voltar para achar parcela que cai no período: 12 parcelas
        /// de 30 dias mais o prazo da primeira cabem com folga.
        /// </summary>
        private const int DiasParaTras = 400;

        public FluxoCaixaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        // ---------- taxas e prazos ----------

        /// <summary>As cinco formas, sempre: a que faltar no banco vem zerada.</summary>
        public Dictionary<string, TaxaPagamento> ListarTaxas()
        {
            var mapa = TaxaPagamento.Todas.ToDictionary(t => t.Forma, t => new TaxaPagamento { Forma = t.Forma });

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT forma, taxa, dias FROM taxa_pagamento", conexao);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                var forma = leitor.GetString("forma");
                if (!mapa.TryGetValue(forma, out var taxa)) continue;

                taxa.Taxa = leitor.GetDecimal("taxa");
                taxa.Dias = leitor.GetInt32("dias");
            }

            return mapa;
        }

        public void SalvarTaxas(IEnumerable<TaxaPagamento> taxas)
        {
            var lista = taxas.ToList();

            foreach (var t in lista)
            {
                if (t.Taxa is < 0 or > 100) throw new Exception($"A taxa de {t.Rotulo} deve ficar entre 0% e 100%.");
                if (t.Dias is < 0 or > 365) throw new Exception($"O prazo de {t.Rotulo} deve ficar entre 0 e 365 dias.");
            }

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                foreach (var t in lista)
                {
                    using var comando = new MySqlCommand(@"
                        INSERT INTO taxa_pagamento (forma, taxa, dias)
                        VALUES (@forma, @taxa, @dias) AS novo
                        ON DUPLICATE KEY UPDATE taxa = novo.taxa, dias = novo.dias", conexao, transacao);

                    comando.Parameters.AddWithValue("@forma", t.Forma);
                    comando.Parameters.AddWithValue("@taxa", Math.Round(t.Taxa, 2));
                    comando.Parameters.AddWithValue("@dias", t.Dias);
                    comando.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        // ---------- fluxo de caixa ----------

        /// <summary>
        /// Entradas e saídas com data no período (ate inclusivo), em ordem.
        /// Entrada: pagamento de venda na data em que cai, sem a taxa.
        /// Saída: despesa paga no dia do pagamento, em aberto no vencimento
        /// -- o que já inclui as compras e a folha, que geram despesa.
        /// </summary>
        public List<LancamentoFluxo> Lancamentos(DateTime de, DateTime ate)
        {
            var hoje = DateTime.Today;
            var lista = FluxoCaixa.Recebimentos(PagamentosDeVenda(de.Date.AddDays(-DiasParaTras), ate), ListarTaxas(), hoje)
                .Where(l => l.Data >= de.Date && l.Data <= ate.Date)
                .ToList();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT x.* FROM (
                    SELECT d.id_desp, d.descricao, d.valor, d.pago,
                           COALESCE(cd.nome_cat, d.categoria) AS categoria,
                           COALESCE(f.nome_forn, fu.nome_fun) AS favorecido,
                           CASE WHEN d.pago = 1 THEN COALESCE(d.data_pagamento, d.data_desp)
                                ELSE COALESCE(d.data_vencimento, d.data_desp) END AS data_ref
                      FROM despesa d
                      LEFT JOIN categoria_despesa cd ON cd.codigo_cat = d.categoria
                      LEFT JOIN fornecedor f ON f.id_forn = d.id_forn_fk
                      LEFT JOIN funcionario fu ON fu.id_fun = d.id_fun_fk
                ) x
                WHERE x.data_ref >= @de AND x.data_ref <= @ate", conexao);

            comando.Parameters.AddWithValue("@de", de.Date);
            comando.Parameters.AddWithValue("@ate", ate.Date);

            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    var favorecido = DAOHelper.GetString(leitor, "favorecido");
                    var categoria = DAOHelper.GetString(leitor, "categoria");

                    lista.Add(new LancamentoFluxo
                    {
                        Data = leitor.GetDateTime("data_ref").Date,
                        Descricao = DAOHelper.GetString(leitor, "descricao") ?? "Despesa",
                        Detalhe = string.Join(" · ", new[] { categoria, favorecido }.Where(t => !string.IsNullOrWhiteSpace(t))),
                        Entrada = false,
                        Valor = leitor.GetDecimal("valor"),
                        Realizado = leitor.GetBoolean("pago"),
                        IdDespesa = leitor.GetInt32("id_desp")
                    });
                }
            }

            return lista.OrderBy(l => l.Data).ThenByDescending(l => l.Entrada).ToList();
        }

        /// <summary>Pagamentos de venda válida com data_vend no intervalo (ate inclusivo).</summary>
        private List<PagamentoDeVenda> PagamentosDeVenda(DateTime de, DateTime ate)
        {
            var lista = new List<PagamentoDeVenda>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT v.id_vend, v.data_vend, p.forma, p.valor, p.parcelas
                  FROM venda_pagamento p
                  JOIN venda v ON v.id_vend = p.id_vend_fk
                 WHERE v.status_vend <> 'CANCELADA'
                   AND p.forma <> @plano
                   AND v.data_vend >= @de AND v.data_vend < @ate", conexao);

            comando.Parameters.AddWithValue("@plano", FormaPagamento.Plano);
            comando.Parameters.AddWithValue("@de", de.Date);
            comando.Parameters.AddWithValue("@ate", ate.Date.AddDays(1));

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new PagamentoDeVenda
                {
                    IdVenda = leitor.GetInt32("id_vend"),
                    DataVenda = leitor.GetDateTime("data_vend"),
                    Forma = leitor.GetString("forma"),
                    Valor = leitor.GetDecimal("valor"),
                    Parcelas = DAOHelper.IsNull(leitor, "parcelas") ? null : leitor.GetInt32("parcelas")
                });
            }

            return lista;
        }

        // ---------- DRE ----------

        /// <summary>
        /// Resultado do período (ate inclusivo), por competência.
        ///
        /// Comissão sai da venda e salário fixo da folha (ou do cadastro), e
        /// por isso a despesa que a folha gerou fica fora das despesas
        /// operacionais -- senão a comissão contaria duas vezes. A despesa
        /// gerada por compra também fica fora: o que foi comprado para
        /// revender entra no custo do produto quando é vendido.
        /// </summary>
        public Dre MontarDre(DateTime de, DateTime ate)
        {
            var inicio = de.Date;
            var fimExclusivo = ate.Date.AddDays(1);
            var dre = new Dre();

            using var conexao = _conexao.GetConnection();

            using (var comando = new MySqlCommand(@"
                SELECT COALESCE(SUM(v.valor_vend), 0) AS receita,
                       COALESCE(SUM((SELECT SUM(vi.comissao * vi.quantidade)
                                        FROM venda_item vi WHERE vi.id_vend_fk = v.id_vend)), 0) AS comissoes,
                       COALESCE(SUM((SELECT SUM(vi.quantidade * COALESCE(p.preco_custo, 0))
                                        FROM venda_item vi
                                        JOIN produto p ON p.id_prod = vi.id_ref
                                       WHERE vi.id_vend_fk = v.id_vend AND vi.tipo_item = 'PRODUTO')), 0) AS custo
                  FROM venda v
                 WHERE v.status_vend <> 'CANCELADA'
                   AND v.data_vend >= @de AND v.data_vend < @ate", conexao))
            {
                comando.Parameters.AddWithValue("@de", inicio);
                comando.Parameters.AddWithValue("@ate", fimExclusivo);

                using var leitor = comando.ExecuteReader();
                if (leitor.Read())
                {
                    dre.Receita = leitor.GetDecimal("receita");
                    dre.Comissoes = Math.Round(leitor.GetDecimal("comissoes"), 2);
                    dre.CustoProdutos = Math.Round(leitor.GetDecimal("custo"), 2);
                }
            }

            var taxas = ListarTaxas();
            dre.Taxas = PagamentosDeVenda(inicio, ate.Date).Sum(p => FluxoCaixa.ValorDaTaxa(p, taxas));

            dre.SalariosFixos = FluxoCaixa.SalariosFixos(Profissionais(conexao), FolhaPaga(conexao, inicio, ate.Date), inicio, ate.Date);

            using (var comando = new MySqlCommand(@"
                SELECT COALESCE(cd.nome_cat, d.categoria) AS categoria, SUM(d.valor) AS total
                  FROM despesa d
                  LEFT JOIN categoria_despesa cd ON cd.codigo_cat = d.categoria
                 WHERE d.data_desp >= @de AND d.data_desp < @ate
                   AND NOT EXISTS (SELECT 1 FROM folha_pagamento fp WHERE fp.id_desp_fk = d.id_desp)
                   AND NOT EXISTS (SELECT 1 FROM compras c WHERE c.id_desp_fk = d.id_desp)
                 GROUP BY COALESCE(cd.nome_cat, d.categoria)
                 ORDER BY total DESC", conexao))
            {
                comando.Parameters.AddWithValue("@de", inicio);
                comando.Parameters.AddWithValue("@ate", fimExclusivo);

                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                {
                    var codigo = leitor.GetString("categoria");
                    dre.Despesas.Add((CategoriaDespesa.Rotulo(codigo), leitor.GetDecimal("total")));
                }
            }

            return dre;
        }

        private static List<SalarioDoProfissional> Profissionais(MySqlConnection conexao)
        {
            var lista = new List<SalarioDoProfissional>();

            using var comando = new MySqlCommand(@"
                SELECT id_fun, COALESCE(salario_fixo, 0) AS salario, ativo_fun, data_admissao, data_desligamento
                  FROM funcionario", conexao);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new SalarioDoProfissional
                {
                    IdFuncionario = leitor.GetInt32("id_fun"),
                    SalarioFixo = leitor.GetDecimal("salario"),
                    Ativo = leitor.GetBoolean("ativo_fun"),
                    Admissao = DAOHelper.GetDateTime(leitor, "data_admissao"),
                    Desligamento = DAOHelper.GetDateTime(leitor, "data_desligamento")
                });
            }

            return lista;
        }

        private static Dictionary<(int, string), decimal> FolhaPaga(MySqlConnection conexao, DateTime de, DateTime ate)
        {
            var mapa = new Dictionary<(int, string), decimal>();

            using var comando = new MySqlCommand(@"
                SELECT id_fun_fk, competencia, salario_fixo
                  FROM folha_pagamento
                 WHERE competencia >= @de AND competencia <= @ate", conexao);

            comando.Parameters.AddWithValue("@de", de.ToString("yyyy-MM"));
            comando.Parameters.AddWithValue("@ate", ate.ToString("yyyy-MM"));

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                mapa[(leitor.GetInt32("id_fun_fk"), leitor.GetString("competencia"))] = leitor.GetDecimal("salario_fixo");

            return mapa;
        }
    }
}
