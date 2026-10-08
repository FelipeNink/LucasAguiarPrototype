using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class DespesaDAO
    {
        private readonly Conexao _conexao;
        private readonly CaixaDAO _caixaDAO;

        public DespesaDAO(Conexao conexao, CaixaDAO caixaDAO)
        {
            _conexao = conexao;
            _caixaDAO = caixaDAO;
        }

        public int Inserir(Despesa despesa)
        {
            if (string.IsNullOrWhiteSpace(despesa.Descricao))
                throw new Exception("Informe a descrição da despesa.");

            if (despesa.Valor <= 0)
                throw new Exception("O valor da despesa deve ser maior que zero.");

            // Despesa paga em dinheiro tira da gaveta: so pode se couber.
            // Sem isto o caixa fecha negativo e o conferente nao descobre
            // de onde veio a diferenca.
            if (despesa.Pago
                && despesa.FormaPagamentoDespesa == FormaPagamento.Dinheiro
                && despesa.IdCaixa > 0)
            {
                _caixaDAO.GarantirSaldoParaSaida(despesa.IdCaixa!.Value, despesa.Valor, "A despesa");
            }

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO despesa
                    (data_desp, descricao, categoria, valor, forma_pagamento,
                     pago, id_forn_fk, id_caixa_fk, observacao)
                VALUES
                    (@data, @descricao, @categoria, @valor, @forma,
                     @pago, @idFornecedor, @idCaixa, @observacao);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@data", despesa.DataDespesa.Date);
            comando.Parameters.AddWithValue("@descricao", despesa.Descricao.Trim());
            comando.Parameters.AddWithValue("@categoria", despesa.Categoria);
            comando.Parameters.AddWithValue("@valor", despesa.Valor);
            comando.Parameters.AddWithValue("@forma", despesa.FormaPagamentoDespesa);
            comando.Parameters.AddWithValue("@pago", despesa.Pago ? 1 : 0);
            comando.Parameters.AddWithValue("@idFornecedor",
                despesa.IdFornecedor > 0 ? despesa.IdFornecedor : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@idCaixa",
                despesa.IdCaixa > 0 ? despesa.IdCaixa : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@observacao",
                string.IsNullOrWhiteSpace(despesa.Observacao) ? (object)DBNull.Value : despesa.Observacao);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Excluir(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("DELETE FROM despesa WHERE id_desp = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);
            comando.ExecuteNonQuery();
        }

        public class Filtro
        {
            public DateTime? De { get; set; }
            public DateTime? Ate { get; set; }
            public string? Categoria { get; set; }
            public string? FormaPagamento { get; set; }
            public int Limite { get; set; } = 500;

            public bool TemAlgum =>
                De.HasValue || Ate.HasValue
                || !string.IsNullOrWhiteSpace(Categoria)
                || !string.IsNullOrWhiteSpace(FormaPagamento);

            public string Resumo()
            {
                if (!TemAlgum) return "Todas as despesas";

                var partes = new List<string>();
                if (De.HasValue && Ate.HasValue)
                    partes.Add($"de {De.Value:dd/MM/yyyy} a {Ate.Value.AddDays(-1):dd/MM/yyyy}");
                else if (De.HasValue) partes.Add($"a partir de {De.Value:dd/MM/yyyy}");
                else if (Ate.HasValue) partes.Add($"até {Ate.Value.AddDays(-1):dd/MM/yyyy}");

                if (!string.IsNullOrWhiteSpace(Categoria))
                    partes.Add(CategoriaDespesa.Rotulo(Categoria));
                if (!string.IsNullOrWhiteSpace(FormaPagamento))
                    partes.Add(Models.FormaPagamento.Rotulo(FormaPagamento));

                return string.Join(" · ", partes);
            }
        }

        public List<Despesa> Listar(Filtro? filtro = null)
        {
            filtro ??= new Filtro();
            var lista = new List<Despesa>();

            var condicoes = "";
            if (filtro.De.HasValue) condicoes += " AND d.data_desp >= @de";
            if (filtro.Ate.HasValue) condicoes += " AND d.data_desp < @ate";
            if (!string.IsNullOrWhiteSpace(filtro.Categoria)) condicoes += " AND d.categoria = @categoria";
            if (!string.IsNullOrWhiteSpace(filtro.FormaPagamento)) condicoes += " AND d.forma_pagamento = @forma";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT d.*, f.nome_forn
                FROM despesa d
                LEFT JOIN fornecedor f ON f.id_forn = d.id_forn_fk
                WHERE 1 = 1 {condicoes}
                ORDER BY d.data_desp DESC, d.id_desp DESC
                LIMIT @limite", conexao);

            if (filtro.De.HasValue) comando.Parameters.AddWithValue("@de", filtro.De.Value.Date);
            if (filtro.Ate.HasValue) comando.Parameters.AddWithValue("@ate", filtro.Ate.Value.Date);
            if (!string.IsNullOrWhiteSpace(filtro.Categoria))
                comando.Parameters.AddWithValue("@categoria", filtro.Categoria);
            if (!string.IsNullOrWhiteSpace(filtro.FormaPagamento))
                comando.Parameters.AddWithValue("@forma", filtro.FormaPagamento);
            comando.Parameters.AddWithValue("@limite", filtro.Limite);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        /// <summary>Total por categoria no período, para o painel.</summary>
        public Dictionary<string, decimal> TotalPorCategoria(DateTime de, DateTime ate)
        {
            var mapa = new Dictionary<string, decimal>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT categoria, SUM(valor) total
                FROM despesa
                WHERE data_desp >= @de AND data_desp < @ate
                GROUP BY categoria
                ORDER BY total DESC", conexao);

            comando.Parameters.AddWithValue("@de", de.Date);
            comando.Parameters.AddWithValue("@ate", ate.Date);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                mapa[leitor.GetString("categoria")] = leitor.GetDecimal("total");

            return mapa;
        }

        private static Despesa Ler(MySqlDataReader leitor)
        {
            var iForn = leitor.GetOrdinal("id_forn_fk");
            var iCaixa = leitor.GetOrdinal("id_caixa_fk");
            var iObs = leitor.GetOrdinal("observacao");
            var iNomeForn = leitor.GetOrdinal("nome_forn");

            return new Despesa
            {
                IdDespesa = leitor.GetInt32("id_desp"),
                DataDespesa = leitor.GetDateTime("data_desp"),
                Descricao = leitor.GetString("descricao"),
                Categoria = leitor.GetString("categoria"),
                Valor = leitor.GetDecimal("valor"),
                FormaPagamentoDespesa = leitor.GetString("forma_pagamento"),
                Pago = leitor.GetBoolean("pago"),
                IdFornecedor = leitor.IsDBNull(iForn) ? null : leitor.GetInt32(iForn),
                IdCaixa = leitor.IsDBNull(iCaixa) ? null : leitor.GetInt32(iCaixa),
                Observacao = leitor.IsDBNull(iObs) ? null : leitor.GetString(iObs),
                NomeFornecedor = leitor.IsDBNull(iNomeForn) ? null : leitor.GetString(iNomeForn)
            };
        }
    }
}
