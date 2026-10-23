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

        // Os servicos vem agregados de agendamento_servico, com o preco proprio
        // do profissional (funcionario_servico) quando houver.
        private const string SelecaoPadrao = @"
            SELECT a.*, c.nome_cli, c.telefone_cli, f.nome_fun,
                   sv.nomes AS nome_serv, sv.preco AS preco_serv, sv.ids AS ids_serv
            FROM agendamento a
            LEFT JOIN cliente c ON c.id_cli = a.id_cli_fk
            LEFT JOIN funcionario f ON f.id_fun = a.id_fun_fk
            LEFT JOIN (
                SELECT ags.id_agend_fk,
                       GROUP_CONCAT(s.nome_serv ORDER BY ags.ordem SEPARATOR ' + ') AS nomes,
                       GROUP_CONCAT(CAST(s.id_serv AS CHAR) ORDER BY ags.ordem) AS ids,
                       SUM(COALESCE(fs.preco, s.preco_serv)) AS preco
                  FROM agendamento_servico ags
                  JOIN agendamento a2 ON a2.id_agend = ags.id_agend_fk
                  JOIN servico s ON s.id_serv = ags.id_serv_fk
                  LEFT JOIN funcionario_servico fs ON fs.id_fun_fk = a2.id_fun_fk AND fs.id_serv_fk = s.id_serv
                 GROUP BY ags.id_agend_fk
            ) sv ON sv.id_agend_fk = a.id_agend";

        public int Inserir(Agendamento agendamento)
        {
            Validar(agendamento);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                TravarProfissional(conexao, transacao, agendamento.IdFuncionario);
                VerificarConflito(agendamento, conexao, transacao);

                int id;
                using (var comando = new MySqlCommand(@"
                    INSERT INTO agendamento
                        (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
                    VALUES
                        (@dataHora, @duracao, @idCliente, @idFuncionario, @idServico, @observacao, @status);
                    SELECT LAST_INSERT_ID();", conexao, transacao))
                {
                    PreencherParametros(comando, agendamento);
                    comando.Parameters.AddWithValue("@status", StatusAgendamento.Agendado);
                    id = Convert.ToInt32(comando.ExecuteScalar());
                }

                GravarServicos(conexao, transacao, id, agendamento.IdsServicos);
                transacao.Commit();
                return id;
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        public void Atualizar(Agendamento agendamento)
        {
            Validar(agendamento);

            if (!StatusAgendamento.Todos.Any(s => s.Valor == agendamento.Status))
                throw new Exception("Status inválido.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                if (agendamento.Status == StatusAgendamento.Agendado)
                {
                    TravarProfissional(conexao, transacao, agendamento.IdFuncionario);
                    VerificarConflito(agendamento, conexao, transacao);
                }

                // Remarcado para outra hora ou outro profissional, a
                // confirmacao do cliente era do horario antigo: volta a
                // pendente. Vem antes de data_hora no SET porque o MySQL
                // aplica as atribuicoes em ordem.
                using (var comando = new MySqlCommand(@"
                    UPDATE agendamento SET
                        confirmado_em = IF(data_hora <> @dataHora OR id_fun_fk <> @idFuncionario, NULL, confirmado_em),
                        data_hora = @dataHora, duracao_min = @duracao, id_cli_fk = @idCliente,
                        id_fun_fk = @idFuncionario, id_serv_fk = @idServico,
                        observacao = @observacao, status = @status
                    WHERE id_agend = @id", conexao, transacao))
                {
                    PreencherParametros(comando, agendamento);
                    comando.Parameters.AddWithValue("@status", agendamento.Status);
                    comando.Parameters.AddWithValue("@id", agendamento.IdAgendamento);
                    comando.ExecuteNonQuery();
                }

                GravarServicos(conexao, transacao, agendamento.IdAgendamento, agendamento.IdsServicos);
                transacao.Commit();
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        internal static void GravarServicos(
            MySqlConnection conexao, MySqlTransaction transacao, int idAgendamento, List<int> idsServicos)
        {
            using (var limpar = new MySqlCommand(
                "DELETE FROM agendamento_servico WHERE id_agend_fk = @id", conexao, transacao))
            {
                limpar.Parameters.AddWithValue("@id", idAgendamento);
                limpar.ExecuteNonQuery();
            }

            for (var i = 0; i < idsServicos.Count; i++)
            {
                using var inserir = new MySqlCommand(@"
                    INSERT INTO agendamento_servico (id_agend_fk, id_serv_fk, ordem)
                    VALUES (@id, @idServico, @ordem)", conexao, transacao);
                inserir.Parameters.AddWithValue("@id", idAgendamento);
                inserir.Parameters.AddWithValue("@idServico", idsServicos[i]);
                inserir.Parameters.AddWithValue("@ordem", i);
                inserir.ExecuteNonQuery();
            }
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

        // ---------- confirmacao pelo cliente ----------

        /// <summary>
        /// Codigo do link de confirmacao, criado na primeira vez que alguem
        /// manda o lembrete. Aleatorio (128 bits): o link de um cliente nao
        /// leva ao de outro trocando um numero.
        /// </summary>
        public string GarantirToken(int id)
        {
            using var conexao = _conexao.GetConnection();

            using (var gravar = new MySqlCommand(@"
                UPDATE agendamento SET token_confirmacao = @token
                WHERE id_agend = @id AND token_confirmacao IS NULL", conexao))
            {
                gravar.Parameters.AddWithValue("@token", Convert.ToHexString(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                gravar.Parameters.AddWithValue("@id", id);
                gravar.ExecuteNonQuery();
            }

            using var ler = new MySqlCommand(
                "SELECT token_confirmacao FROM agendamento WHERE id_agend = @id", conexao);
            ler.Parameters.AddWithValue("@id", id);
            return Convert.ToString(ler.ExecuteScalar())
                ?? throw new Exception("Agendamento não encontrado.");
        }

        public Agendamento? BuscarPorToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length != 32) return null;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + " WHERE a.token_confirmacao = @token LIMIT 1", conexao);
            comando.Parameters.AddWithValue("@token", token);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>
        /// O cliente confirma pelo link. So vale para horario em aberto que
        /// ainda nao passou; confirmar de novo nao muda a data da primeira.
        /// </summary>
        public bool ConfirmarPeloCliente(string token)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE agendamento SET confirmado_em = COALESCE(confirmado_em, NOW())
                WHERE token_confirmacao = @token AND status = @agendado AND data_hora > NOW()", conexao);
            comando.Parameters.AddWithValue("@token", token);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            return comando.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// O cliente cancela pelo link: o horario fica livre na hora, e a
        /// data do cancelamento serve para o painel avisar a equipe.
        /// </summary>
        public bool CancelarPeloCliente(string token)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE agendamento SET status = @cancelado, cancelado_cliente_em = NOW()
                WHERE token_confirmacao = @token AND status = @agendado AND data_hora > NOW()", conexao);
            comando.Parameters.AddWithValue("@cancelado", StatusAgendamento.Cancelado);
            comando.Parameters.AddWithValue("@token", token);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            return comando.ExecuteNonQuery() > 0;
        }

        /// <summary>Cancelados pelo cliente desde uma data, de horarios que ainda nao passaram.</summary>
        public List<Agendamento> CanceladosPeloCliente(DateTime desde)
        {
            var lista = new List<Agendamento>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + @" WHERE a.cancelado_cliente_em >= @desde AND a.data_hora >= @hoje
                                   ORDER BY a.data_hora", conexao);
            comando.Parameters.AddWithValue("@desde", desde);
            comando.Parameters.AddWithValue("@hoje", DateTime.Today);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        /// <summary>Marcados pelo proprio cliente no link publico desde uma data, ainda em aberto.</summary>
        public List<Agendamento> CriadosOnline(DateTime desde)
        {
            var lista = new List<Agendamento>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + @" WHERE a.origem = @online AND a.data_criacao >= @desde
                                   AND a.status = @agendado AND a.data_hora >= @hoje
                                   ORDER BY a.data_hora", conexao);
            comando.Parameters.AddWithValue("@online", OrigemAgendamento.Online);
            comando.Parameters.AddWithValue("@desde", desde);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            comando.Parameters.AddWithValue("@hoje", DateTime.Today);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        /// <summary>Marca a falta do cliente; só vale para horário ainda em aberto.</summary>
        public void MarcarFalta(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE agendamento SET status = @falta
                WHERE id_agend = @id AND status = @agendado", conexao);

            comando.Parameters.AddWithValue("@falta", StatusAgendamento.Faltou);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            comando.Parameters.AddWithValue("@id", id);

            if (comando.ExecuteNonQuery() == 0)
                throw new Exception("Só um agendamento em aberto pode ser marcado como falta.");
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
            // Quem so preenche IdServico (um servico) continua valendo.
            if (agendamento.IdsServicos.Count == 0 && agendamento.IdServico > 0)
                agendamento.IdsServicos.Add(agendamento.IdServico);

            agendamento.IdsServicos = agendamento.IdsServicos.Where(id => id > 0).Distinct().ToList();
            agendamento.IdServico = agendamento.IdsServicos.FirstOrDefault();

            if (agendamento.IdCliente <= 0) throw new Exception("Selecione o cliente.");
            if (agendamento.IdFuncionario <= 0) throw new Exception("Selecione o profissional.");
            if (agendamento.IdServico <= 0) throw new Exception("Selecione ao menos um serviço.");
            if (agendamento.DataHora == default) throw new Exception("Informe a data e a hora.");
            if (agendamento.DuracaoMin <= 0) throw new Exception("A duração deve ser maior que zero.");
        }

        /// <summary>
        /// Fila por profissional: quem grava horario dele espera quem ja
        /// esta gravando. Sem isto, dois pedidos ao mesmo tempo -- o balcao
        /// e o link publico, ou dois clientes no link -- conferiam o
        /// conflito antes de qualquer um gravar e marcavam o mesmo horario.
        /// A linha do funcionario serve de trava mesmo com a agenda vazia,
        /// quando nao ha agendamento nenhum para o FOR UPDATE segurar.
        /// </summary>
        internal static void TravarProfissional(MySqlConnection conexao, MySqlTransaction transacao, int idFuncionario)
        {
            using var comando = new MySqlCommand(
                "SELECT id_fun FROM funcionario WHERE id_fun = @id FOR UPDATE", conexao, transacao);
            comando.Parameters.AddWithValue("@id", idFuncionario);
            comando.ExecuteScalar();
        }

        /// <summary>
        /// O mesmo profissional nao atende dois horarios que se cruzam.
        /// Cancelados e concluidos nao ocupam a agenda. Roda na transacao da
        /// gravacao, depois de TravarProfissional.
        /// </summary>
        private static void VerificarConflito(Agendamento agendamento, MySqlConnection conexao, MySqlTransaction transacao)
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
                LIMIT 1", conexao, transacao);

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

            // Colunas da confirmacao: se a migracao nao rodou, ficam nulas em
            // vez de derrubar a agenda inteira.
            DateTime? Data(string coluna)
            {
                for (var i = 0; i < leitor.FieldCount; i++)
                    if (leitor.GetName(i) == coluna)
                        return leitor.IsDBNull(i) ? null : leitor.GetDateTime(i);
                return null;
            }

            string? TextoSeHouver(string coluna)
            {
                for (var i = 0; i < leitor.FieldCount; i++)
                    if (leitor.GetName(i) == coluna)
                        return leitor.IsDBNull(i) ? null : leitor.GetString(i);
                return null;
            }

            var iVenda = leitor.GetOrdinal("id_vend_fk");
            var iPreco = leitor.GetOrdinal("preco_serv");

            var idServico = Inteiro("id_serv_fk");
            var ids = (Texto("ids_serv") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList();
            if (ids.Count == 0 && idServico > 0) ids.Add(idServico);

            return new Agendamento
            {
                IdsServicos = ids,
                IdAgendamento = leitor.GetInt32("id_agend"),
                DataHora = leitor.GetDateTime("data_hora"),
                DuracaoMin = Inteiro("duracao_min"),
                IdCliente = Inteiro("id_cli_fk"),
                IdFuncionario = Inteiro("id_fun_fk"),
                IdServico = idServico,
                Observacao = Texto("observacao"),
                Status = Texto("status") ?? StatusAgendamento.Agendado,
                IdVenda = leitor.IsDBNull(iVenda) ? null : leitor.GetInt32(iVenda),
                ConfirmadoEm = Data("confirmado_em"),
                CanceladoClienteEm = Data("cancelado_cliente_em"),
                Origem = TextoSeHouver("origem"),
                NomeCliente = Texto("nome_cli"),
                TelefoneCliente = Texto("telefone_cli"),
                NomeFuncionario = Texto("nome_fun"),
                NomeServico = Texto("nome_serv"),
                // Soma de FLOAT com DECIMAL vem como double: converte em vez de GetDecimal.
                PrecoServico = leitor.IsDBNull(iPreco) ? 0m : Convert.ToDecimal(leitor.GetValue(iPreco))
            };
        }
    }
}
