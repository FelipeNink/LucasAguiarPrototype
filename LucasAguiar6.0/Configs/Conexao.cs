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

        public MySqlConnection GetConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public MySqlCommand CreateCommand(string query)
        {
            var conn = GetConnection();
            return new MySqlCommand(query, conn);
        }

    }
}
