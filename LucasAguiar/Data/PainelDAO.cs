using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>Números do painel, apurados direto no banco.</summary>
    public class PainelDAO
    {
        private readonly Conexao _conexao;

        public PainelDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        public class ResumoPainel
        {
            public decimal Receita { get; set; }
            public decimal Despesas { get; set; }
            public decimal Comissoes { get; set; }
            public int QuantidadeVendas { get; set; }

            /// <summary>Receita menos despesas e comissões.</summary>
            public decimal Lucro => Receita - Despesas - Comissoes;

            public decimal TicketMedio =>
                QuantidadeVendas == 0 ? 0 : Math.Round(Receita / QuantidadeVendas, 2);
        }

        public ResumoPainel Resumo(DateTime de, DateTime ate)
        {
            var resumo = new ResumoPainel();
            using var conexao = _conexao.GetConnection();

            using (var comando = new MySqlCommand(@"
                SELECT COUNT(*) qtd, COALESCE(SUM(valor_vend), 0) total
                FROM venda
                WHERE data_vend >= @de AND data_vend < @ate AND status_vend <> 'CANCELADA'",
                conexao))
            {
                comando.Parameters.AddWithValue("@de", de);
                comando.Parameters.AddWithValue("@ate", ate);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read())
                {
                    resumo.QuantidadeVendas = leitor.GetInt32("qtd");
                    resumo.Receita = leitor.GetDecimal("total");
                }
            }

            using (var comando = new MySqlCommand(@"
                SELECT COALESCE(SUM(vi.comissao * vi.quantidade), 0) total
                FROM venda_item vi
                JOIN venda v ON v.id_vend = vi.id_vend_fk
                WHERE v.data_vend >= @de AND v.data_vend < @ate AND v.status_vend <> 'CANCELADA'",
                conexao))
            {
                comando.Parameters.AddWithValue("@de", de);
                comando.Parameters.AddWithValue("@ate", ate);
                resumo.Comissoes = Convert.ToDecimal(comando.ExecuteScalar());
            }

            using (var comando = new MySqlCommand(
                "SELECT COALESCE(SUM(valor), 0) FROM despesa WHERE data_desp >= @de AND data_desp < @ate",
                conexao))
            {
                comando.Parameters.AddWithValue("@de", de.Date);
                comando.Parameters.AddWithValue("@ate", ate.Date);
                resumo.Despesas = Convert.ToDecimal(comando.ExecuteScalar());
            }

            return resumo;
        }

        /// <summary>Receita e despesa mês a mês, para o gráfico de barras.</summary>
        public List<(string Mes, decimal Receita, decimal Despesa)> SerieMensal(int meses = 12)
        {
            var inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                .AddMonths(-(meses - 1));

            var receitas = new Dictionary<string, decimal>();
            var despesas = new Dictionary<string, decimal>();

            using var conexao = _conexao.GetConnection();

            using (var comando = new MySqlCommand(@"
                SELECT DATE_FORMAT(data_vend, '%Y-%m') mes, COALESCE(SUM(valor_vend), 0) total
                FROM venda
                WHERE data_vend >= @inicio AND status_vend <> 'CANCELADA'
                GROUP BY mes", conexao))
            {
                comando.Parameters.AddWithValue("@inicio", inicio);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                    receitas[leitor.GetString("mes")] = leitor.GetDecimal("total");
            }

            using (var comando = new MySqlCommand(@"
                SELECT DATE_FORMAT(data_desp, '%Y-%m') mes, COALESCE(SUM(valor), 0) total
                FROM despesa
                WHERE data_desp >= @inicio
                GROUP BY mes", conexao))
            {
                comando.Parameters.AddWithValue("@inicio", inicio.Date);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                    despesas[leitor.GetString("mes")] = leitor.GetDecimal("total");
            }

            var serie = new List<(string, decimal, decimal)>();
            for (int i = 0; i < meses; i++)
            {
                var data = inicio.AddMonths(i);
                var chave = data.ToString("yyyy-MM");
                serie.Add((
                    data.ToString("MMM/yy", new System.Globalization.CultureInfo("pt-BR")),
                    receitas.GetValueOrDefault(chave),
                    despesas.GetValueOrDefault(chave)));
            }

            return serie;
        }

        /// <summary>Faturamento por forma de pagamento, para a rosca.</summary>
        public Dictionary<string, decimal> PorFormaPagamento(DateTime de, DateTime ate)
        {
            var mapa = new Dictionary<string, decimal>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT forma_pagamento_vend forma, COALESCE(SUM(valor_vend), 0) total
                FROM venda
                WHERE data_vend >= @de AND data_vend < @ate AND status_vend <> 'CANCELADA'
                GROUP BY forma_pagamento_vend", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);

            using var leitor = comando.ExecuteReader();
            var iForma = leitor.GetOrdinal("forma");

            while (leitor.Read())
            {
                var forma = leitor.IsDBNull(iForma) ? "OUTROS" : leitor.GetString(iForma);
                mapa[forma] = leitor.GetDecimal("total");
            }

            return mapa;
        }
    }
}
