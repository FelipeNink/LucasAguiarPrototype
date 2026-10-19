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
                        (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, rg_cli, cep_cli,
                         estado_cli, cidade_cli, bairro_cli, rua_cli, numero_cli, id_plan_fk)
                    VALUES
                        (@nome, @telefone, @cpf, @dataNasc, @rg, @cep,
                         @estado, @cidade, @bairro, @rua, @numero, @idPlano);
                    SELECT LAST_INSERT_ID();", conexao);

                comando.Parameters.AddWithValue("@nome", cliente.NomeCliente ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@telefone", cliente.Telefone ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@cpf", cliente.CPF ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@dataNasc", cliente.DataNascimento ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@rg", cliente.RG ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@cep", cliente.Cep ?? (object)DBNull.Value);
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

        /// <summary>Quem faz aniversario no mes, pela ordem do dia. So le data_nasc_cli, que ja existe.</summary>
        public List<Cliente> AniversariantesDoMes(int mes)
        {
            var lista = new List<Cliente>();
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + @"
                WHERE c.data_nasc_cli IS NOT NULL AND MONTH(c.data_nasc_cli) = @mes
                ORDER BY DAY(c.data_nasc_cli), c.nome_cli", conexao);
            comando.Parameters.AddWithValue("@mes", mes);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
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

        /// <summary>
        /// Listagem com o historico de vendas validas: ultima visita, numero
        /// de visitas, total gasto e o profissional que mais o atendeu.
        /// </summary>
        public List<Cliente> ListarComHistorico()
        {
            var lista = new List<Cliente>();
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT x.*, fp.nome_fun AS nome_fun_pref
                FROM (
                    SELECT c.*, p.nome_plan, p.valor_plan,
                           h.ultima_visita, h.visitas, h.total_gasto,
                           (SELECT v2.id_fun_fk
                              FROM venda v2
                             WHERE v2.id_cli_fk = c.id_cli
                               AND v2.id_fun_fk IS NOT NULL
                               AND v2.status_vend <> 'CANCELADA'
                             GROUP BY v2.id_fun_fk
                             ORDER BY COUNT(*) DESC, MAX(v2.data_vend) DESC
                             LIMIT 1) AS id_fun_pref
                    FROM cliente c
                    LEFT JOIN plano p ON p.id_plan = c.id_plan_fk
                    LEFT JOIN (
                        SELECT id_cli_fk,
                               MAX(data_vend) AS ultima_visita,
                               COUNT(*) AS visitas,
                               SUM(valor_vend) AS total_gasto
                          FROM venda
                         WHERE status_vend <> 'CANCELADA'
                         GROUP BY id_cli_fk
                    ) h ON h.id_cli_fk = c.id_cli
                ) x
                LEFT JOIN funcionario fp ON fp.id_fun = x.id_fun_pref
                ORDER BY x.nome_cli", conexao);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var cliente = Ler(leitor);

                var iUltima = leitor.GetOrdinal("ultima_visita");
                var iVisitas = leitor.GetOrdinal("visitas");
                var iTotal = leitor.GetOrdinal("total_gasto");
                var iPref = leitor.GetOrdinal("id_fun_pref");
                var iNomePref = leitor.GetOrdinal("nome_fun_pref");

                cliente.UltimaVisita = leitor.IsDBNull(iUltima) ? null : leitor.GetDateTime(iUltima);
                cliente.Visitas = leitor.IsDBNull(iVisitas) ? 0 : Convert.ToInt32(leitor.GetValue(iVisitas));
                cliente.TotalGasto = leitor.IsDBNull(iTotal) ? 0m : Convert.ToDecimal(leitor.GetValue(iTotal));
                cliente.IdProfissionalPreferido = leitor.IsDBNull(iPref) ? null : Convert.ToInt32(leitor.GetValue(iPref));
                cliente.NomeProfissionalPreferido = leitor.IsDBNull(iNomePref) ? null : leitor.GetString(iNomePref);

                lista.Add(cliente);
            }
            return lista;
        }

        public void Atualizar(Cliente cliente)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE cliente SET
                    nome_cli = @nome, telefone_cli = @telefone, cpf_cli = @cpf,
                    data_nasc_cli = @dataNasc, rg_cli = @rg, cep_cli = @cep, estado_cli = @estado,
                    cidade_cli = @cidade, bairro_cli = @bairro, rua_cli = @rua,
                    numero_cli = @numero, id_plan_fk = @idPlano
                WHERE id_cli = @id", conexao);

            comando.Parameters.AddWithValue("@nome", cliente.NomeCliente ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@telefone", cliente.Telefone ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cpf", cliente.CPF ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@dataNasc", cliente.DataNascimento ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@rg", cliente.RG ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@cep", cliente.Cep ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@estado", cliente.Estado ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cidade", cliente.Cidade ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@bairro", cliente.Bairro ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@rua", cliente.Rua ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@numero", cliente.Numero ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@idPlano",
                cliente.IdPlano.HasValue && cliente.IdPlano.Value > 0
                    ? cliente.IdPlano.Value
                    : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@id", cliente.IdCliente);

            comando.ExecuteNonQuery();
        }

        public void Excluir(int id)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand("DELETE FROM cliente WHERE id_cli = @id", conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (DAOHelper.EhRegistroEmUso(ex))
            {
                throw new Exception(
                    "Este cliente tem vendas, assinaturas ou agendamentos registrados e não pode ser excluído.");
            }
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
                Cep = Texto("cep_cli"),
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
