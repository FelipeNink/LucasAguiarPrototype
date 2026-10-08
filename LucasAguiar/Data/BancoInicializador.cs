using System.Reflection;
using LucasAguiar.Configs;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Prepara o banco na primeira subida.
    ///
    /// O projeto nao usa EF Core, entao nao ha migrations que criem o
    /// esquema sozinhas. Sem isto, publicar significa abrir um cliente
    /// MySQL e rodar um script de 17 tabelas na mao antes do primeiro
    /// acesso -- e depois lembrar de inserir o usuario de login, sem o
    /// qual ninguem entra.
    ///
    /// Nada aqui apaga ou altera o que ja existe: as duas etapas so
    /// rodam quando encontram o banco vazio.
    /// </summary>
    public class BancoInicializador
    {
        private readonly Conexao _conexao;
        private readonly ILogger<BancoInicializador> _log;

        public BancoInicializador(Conexao conexao, ILogger<BancoInicializador> log)
        {
            _conexao = conexao;
            _log = log;
        }

        public void Preparar()
        {
            try
            {
                using var conexao = _conexao.GetConnection();

                if (!TabelasExistem(conexao))
                {
                    CriarEsquema(conexao);
                }

                CriarPrimeiroUsuario(conexao);
            }
            catch (Exception ex)
            {
                // Falhar aqui nao pode derrubar a aplicacao: se o banco
                // estiver fora do ar, e melhor subir e mostrar o erro na
                // tela do que o container nem iniciar e esconder a causa.
                _log.LogError(ex,
                    "Não foi possível preparar o banco de dados. Tentativa em: {Onde}. "
                    + "Confira a variável ConnectionStrings__MySqlConnection.",
                    _conexao.Descricao);
            }
        }

        /// <summary>A tabela de login e a prova de que o esquema ja rodou.</summary>
        private static bool TabelasExistem(MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.tables
                  WHERE table_schema = DATABASE() AND table_name = 'usuario'", conexao);

            return Convert.ToInt32(comando.ExecuteScalar()) > 0;
        }

        private void CriarEsquema(MySqlConnection conexao)
        {
            var script = LerScript();
            if (string.IsNullOrWhiteSpace(script))
            {
                _log.LogError("O script do esquema não foi encontrado no assembly.");
                return;
            }

            using (var comando = new MySqlCommand(script, conexao))
            {
                comando.CommandTimeout = 120;
                comando.ExecuteNonQuery();
            }

            _log.LogWarning("Banco vazio: esquema criado a partir de banco_dados.sql.");
        }

        /// <summary>
        /// Cria o primeiro login a partir de ADMIN_USUARIO e ADMIN_SENHA.
        /// So acontece com a tabela vazia, entao reiniciar a aplicacao nao
        /// recria nem sobrescreve nada.
        /// </summary>
        private void CriarPrimeiroUsuario(MySqlConnection conexao)
        {
            using (var conta = new MySqlCommand("SELECT COUNT(*) FROM usuario", conexao))
            {
                if (Convert.ToInt32(conta.ExecuteScalar()) > 0) return;
            }

            var nome = Environment.GetEnvironmentVariable("ADMIN_USUARIO");
            var senha = Environment.GetEnvironmentVariable("ADMIN_SENHA");

            if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(senha))
            {
                _log.LogWarning(
                    "Não há usuário cadastrado e ADMIN_USUARIO/ADMIN_SENHA não foram " +
                    "definidas. Defina as duas e reinicie para criar o primeiro acesso.");
                return;
            }

            using var comando = new MySqlCommand(
                @"INSERT INTO usuario
                      (nome_usr, email_usr, senha_usr, ativo_usr, data_criacao_usr)
                  VALUES (@nome, @email, SHA2(@senha, 256), 1, NOW())", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@email",
                Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? $"{nome.Trim()}@local");
            comando.Parameters.AddWithValue("@senha", senha);

            comando.ExecuteNonQuery();

            // A senha nao vai para o log de proposito.
            _log.LogWarning("Primeiro usuário criado: {Nome}.", nome.Trim());
        }

        /// <summary>
        /// O .sql viaja dentro do assembly. Depender do arquivo em disco
        /// quebraria no container, onde so vai o resultado do publish.
        /// </summary>
        private static string LerScript()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var nome = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("banco_dados.sql", StringComparison.OrdinalIgnoreCase));

            if (nome == null) return "";

            using var fluxo = assembly.GetManifestResourceStream(nome)!;
            using var leitor = new StreamReader(fluxo);
            return leitor.ReadToEnd();
        }
    }
}
