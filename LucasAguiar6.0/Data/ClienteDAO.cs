using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class ClienteDAO
    {
        private readonly Conexao _conexao;

        public ClienteDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        private const string SelecaoPadrao = @"
            SELECT c.*, p.nome_plan, p.valor_plan
            FROM cliente c
            LEFT JOIN plano p ON p.id_plan = c.id_plan_fk";

        /// <summary>Insere o cliente e devolve o id gerado.</summary>
        public int Inserir(Cliente cliente)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand(@"
                    INSERT INTO cliente
                        (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, rg_cli,
                         estado_cli, cidade_cli, bairro_cli, rua_cli, numero_cli, id_plan_fk)
                    VALUES
                        (@nome, @telefone, @cpf, @dataNasc, @rg,
                         @estado, @cidade, @bairro, @rua, @numero, @idPlano);
                    SELECT LAST_INSERT_ID();", conexao);

                comando.Parameters.AddWithValue("@nome", cliente.NomeCliente ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@telefone", cliente.Telefone ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@cpf", cliente.CPF ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@dataNasc", cliente.DataNascimento ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@rg", cliente.RG ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@estado", cliente.Estado ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@cidade", cliente.Cidade ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@bairro", cliente.Bairro ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@rua", cliente.Rua ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@numero", cliente.Numero ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@idPlano",
                    cliente.IdPlano.HasValue && cliente.IdPlano.Value > 0
                        ? cliente.IdPlano.Value
                        : (object)DBNull.Value);

                return Convert.ToInt32(comando.ExecuteScalar());
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir cliente: " + ex.Message);
            }
        }

        /// <summary>Cadastro rápido durante a venda: só o nome. Devolve o id.</summary>
        public int InserirSomenteNome(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do cliente.");

            return Inserir(new Cliente { NomeCliente = nome.Trim() });
        }

        /// <summary>Busca por nome, telefone ou CPF. Termo vazio traz os primeiros.</summary>
        public List<Cliente> Buscar(string? termo, int limite = 20)
        {
            var lista = new List<Cliente>();
            var temTermo = !string.IsNullOrWhiteSpace(termo);

            var sql = SelecaoPadrao +
                (temTermo
                    ? " WHERE c.nome_cli LIKE @termo OR c.telefone_cli LIKE @termo OR c.cpf_cli LIKE @termo"
                    : "") +
                " ORDER BY c.nome_cli LIMIT @limite";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(sql, conexao);
            if (temTermo) comando.Parameters.AddWithValue("@termo", $"%{termo!.Trim()}%");
            comando.Parameters.AddWithValue("@limite", limite);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        public Cliente? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + " WHERE c.id_cli = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        public List<Cliente> ListarTodos()
        {
            var lista = new List<Cliente>();
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + " ORDER BY c.nome_cli", conexao);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        private static Cliente Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            var iPlano = leitor.GetOrdinal("id_plan_fk");
            var iNomePlano = leitor.GetOrdinal("nome_plan");
            var iValorPlano = leitor.GetOrdinal("valor_plan");
            var iNasc = leitor.GetOrdinal("data_nasc_cli");

            return new Cliente
            {
                IdCliente = leitor.GetInt32("id_cli"),
                NomeCliente = Texto("nome_cli"),
                Telefone = Texto("telefone_cli"),
                CPF = Texto("cpf_cli"),
                DataNascimento = leitor.IsDBNull(iNasc) ? null : leitor.GetDateTime(iNasc),
                RG = Texto("rg_cli"),
                Estado = Texto("estado_cli"),
                Cidade = Texto("cidade_cli"),
                Bairro = Texto("bairro_cli"),
                Rua = Texto("rua_cli"),
                Numero = Texto("numero_cli"),
                IdPlano = leitor.IsDBNull(iPlano) ? null : leitor.GetInt32(iPlano),
                NomePlano = leitor.IsDBNull(iNomePlano) ? null : leitor.GetString(iNomePlano),
                // valor_plan e FLOAT no banco: converte em vez de GetDecimal.
                ValorPlano = leitor.IsDBNull(iValorPlano) ? 0m : Convert.ToDecimal(leitor.GetValue(iValorPlano))
            };
        }
    }
}
