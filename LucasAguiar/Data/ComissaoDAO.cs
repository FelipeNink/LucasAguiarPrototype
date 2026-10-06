using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>Ganho por comissão de cada profissional e o acerto de pagamento.</summary>
    public class ComissaoDAO
    {
        private readonly Conexao _conexao;

        public ComissaoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        public class ResumoProfissional
        {
            public int IdFuncionario { get; set; }
            public string NomeFuncionario { get; set; } = "";
            public int QuantidadeAtendimentos { get; set; }
            public decimal TotalVendido { get; set; }
            public decimal TotalComissao { get; set; }

            /// <summary>Vendas de produto, mesmo quando não geram comissão.</summary>
            public int ProdutosVendidos { get; set; }
            public decimal ValorProdutos { get; set; }
            public int ServicosRealizados { get; set; }

            public decimal PercentualSobreVenda =>
                TotalVendido == 0 ? 0 : Math.Round(TotalComissao / TotalVendido * 100, 1);
        }

        public class LinhaComissao
        {
            public int IdVenda { get; set; }
            public DateTime DataVenda { get; set; }
            public string Descricao { get; set; } = "";
            public int Quantidade { get; set; }
            public decimal ValorItem { get; set; }
            public decimal Comissao { get; set; }
            public bool CobertoPlano { get; set; }
            public string? NomeCliente { get; set; }
        }

        /// <summary>
        /// Total por profissional no período. Vendas canceladas ficam de fora.
        /// </summary>
        public List<ResumoProfissional> ResumoPorProfissional(DateTime de, DateTime ate)
        {
            var lista = new List<ResumoProfissional>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(@"
                SELECT f.id_fun, f.nome_fun,
                       COUNT(DISTINCT v.id_vend) atendimentos,
                       COALESCE(SUM(vi.valor_total), 0) vendido,
                       COALESCE(SUM(vi.comissao * vi.quantidade), 0) comissao,
                       COALESCE(SUM(CASE WHEN vi.tipo_item = 'PRODUTO' THEN vi.quantidade ELSE 0 END), 0) produtos,
                       COALESCE(SUM(CASE WHEN vi.tipo_item = 'PRODUTO' THEN vi.valor_total ELSE 0 END), 0) valor_produtos,
                       COALESCE(SUM(CASE WHEN vi.tipo_item <> 'PRODUTO' THEN vi.quantidade ELSE 0 END), 0) servicos
                FROM funcionario f
                JOIN venda v ON v.id_fun_fk = f.id_fun
                             AND v.data_vend >= @de AND v.data_vend < @ate
                             AND v.status_vend <> 'CANCELADA'
                JOIN venda_item vi ON vi.id_vend_fk = v.id_vend
                GROUP BY f.id_fun, f.nome_fun
                HAVING comissao > 0 OR atendimentos > 0
                ORDER BY comissao DESC", conexao))
            {
                comando.Parameters.AddWithValue("@de", de);
                comando.Parameters.AddWithValue("@ate", ate);

                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                {
                    lista.Add(new ResumoProfissional
                    {
                        IdFuncionario = leitor.GetInt32("id_fun"),
                        NomeFuncionario = leitor.IsDBNull(leitor.GetOrdinal("nome_fun"))
                            ? "(sem nome)" : leitor.GetString("nome_fun"),
                        QuantidadeAtendimentos = leitor.GetInt32("atendimentos"),
                        TotalVendido = leitor.GetDecimal("vendido"),
                        TotalComissao = leitor.GetDecimal("comissao"),
                        ProdutosVendidos = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("produtos"))),
                        ValorProdutos = leitor.GetDecimal("valor_produtos"),
                        ServicosRealizados = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("servicos")))
                    });
                }
            }

            return lista;
        }

        /// <summary>Detalhe item a item do que gerou a comissão.</summary>
        public List<LinhaComissao> Detalhar(int idFuncionario, DateTime de, DateTime ate)
        {
            var lista = new List<LinhaComissao>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT v.id_vend, v.data_vend, c.nome_cli,
                       vi.descricao, vi.quantidade, vi.valor_total,
                       (vi.comissao * vi.quantidade) comissao, vi.coberto_plano
                FROM venda v
                JOIN venda_item vi ON vi.id_vend_fk = v.id_vend
                LEFT JOIN cliente c ON c.id_cli = v.id_cli_fk
                WHERE v.id_fun_fk = @id
                  AND v.data_vend >= @de AND v.data_vend < @ate
                  AND v.status_vend <> 'CANCELADA'
                  AND vi.comissao > 0
                ORDER BY v.data_vend DESC, vi.id_vend_item", conexao);

            comando.Parameters.AddWithValue("@id", idFuncionario);
            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);

            using var leitor = comando.ExecuteReader();
            var iCliente = leitor.GetOrdinal("nome_cli");

            while (leitor.Read())
            {
                lista.Add(new LinhaComissao
                {
                    IdVenda = leitor.GetInt32("id_vend"),
                    DataVenda = leitor.GetDateTime("data_vend"),
                    Descricao = leitor.GetString("descricao"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    ValorItem = leitor.GetDecimal("valor_total"),
                    Comissao = leitor.GetDecimal("comissao"),
                    CobertoPlano = leitor.GetBoolean("coberto_plano"),
                    NomeCliente = leitor.IsDBNull(iCliente) ? null : leitor.GetString(iCliente)
                });
            }

            return lista;
        }

    }
}
