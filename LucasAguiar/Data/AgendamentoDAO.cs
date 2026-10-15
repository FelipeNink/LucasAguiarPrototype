using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class AgendamentoDAO
    {
        private readonly Conexao _conexao;

        public AgendamentoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        private const string SelecaoPadrao = @"
            SELECT a.*, c.nome_cli, c.telefone_cli, f.nome_fun, s.nome_serv, s.preco_serv
            FROM agendamento a
            LEFT JOIN cliente c ON c.id_cli = a.id_cli_fk
            LEFT JOIN funcionario f ON f.id_fun = a.id_fun_fk
            LEFT JOIN servico s ON s.id_serv = a.id_serv_fk";

        public int Inserir(Agendamento agendamento)
        {
            Validar(agendamento);

            using var conexao = _conexao.GetConnection();
            VerificarConflito(agendamento, conexao);

            using var comando = new MySqlCommand(@"
                INSERT INTO agendamento
                    (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
                VALUES
                    (@dataHora, @duracao, @idCliente, @idFuncionario, @idServico, @observacao, @status);
                SELECT LAST_INSERT_ID();", conexao);

            PreencherParametros(comando, agendamento);
            comando.Parameters.AddWithValue("@status", StatusAgendamento.Agendado);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Atualizar(Agendamento agendamento)
        {
            Validar(agendamento);

            if (!StatusAgendamento.Todos.Any(s => s.Valor == agendamento.Status))
                throw new Exception("Status inválido.");

            using var conexao = _conexao.GetConnection();
            if (agendamento.Status == StatusAgendamento.Agendado)
                VerificarConflito(agendamento, conexao);

            using var comando = new MySqlCommand(@"
                UPDATE agendamento SET
                    data_hora = @dataHora, duracao_min = @duracao, id_cli_fk = @idCliente,
                    id_fun_fk = @idFuncionario, id_serv_fk = @idServico,
                    observacao = @observacao, status = @status
                WHERE id_agend = @id", conexao);

            PreencherParametros(comando, agendamento);
            comando.Parameters.AddWithValue("@status", agendamento.Status);
            comando.Parameters.AddWithValue("@id", agendamento.IdAgendamento);

            comando.ExecuteNonQuery();
        }

        /// <summary>Fecha o agendamento apontando para a venda que ele gerou.</summary>
        public void Concluir(int id, int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE agendamento SET status = @status, id_vend_fk = @idVenda
                WHERE id_agend = @id", conexao);

            comando.Parameters.AddWithValue("@status", StatusAgendamento.Concluido);
            comando.Parameters.AddWithValue("@idVenda", idVenda);
            comando.Parameters.AddWithValue("@id", id);

            comando.ExecuteNonQuery();
        }

        public void Excluir(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("DELETE FROM agendamento WHERE id_agend = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);
            comando.ExecuteNonQuery();
        }

        public Agendamento? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + " WHERE a.id_agend = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>Agendamentos entre as datas, do mais cedo para o mais tarde.</summary>
        public List<Agendamento> Listar(DateTime? de = null, DateTime? ate = null, string? status = null)
        {
            var lista = new List<Agendamento>();

            var condicoes = "";
            if (de.HasValue) condicoes += " AND a.data_hora >= @de";
            if (ate.HasValue) condicoes += " AND a.data_hora < @ate";
            if (!string.IsNullOrEmpty(status)) condicoes += " AND a.status = @status";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + $" WHERE 1 = 1 {condicoes} ORDER BY a.data_hora, a.id_agend", conexao);

            if (de.HasValue) comando.Parameters.AddWithValue("@de", de.Value);
            if (ate.HasValue) comando.Parameters.AddWithValue("@ate", ate.Value);
            if (!string.IsNullOrEmpty(status)) comando.Parameters.AddWithValue("@status", status);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        private static void Validar(Agendamento agendamento)
        {
            if (agendamento.IdCliente <= 0) throw new Exception("Selecione o cliente.");
            if (agendamento.IdFuncionario <= 0) throw new Exception("Selecione o profissional.");
            if (agendamento.IdServico <= 0) throw new Exception("Selecione o serviço.");
            if (agendamento.DataHora == default) throw new Exception("Informe a data e a hora.");
            if (agendamento.DuracaoMin <= 0) throw new Exception("A duração deve ser maior que zero.");
        }

        /// <summary>
        /// O mesmo profissional nao atende dois horarios que se cruzam.
        /// Cancelados e concluidos nao ocupam a agenda.
        /// </summary>
        private static void VerificarConflito(Agendamento agendamento, MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(@"
                SELECT a.data_hora, a.duracao_min, c.nome_cli
                FROM agendamento a
                LEFT JOIN cliente c ON c.id_cli = a.id_cli_fk
                WHERE a.id_fun_fk = @idFuncionario
                  AND a.status = @status
                  AND a.id_agend <> @id
                  AND a.data_hora < @fim
                  AND DATE_ADD(a.data_hora, INTERVAL a.duracao_min MINUTE) > @inicio
                ORDER BY a.data_hora
                LIMIT 1", conexao);

            comando.Parameters.AddWithValue("@idFuncionario", agendamento.IdFuncionario);
            comando.Parameters.AddWithValue("@status", StatusAgendamento.Agendado);
            comando.Parameters.AddWithValue("@id", agendamento.IdAgendamento);
            comando.Parameters.AddWithValue("@inicio", agendamento.DataHora);
            comando.Parameters.AddWithValue("@fim", agendamento.Fim);

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return;

            var inicio = leitor.GetDateTime(0);
            var fim = inicio.AddMinutes(leitor.GetInt32(1));
            var cliente = leitor.IsDBNull(2) ? "outro cliente" : leitor.GetString(2);

            throw new Exception(
                $"O profissional já tem horário com {cliente} das {inicio:HH:mm} às {fim:HH:mm} em {inicio:dd/MM}.");
        }

        private static void PreencherParametros(MySqlCommand comando, Agendamento agendamento)
        {
            comando.Parameters.AddWithValue("@dataHora", agendamento.DataHora);
            comando.Parameters.AddWithValue("@duracao", agendamento.DuracaoMin);
            comando.Parameters.AddWithValue("@idCliente", agendamento.IdCliente);
            comando.Parameters.AddWithValue("@idFuncionario", agendamento.IdFuncionario);
            comando.Parameters.AddWithValue("@idServico", agendamento.IdServico);
            comando.Parameters.AddWithValue("@observacao",
                string.IsNullOrWhiteSpace(agendamento.Observacao) ? (object)DBNull.Value : agendamento.Observacao.Trim());
        }

        private static Agendamento Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            int Inteiro(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? 0 : leitor.GetInt32(i);
            }

            var iVenda = leitor.GetOrdinal("id_vend_fk");
            var iPreco = leitor.GetOrdinal("preco_serv");

            return new Agendamento
            {
                IdAgendamento = leitor.GetInt32("id_agend"),
                DataHora = leitor.GetDateTime("data_hora"),
                DuracaoMin = Inteiro("duracao_min"),
                IdCliente = Inteiro("id_cli_fk"),
                IdFuncionario = Inteiro("id_fun_fk"),
                IdServico = Inteiro("id_serv_fk"),
                Observacao = Texto("observacao"),
                Status = Texto("status") ?? StatusAgendamento.Agendado,
                IdVenda = leitor.IsDBNull(iVenda) ? null : leitor.GetInt32(iVenda),
                NomeCliente = Texto("nome_cli"),
                TelefoneCliente = Texto("telefone_cli"),
                NomeFuncionario = Texto("nome_fun"),
                NomeServico = Texto("nome_serv"),
                // preco_serv e FLOAT no banco: converte em vez de GetDecimal.
                PrecoServico = leitor.IsDBNull(iPreco) ? 0m : Convert.ToDecimal(leitor.GetValue(iPreco))
            };
        }
    }
}
