using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class FornecedorDAO
    {
        private readonly Conexao _conexao;

        public FornecedorDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Cadastro mínimo feito de dentro de outra tela, sem perder o que
        /// já estava preenchido lá. Devolve o id gerado.
        /// </summary>
        public int InserirRapido(string nome, string? telefone = null)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do fornecedor.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO fornecedor (nome_forn, telefone_forn)
                VALUES (@nome, @telefone);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@telefone",
                string.IsNullOrWhiteSpace(telefone) ? (object)DBNull.Value : telefone.Trim());

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Inserir(Fornecedor fornecedor)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(@"INSERT INTO fornecedor (nome_forn, email_forn, telefone_forn, tipo_prod_forn)
                    VALUES (@_nome, @_email, @_telefone, @_tipo)
                "))
                {
                    comando.Parameters.AddWithValue("@_nome", fornecedor.NomeFornecedor ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_email", fornecedor.Email ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_telefone", fornecedor.Telefone ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_tipo", fornecedor.TipoProd ?? (object)DBNull.Value);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir fornecedor: " + ex.Message);
            }
        }

        public List<Fornecedor> ListarTodos()
        {
            var lista = new List<Fornecedor>();

            using (var comando = _conexao.CreateCommand("SELECT * FROM fornecedor"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    var fornecedor = new Fornecedor
                    {
                        IdFornecedor = leitor.GetInt32("id_forn"),
                        NomeFornecedor = leitor.IsDBNull(leitor.GetOrdinal("nome_forn")) ? "" : leitor.GetString("nome_forn"),
                        Email = leitor.IsDBNull(leitor.GetOrdinal("email_forn")) ? "" : leitor.GetString("email_forn"),
                        Telefone = leitor.IsDBNull(leitor.GetOrdinal("telefone_forn")) ? "" : leitor.GetString("telefone_forn"),
                        TipoProd = leitor.IsDBNull(leitor.GetOrdinal("tipo_prod_forn")) ? "" : leitor.GetString("tipo_prod_forn")
                    };

                    lista.Add(fornecedor);
                }
            }

            return lista;
        }
    }
}
