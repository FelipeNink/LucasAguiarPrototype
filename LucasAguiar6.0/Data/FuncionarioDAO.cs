using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class FuncionarioDAO
    {
        private readonly Conexao _conexao;

        public FuncionarioDAO(Conexao conexao)
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
                throw new Exception("Informe o nome do profissional.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO funcionario (nome_fun, telefone_fun)
                VALUES (@nome, @telefone);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@telefone",
                string.IsNullOrWhiteSpace(telefone) ? (object)DBNull.Value : telefone.Trim());

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Inserir(Funcionario funcionario)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(@"
                        INSERT INTO funcionario (nome_fun, telefone_fun, cpf_fun, data_nasc_fun, ctps_fun, rg_fun, email_fun, estado_fun, cidade_fun, bairro_fun, rua_fun, numero_fun, salario_fixo)
                        VALUES (@_nome, @_telefone, @_cpf, @_dataNasc,@_ctps, @_rg, @_email, @_estado, @_cidade, @_bairro, @_rua, @_numero, @_salario)
                "))
                {
                    comando.Parameters.AddWithValue("@_salario", funcionario.SalarioFixo);
                    comando.Parameters.AddWithValue("@_email", funcionario.Email ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_nome", funcionario.NomeFuncionario ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_telefone", funcionario.Telefone ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_cpf", funcionario.CPF ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_dataNasc", funcionario.DataNascimento ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_ctps", funcionario.CTPS ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_rg", funcionario.RG ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_estado", funcionario.Estado ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_cidade", funcionario.Cidade ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_bairro", funcionario.Bairro ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_rua", funcionario.Rua ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_numero", funcionario.Numero ?? (object)DBNull.Value);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir funcionario: " + ex.Message);
            }
        }

        public List<Funcionario> ListarTodos()
        {
            var lista = new List<Funcionario>();
            using (var comando = _conexao.CreateCommand("SELECT * FROM funcionario"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    var funcionario = new Funcionario
                    {
                        IdFuncionario = leitor.GetInt32("id_fun"),
                        NomeFuncionario = leitor.IsDBNull(leitor.GetOrdinal("nome_fun")) ? null : leitor.GetString("nome_fun"),
                        Telefone = leitor.IsDBNull(leitor.GetOrdinal("telefone_fun")) ? null : leitor.GetString("telefone_fun"),
                        CPF = leitor.IsDBNull(leitor.GetOrdinal("cpf_fun")) ? null : leitor.GetString("cpf_fun"),
                        DataNascimento = leitor.IsDBNull(leitor.GetOrdinal("data_nasc_fun")) ? null : leitor.GetDateTime("data_nasc_fun"),
                        CTPS = leitor.IsDBNull(leitor.GetOrdinal("ctps_fun")) ? null : leitor.GetString("ctps_fun"),
                        RG = leitor.IsDBNull(leitor.GetOrdinal("rg_fun")) ? null : leitor.GetString("rg_fun"),
                        Estado = leitor.IsDBNull(leitor.GetOrdinal("estado_fun")) ? null : leitor.GetString("estado_fun"),
                        Cidade = leitor.IsDBNull(leitor.GetOrdinal("cidade_fun")) ? null : leitor.GetString("cidade_fun"),
                        Bairro = leitor.IsDBNull(leitor.GetOrdinal("bairro_fun")) ? null : leitor.GetString("bairro_fun"),
                        Rua = leitor.IsDBNull(leitor.GetOrdinal("rua_fun")) ? null : leitor.GetString("rua_fun"),
                        Numero = leitor.IsDBNull(leitor.GetOrdinal("numero_fun")) ? null : leitor.GetString("numero_fun"),
                        SalarioFixo = leitor.IsDBNull(leitor.GetOrdinal("salario_fixo")) ? 0m : leitor.GetDecimal("salario_fixo"),
                    };

                    lista.Add(funcionario);
                }
            }

            return lista;
        }
    }
}
