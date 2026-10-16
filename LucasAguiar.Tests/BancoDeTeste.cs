using LucasAguiar.Configs;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Apoio dos testes que precisam de um MySQL de verdade.
    ///
    /// A regra de sessao, de bloqueio por tentativas e de login nao da para
    /// testar com objeto falso: o que importa nelas e justamente o que o
    /// banco faz -- transacao, FOR UPDATE, NOW(), chave estrangeira. Testar
    /// contra uma imitacao provaria que a imitacao funciona.
    ///
    /// Por isso estes testes so rodam quando a variavel de ambiente
    /// LUCASAGUIAR_TESTE_CONEXAO aponta para um banco descartavel:
    ///
    ///   LUCASAGUIAR_TESTE_CONEXAO="Server=localhost;Database=barbearia_teste;User=root;Password=...;Port=3306" \
    ///   dotnet test
    ///
    /// Sem a variavel eles sao ignorados, e o `dotnet test` continua
    /// passando numa maquina sem MySQL.
    ///
    /// Cada teste limpa o que criou. O banco apontado nunca deve ser o de
    /// producao -- os testes criam e apagam usuarios.
    /// </summary>
    public static class BancoDeTeste
    {
        public const string Variavel = "LUCASAGUIAR_TESTE_CONEXAO";

        public static string? Conexao =>
            Environment.GetEnvironmentVariable(Variavel);

        public static bool Disponivel => !string.IsNullOrWhiteSpace(Conexao);

        /// <summary>Um <see cref="Conexao"/> apontando para o banco de teste.</summary>
        public static Conexao Fabricar()
        {
            var configuracao = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MySqlConnection"] = Conexao
                })
                .Build();

            return new Conexao(configuracao);
        }

        public static MySqlConnection Abrir() => Fabricar().GetConnection();

        /// <summary>
        /// Cria um usuario so para o teste, com nome unico, e devolve o id.
        /// <paramref name="senha"/> nula deixa a linha sem senha utilizavel.
        /// </summary>
        public static int CriarUsuario(
            MySqlConnection conexao, string nome, string? senha = null,
            bool ativo = true, string perfil = "COMUM")
        {
            using var comando = new MySqlCommand(
                @"INSERT INTO usuario
                      (nome_usr, nome_exib_usr, email_usr, senha_usr, algo_senha_usr,
                       ativo_usr, perfil_usr, data_criacao_usr)
                  VALUES (@nome, @exibicao, @email, @senha, @algo, @ativo, @perfil, NOW());
                  SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome);
            comando.Parameters.AddWithValue("@exibicao", "Teste " + nome);
            comando.Parameters.AddWithValue("@email", nome + "@teste.local");
            comando.Parameters.AddWithValue("@senha",
                senha == null ? DBNull.Value : SegurancaSenha.Gerar(senha));
            comando.Parameters.AddWithValue("@algo", SegurancaSenha.Atual);
            comando.Parameters.AddWithValue("@ativo", ativo);
            comando.Parameters.AddWithValue("@perfil", perfil);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        /// <summary>
        /// Grava a senha no formato antigo, para exercitar a migracao
        /// transparente feita no login.
        /// </summary>
        public static void GravarSenhaAntiga(
            MySqlConnection conexao, int idUsuario, string senha)
        {
            using var comando = new MySqlCommand(
                @"UPDATE usuario
                     SET senha_usr = SHA2(@senha, 256), algo_senha_usr = 'sha256'
                   WHERE id_usr = @id", conexao);

            comando.Parameters.AddWithValue("@senha", senha);
            comando.Parameters.AddWithValue("@id", idUsuario);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Apaga o usuario do teste. As sessoes e permissoes dele saem
        /// junto pela chave estrangeira; o log fica, com id nulo.
        /// </summary>
        public static void ApagarUsuario(MySqlConnection conexao, int idUsuario)
        {
            using var comando = new MySqlCommand(
                "DELETE FROM usuario WHERE id_usr = @id", conexao);

            comando.Parameters.AddWithValue("@id", idUsuario);
            comando.ExecuteNonQuery();
        }

        public static void ApagarTentativas(MySqlConnection conexao, params string[] chaves)
        {
            foreach (var chave in chaves)
            {
                using var comando = new MySqlCommand(
                    "DELETE FROM tentativa_login WHERE chave = @chave", conexao);

                comando.Parameters.AddWithValue("@chave", chave);
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>Nome irrepetivel, para dois testes nao colidirem.</summary>
        public static string NomeUnico(string prefixo) =>
            prefixo + "_" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Marca o teste para ser ignorado quando nao ha banco configurado, em
    /// vez de falhar. O motivo aparece no relatorio do dotnet test.
    /// </summary>
    public sealed class FatoComBanco : FactAttribute
    {
        public FatoComBanco()
        {
            if (!BancoDeTeste.Disponivel)
                Skip = $"Defina {BancoDeTeste.Variavel} para rodar os testes de banco.";
        }
    }
}
