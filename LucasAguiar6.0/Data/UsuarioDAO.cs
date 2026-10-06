using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text;

namespace LucasAguiar.Data
{
    public class UsuarioDAO
    {
        private readonly Conexao _conexao;

        public UsuarioDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        private string HashSenha(string senha)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(senha));
                return Convert.ToHexString(hashedBytes);
            }
        }

        public Usuario? ValidarLogin(string nomeUsuario, string senha)
        {
            try
            {
                var senhaHash = HashSenha(senha);

                // As tabelas usam collation utf8mb4_0900_ai_ci, que ignora
                // maiusculas/minusculas. O COLLATE utf8mb4_bin forca a
                // comparacao byte a byte no nome de usuario.
                using (var comando = _conexao.CreateCommand(
                    "SELECT * FROM usuario " +
                    "WHERE nome_usr = @nome COLLATE utf8mb4_bin " +
                    "AND senha_usr = @senha AND ativo_usr = true"))
                {
                    comando.Parameters.AddWithValue("@nome", nomeUsuario);
                    comando.Parameters.AddWithValue("@senha", senhaHash);

                    using (var leitor = comando.ExecuteReader())
                    {
                        if (leitor.Read())
                        {
                            var nomeGravado = leitor.GetString("nome_usr");

                            // Segunda barreira, independente da collation do banco.
                            if (!string.Equals(nomeGravado, nomeUsuario, StringComparison.Ordinal))
                                return null;

                            return new Usuario
                            {
                                IdUsr = leitor.GetInt32("id_usr"),
                                NomeUsr = nomeGravado,
                                EmailUsr = leitor.GetString("email_usr"),
                                AtivoUsr = leitor.GetBoolean("ativo_usr"),
                                DataCriacaoUsr = leitor.GetDateTime("data_criacao_usr")
                            };
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao validar login: " + ex.Message);
            }
        }

        public Usuario? BuscarPorEmail(string email)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(
                    "SELECT * FROM usuario WHERE email_usr = @email"))
                {
                    comando.Parameters.AddWithValue("@email", email);

                    using (var leitor = comando.ExecuteReader())
                    {
                        if (leitor.Read())
                        {
                            return new Usuario
                            {
                                IdUsr = leitor.GetInt32("id_usr"),
                                NomeUsr = leitor.GetString("nome_usr"),
                                EmailUsr = leitor.GetString("email_usr"),
                                AtivoUsr = leitor.GetBoolean("ativo_usr")
                            };
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao buscar usuário: " + ex.Message);
            }
        }
    }
}
