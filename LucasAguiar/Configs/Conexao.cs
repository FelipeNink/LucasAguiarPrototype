using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace LucasAguiar.Configs
{
    public class Conexao
    {
        private readonly string _connectionString;
        public Conexao(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySqlConnection") ?? "";
        }

        /// <summary>
        /// Servidor, porta e banco para diagnostico, sem usuario nem senha.
        /// Serve para o log dizer onde a aplicacao tentou se conectar
        /// quando a conexao falha -- vazio aqui significa variavel de
        /// ambiente ausente ou com nome errado.
        /// </summary>
        public string Descricao
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_connectionString))
                    return "(connection string vazia)";

                try
                {
                    var c = new MySqlConnectionStringBuilder(_connectionString);
                    return $"servidor={c.Server}; porta={c.Port}; banco={c.Database}";
                }
                catch
                {
                    return "(connection string em formato invalido)";
                }
            }
        }

        public MySqlConnection GetConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();

            // NOW() e CURDATE() do MySQL no mesmo fuso do DateTime.Now da
            // aplicacao (variavel TZ). Sem isto o banco do Railway fica em
            // UTC e as comparacoes com horarios gravados pela aplicacao --
            // "data_hora > NOW()", "quando >= CURDATE()" -- erram por horas.
            // Por conexao: o pool zera as variaveis de sessao ao reaproveitar.
            using (var fuso = new MySqlCommand("SET time_zone = @fuso", conn))
            {
                fuso.Parameters.AddWithValue("@fuso", FusoMySql());
                fuso.ExecuteNonQuery();
            }

            return conn;
        }

        /// <summary>"-04:00": deslocamento do fuso local agora, no formato que o MySQL aceita sem tabela de fusos.</summary>
        public static string FusoMySql()
        {
            var deslocamento = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            return (deslocamento < TimeSpan.Zero ? "-" : "+") + deslocamento.ToString(@"hh\:mm");
        }

        public MySqlCommand CreateCommand(string query)
        {
            var conn = GetConnection();
            return new MySqlCommand(query, conn);
        }

    }
}
