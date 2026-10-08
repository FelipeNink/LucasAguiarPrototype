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
            return conn;
        }

        public MySqlCommand CreateCommand(string query)
        {
            var conn = GetConnection();
            return new MySqlCommand(query, conn);
        }

    }
}
