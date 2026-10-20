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

            if (despesa.Parcelas is < 1 or > 120)
                throw new Exception("As parcelas devem ficar entre 1 e 120.");

            despesa.DataDespesa = DataDeReferencia(despesa);

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
                     pago, id_forn_fk, id_caixa_fk, observacao,
                     id_fun_fk, data_vencimento, data_pagamento, parcelas, recorrente)
                VALUES
                    (@data, @descricao, @categoria, @valor, @forma,
                     @pago, @idFornecedor, @idCaixa, @observacao,
                     @idFuncionario, @vencimento, @pagamento, @parcelas, @recorrente);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@idFuncionario",
                despesa.IdFuncionario > 0 ? despesa.IdFuncionario : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@vencimento", despesa.DataVencimento?.Date ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@pagamento",
                despesa.Pago ? despesa.DataPagamento?.Date ?? DateTime.Today : DBNull.Value);
            comando.Parameters.AddWithValue("@parcelas", despesa.Parcelas);
            comando.Parameters.AddWithValue("@recorrente", despesa.Recorrente ? 1 : 0);

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

        /// <summary>
        /// Corrige os dados descritivos. Valor, forma e situação ficam de
        /// fora de propósito: já compõem o fechamento do caixa.
        /// </summary>
        public void AtualizarDados(Despesa despesa)
        {
            if (string.IsNullOrWhiteSpace(despesa.Descricao))
                throw new Exception("Informe a descrição da despesa.");

            // Na paga, data_desp e o dia do pagamento, que ja entrou no caixa:
            // so a despesa em aberto acompanha o vencimento.
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE despesa SET
                    data_desp = CASE WHEN pago = 1 THEN data_desp ELSE @data END,
                    descricao = @descricao, categoria = @categoria,
                    id_forn_fk = @idFornecedor, observacao = @observacao,
                    id_fun_fk = @idFuncionario, data_vencimento = @vencimento, recorrente = @recorrente
                WHERE id_desp = @id", conexao);

            comando.Parameters.AddWithValue("@idFuncionario",
                despesa.IdFuncionario > 0 ? despesa.IdFuncionario : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@vencimento", despesa.DataVencimento?.Date ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@recorrente", despesa.Recorrente ? 1 : 0);
            comando.Parameters.AddWithValue("@data", (despesa.DataVencimento ?? despesa.DataDespesa).Date);
            comando.Parameters.AddWithValue("@descricao", despesa.Descricao.Trim());
            comando.Parameters.AddWithValue("@categoria", despesa.Categoria);
            comando.Parameters.AddWithValue("@idFornecedor",
                despesa.IdFornecedor > 0 ? despesa.IdFornecedor : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@observacao",
                string.IsNullOrWhiteSpace(despesa.Observacao) ? (object)DBNull.Value : despesa.Observacao.Trim());
            comando.Parameters.AddWithValue("@id", despesa.IdDespesa);

            comando.ExecuteNonQuery();
        }

        public Despesa? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + " WHERE d.id_desp = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
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
            using var comando = new MySqlCommand(SelecaoPadrao + $@"
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

        private const string SelecaoPadrao = @"
            SELECT d.*, f.nome_forn, fu.nome_fun, cd.nome_cat,
                   (SELECT COUNT(*) FROM despesa_anexo da WHERE da.id_desp_fk = d.id_desp) AS anexos
            FROM despesa d
            LEFT JOIN fornecedor f ON f.id_forn = d.id_forn_fk
            LEFT JOIN funcionario fu ON fu.id_fun = d.id_fun_fk
            LEFT JOIN categoria_despesa cd ON cd.codigo_cat = d.categoria";

        /// <summary>Paga: o dia do pagamento. Em aberto: o vencimento.</summary>
        private static DateTime DataDeReferencia(Despesa despesa) =>
            (despesa.Pago
                ? despesa.DataPagamento ?? DateTime.Today
                : despesa.DataVencimento ?? despesa.DataDespesa).Date;

        // ---------- anexos ----------

        public const int TamanhoMaximoAnexo = 5 * 1024 * 1024;

        public List<DespesaAnexo> ListarAnexos(int idDespesa)
        {
            var lista = new List<DespesaAnexo>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT id_anexo, nome, tipo, tamanho FROM despesa_anexo
                WHERE id_desp_fk = @id ORDER BY id_anexo", conexao);
            comando.Parameters.AddWithValue("@id", idDespesa);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new DespesaAnexo
                {
                    IdAnexo = leitor.GetInt32(0),
                    Nome = leitor.GetString(1),
                    Tipo = leitor.GetString(2),
                    Tamanho = leitor.GetInt32(3)
                });
            }

            return lista;
        }

        public DespesaAnexo? BuscarAnexo(int idAnexo)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT id_anexo, nome, tipo, tamanho, conteudo FROM despesa_anexo WHERE id_anexo = @id", conexao);
            comando.Parameters.AddWithValue("@id", idAnexo);

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return null;

            return new DespesaAnexo
            {
                IdAnexo = leitor.GetInt32(0),
                Nome = leitor.GetString(1),
                Tipo = leitor.GetString(2),
                Tamanho = leitor.GetInt32(3),
                Conteudo = (byte[])leitor.GetValue(4)
            };
        }

        public void AdicionarAnexo(int idDespesa, string nome, string tipo, byte[] conteudo)
        {
            if (conteudo.Length == 0) throw new Exception("O arquivo está vazio.");
            if (conteudo.Length > TamanhoMaximoAnexo) throw new Exception("O anexo pode ter no máximo 5 MB.");

            // So o nome do arquivo, sem caminho, e no tamanho da coluna.
            var nomeArquivo = Path.GetFileName(nome);
            if (nomeArquivo.Length > 200) nomeArquivo = nomeArquivo[^200..];

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO despesa_anexo (id_desp_fk, nome, tipo, tamanho, conteudo)
                VALUES (@id, @nome, @tipo, @tamanho, @conteudo)", conexao);
            comando.Parameters.AddWithValue("@id", idDespesa);
            comando.Parameters.AddWithValue("@nome", nomeArquivo);
            comando.Parameters.AddWithValue("@tipo", string.IsNullOrWhiteSpace(tipo) ? "application/octet-stream" : tipo);
            comando.Parameters.AddWithValue("@tamanho", conteudo.Length);
            comando.Parameters.AddWithValue("@conteudo", conteudo);
            comando.ExecuteNonQuery();
        }

        public void RemoverAnexo(int idAnexo)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("DELETE FROM despesa_anexo WHERE id_anexo = @id", conexao);
            comando.Parameters.AddWithValue("@id", idAnexo);
            comando.ExecuteNonQuery();
        }

        private static Despesa Ler(MySqlDataReader leitor)
        {
            var iForn = leitor.GetOrdinal("id_forn_fk");
            var iCaixa = leitor.GetOrdinal("id_caixa_fk");
            var iObs = leitor.GetOrdinal("observacao");
            var iNomeForn = leitor.GetOrdinal("nome_forn");

            return new Despesa
            {
                IdFuncionario = DAOHelper.IsNull(leitor, "id_fun_fk") ? null : leitor.GetInt32("id_fun_fk"),
                NomeFuncionario = DAOHelper.IsNull(leitor, "nome_fun") ? null : leitor.GetString("nome_fun"),
                NomeCategoria = DAOHelper.IsNull(leitor, "nome_cat") ? null : leitor.GetString("nome_cat"),
                DataVencimento = DAOHelper.GetDateTime(leitor, "data_vencimento"),
                DataPagamento = DAOHelper.GetDateTime(leitor, "data_pagamento"),
                Parcelas = leitor.GetInt32("parcelas"),
                Recorrente = leitor.GetBoolean("recorrente"),
                QuantidadeAnexos = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("anexos"))),
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
