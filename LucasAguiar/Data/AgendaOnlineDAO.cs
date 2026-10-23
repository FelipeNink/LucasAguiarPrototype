using System.Security.Cryptography;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Erro que o cliente da pagina publica pode ler: horario que acabou de
    /// ser ocupado, telefone invalido, limite atingido. Qualquer outra
    /// excecao e falha do sistema e vira mensagem generica.
    /// </summary>
    public class AgendaOnlineException : Exception
    {
        /// <summary>O horario nao esta mais livre: a pagina volta para a escolha do horario.</summary>
        public bool HorarioOcupado { get; }

        public AgendaOnlineException(string mensagem, bool horarioOcupado = false) : base(mensagem)
        {
            HorarioOcupado = horarioOcupado;
        }
    }

    /// <summary>O que o cliente escolheu na pagina publica.</summary>
    public class PedidoOnline
    {
        public List<int> Servicos { get; set; } = new();

        /// <summary>0 = qualquer profissional livre no horario.</summary>
        public int IdFuncionario { get; set; }

        public DateTime Inicio { get; set; }
        public string Nome { get; set; } = "";
        public string Telefone { get; set; } = "";
        public string? Observacao { get; set; }
    }

    /// <summary>O horario marcado, para a tela de sucesso.</summary>
    public record ResultadoOnline(
        int IdAgendamento, string Token, DateTime Inicio, int DuracaoMin,
        string NomeFuncionario, string NomeServicos, decimal Preco);

    /// <summary>
    /// Profissional ou servico na tela de configuracao, com a marca de
    /// "aparece no link". Indisponivel: servico sem duracao, que nao da
    /// para oferecer.
    /// </summary>
    public record ItemOnline(int Id, string Nome, string? Detalhe, bool Online, bool Disponivel = true);

    /// <summary>
    /// Agendamento pelo link publico: configuracao, o que aparece para o
    /// cliente, os horarios livres e a gravacao.
    ///
    /// Os horarios livres saem do QuadroAgenda, que segue a jornada como
    /// regra: no balcao a jornada so avisa (encaixe acontece), mas o
    /// cliente sozinho nunca marca fora dela.
    /// </summary>
    public class AgendaOnlineDAO
    {
        private readonly Conexao _conexao;

        public const int TamanhoNome = 100;
        public const int TamanhoObservacao = 200;

        public AgendaOnlineDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        // ---------- configuracao ----------

        /// <summary>A linha de configuracao; sem ela (migracao nao rodou), desligado.</summary>
        public ConfigAgendaOnline LerConfig()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT * FROM agenda_online WHERE id = 1", conexao);
            using var leitor = comando.ExecuteReader();

            if (!leitor.Read()) return new ConfigAgendaOnline();

            string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

            return new ConfigAgendaOnline
            {
                Ativo = leitor.GetBoolean("ativo"),
                AntecedenciaMin = leitor.GetInt32("antecedencia_min"),
                DiasAFrente = leitor.GetInt32("dias_a_frente"),
                IntervaloMin = leitor.GetInt32("intervalo_min"),
                MaximoAbertos = leitor.GetInt32("maximo_abertos"),
                Aviso = Texto("aviso"),
                WhatsApp = Texto("whatsapp")
            };
        }

        /// <summary>
        /// Grava a configuracao e quem aparece no link, tudo junto. Quem nao
        /// esta nas listas sai do link (0), e nao volta ao padrao.
        /// </summary>
        public void SalvarConfig(ConfigAgendaOnline config, IReadOnlyCollection<int> profissionais, IReadOnlyCollection<int> servicos)
        {
            if (config.Erro() is string problema) throw new Exception(problema);
            if (config.Ativo && profissionais.Count == 0)
                throw new Exception("Para ligar o agendamento online, escolha ao menos um profissional.");
            if (config.Ativo && servicos.Count == 0)
                throw new Exception("Para ligar o agendamento online, escolha ao menos um serviço.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                using (var comando = new MySqlCommand(@"
                    INSERT INTO agenda_online
                        (id, ativo, antecedencia_min, dias_a_frente, intervalo_min, maximo_abertos, aviso, whatsapp)
                    VALUES (1, @ativo, @antecedencia, @dias, @intervalo, @maximo, @aviso, @whatsapp) AS novo
                    ON DUPLICATE KEY UPDATE ativo = novo.ativo, antecedencia_min = novo.antecedencia_min,
                        dias_a_frente = novo.dias_a_frente, intervalo_min = novo.intervalo_min,
                        maximo_abertos = novo.maximo_abertos, aviso = novo.aviso, whatsapp = novo.whatsapp",
                    conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@ativo", config.Ativo ? 1 : 0);
                    comando.Parameters.AddWithValue("@antecedencia", config.AntecedenciaMin);
                    comando.Parameters.AddWithValue("@dias", config.DiasAFrente);
                    comando.Parameters.AddWithValue("@intervalo", config.IntervaloMin);
                    comando.Parameters.AddWithValue("@maximo", config.MaximoAbertos);
                    comando.Parameters.AddWithValue("@aviso",
                        string.IsNullOrWhiteSpace(config.Aviso) ? DBNull.Value : config.Aviso.Trim());
                    comando.Parameters.AddWithValue("@whatsapp",
                        string.IsNullOrWhiteSpace(config.WhatsApp) ? DBNull.Value : Documentos.FormatarTelefone(config.WhatsApp));
                    comando.ExecuteNonQuery();
                }

                Marcar(conexao, transacao, "funcionario", "online_fun", "id_fun", profissionais);
                Marcar(conexao, transacao, "servico", "online_serv", "id_serv", servicos);

                transacao.Commit();
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        /// <summary>1 para os ids da lista, 0 para o resto da tabela.</summary>
        private static void Marcar(
            MySqlConnection conexao, MySqlTransaction transacao,
            string tabela, string coluna, string chave, IReadOnlyCollection<int> ids)
        {
            var nomes = ids.Select((_, i) => "@i" + i).ToList();
            var dentro = nomes.Count == 0 ? "0" : $"{chave} IN ({string.Join(',', nomes)})";

            // Tabela e colunas vem do codigo; os ids vao como parametro.
            using var comando = new MySqlCommand(
                $"UPDATE `{tabela}` SET `{coluna}` = IF({dentro}, 1, 0)", conexao, transacao);
            var i = 0;
            foreach (var id in ids) comando.Parameters.AddWithValue("@i" + i++, id);
            comando.ExecuteNonQuery();
        }

        // Sem marca gravada: barbeiro (ou sem cargo, do cadastro antigo) aparece;
        // recepcao, gerente e auxiliar nao. Servico ativo aparece.
        private const string ProfissionalNoLink =
            "COALESCE(f.online_fun, f.cargo_fun = 'BARBEIRO' OR f.cargo_fun IS NULL) = 1";

        private const string ProfissionalAtivo =
            "f.ativo_fun = 1 AND (f.data_desligamento IS NULL OR f.data_desligamento > CURDATE())";

        /// <summary>Profissionais ativos para a tela de configuracao.</summary>
        public List<ItemOnline> ListarProfissionaisConfig()
        {
            var lista = new List<ItemOnline>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT f.id_fun, f.nome_fun, f.cargo_fun, {ProfissionalNoLink} AS online
                  FROM funcionario f
                 WHERE {ProfissionalAtivo}
                 ORDER BY f.nome_fun", conexao);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                lista.Add(new ItemOnline(
                    leitor.GetInt32("id_fun"),
                    DAOHelper.IsNull(leitor, "nome_fun") ? "Sem nome" : leitor.GetString("nome_fun"),
                    DAOHelper.IsNull(leitor, "cargo_fun") ? null : CargoFuncionario.Rotulo(leitor.GetString("cargo_fun")),
                    Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("online"))) == 1));
            }

            return lista;
        }

        /// <summary>Servicos ativos para a tela de configuracao. Sem duracao nao da para oferecer.</summary>
        public List<ItemOnline> ListarServicosConfig()
        {
            var lista = new List<ItemOnline>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT s.id_serv, s.nome_serv, s.duracao_min, s.preco_serv,
                       COALESCE(s.online_serv, 1) = 1 AND COALESCE(s.duracao_min, 0) > 0 AS online
                  FROM servico s
                 WHERE s.ativo_serv = 1
                 ORDER BY s.nome_serv", conexao);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                var duracao = DAOHelper.IsNull(leitor, "duracao_min") ? 0 : leitor.GetInt32("duracao_min");
                var preco = DAOHelper.IsNull(leitor, "preco_serv") ? 0m : Convert.ToDecimal(leitor.GetValue(leitor.GetOrdinal("preco_serv")));

                lista.Add(new ItemOnline(
                    leitor.GetInt32("id_serv"),
                    DAOHelper.IsNull(leitor, "nome_serv") ? "Sem nome" : leitor.GetString("nome_serv"),
                    duracao > 0 ? $"{duracao} min · {preco:C}" : "sem duração cadastrada",
                    Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("online"))) == 1,
                    duracao > 0));
            }

            return lista;
        }

        // ---------- o que o cliente ve ----------

        /// <summary>Servicos do link, por nome. So id, nome, preco e duracao vem preenchidos.</summary>
        public List<Servico> ServicosOnline()
        {
            var lista = new List<Servico>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT s.id_serv, s.nome_serv, s.preco_serv, s.duracao_min
                  FROM servico s
                 WHERE s.ativo_serv = 1 AND COALESCE(s.online_serv, 1) = 1 AND s.duracao_min > 0
                 ORDER BY s.nome_serv", conexao);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                lista.Add(new Servico
                {
                    IdServico = leitor.GetInt32("id_serv"),
                    NomeServico = DAOHelper.IsNull(leitor, "nome_serv") ? "" : leitor.GetString("nome_serv"),
                    PrecoServico = DAOHelper.IsNull(leitor, "preco_serv") ? 0f : leitor.GetFloat(leitor.GetOrdinal("preco_serv")),
                    DuracaoServico = leitor.GetInt32("duracao_min")
                });
            }

            return lista;
        }

        /// <summary>Profissionais do link, com os servicos que fazem e o preco proprio.</summary>
        public List<ProfissionalOnline> ProfissionaisOnline()
        {
            var lista = new List<ProfissionalOnline>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand($@"
                SELECT f.id_fun, f.nome_fun, f.cor_agenda
                  FROM funcionario f
                 WHERE {ProfissionalAtivo} AND {ProfissionalNoLink}
                 ORDER BY f.nome_fun", conexao))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    lista.Add(new ProfissionalOnline
                    {
                        IdFuncionario = leitor.GetInt32("id_fun"),
                        Nome = DAOHelper.IsNull(leitor, "nome_fun") ? "Profissional" : leitor.GetString("nome_fun"),
                        Cor = DAOHelper.IsNull(leitor, "cor_agenda") ? null : leitor.GetString("cor_agenda")
                    });
                }
            }

            if (lista.Count == 0) return lista;

            var porId = lista.ToDictionary(p => p.IdFuncionario);
            using (var comando = new MySqlCommand(
                "SELECT id_fun_fk, id_serv_fk, preco FROM funcionario_servico", conexao))
            using (var leitor = comando.ExecuteReader())
            {
                var iPreco = leitor.GetOrdinal("preco");
                while (leitor.Read())
                {
                    if (!porId.TryGetValue(leitor.GetInt32("id_fun_fk"), out var p)) continue;

                    var servico = leitor.GetInt32("id_serv_fk");
                    p.Servicos.Add(servico);
                    if (!leitor.IsDBNull(iPreco)) p.Precos[servico] = leitor.GetDecimal(iPreco);
                }
            }

            return lista;
        }

        /// <summary>
        /// Grade, ajustes do dia, jornadas, bloqueios e horarios em aberto
        /// dos profissionais no periodo [de, ate), numa leitura so.
        /// </summary>
        public QuadroAgenda CarregarQuadro(IReadOnlyCollection<int> profissionais, DateTime de, DateTime ate)
        {
            var quadro = CarregarSemAgenda(profissionais, de, ate);

            using var conexao = _conexao.GetConnection();
            foreach (var (id, inicio, fim) in LerAgendados(conexao, null, profissionais, de, ate))
                quadro.Ocupados[id].Add(new Periodo(inicio, fim));

            return quadro;
        }

        /// <summary>O quadro so com os bloqueios em Ocupados; os horarios marcados ficam para quem chama.</summary>
        private QuadroAgenda CarregarSemAgenda(IReadOnlyCollection<int> profissionais, DateTime de, DateTime ate)
        {
            var grade = new HorarioDAO(_conexao).Listar();
            var jornadaDAO = new JornadaDAO(_conexao);

            var jornadas = new Dictionary<int, List<JornadaDia>?>();
            var ocupados = new Dictionary<int, List<Periodo>>();
            foreach (var id in profissionais)
            {
                jornadas[id] = jornadaDAO.Listar(id);
                ocupados[id] = jornadaDAO.ListarBloqueios(id, de, ate)
                    .Select(b => new Periodo(b.Inicio, b.Fim))
                    .ToList();
            }

            var excecoes = new Dictionary<DateTime, bool>();
            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(
                "SELECT data, aberto FROM horario_excecao WHERE data >= @de AND data < @ate", conexao))
            {
                comando.Parameters.AddWithValue("@de", de.Date);
                comando.Parameters.AddWithValue("@ate", ate);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                    excecoes[leitor.GetDateTime("data").Date] = leitor.GetBoolean("aberto");
            }

            return new QuadroAgenda { Grade = grade, Excecoes = excecoes, Jornadas = jornadas, Ocupados = ocupados };
        }

        /// <summary>Horarios em aberto dos profissionais que cruzam [de, ate).</summary>
        private static List<(int IdFuncionario, DateTime Inicio, DateTime Fim)> LerAgendados(
            MySqlConnection conexao, MySqlTransaction? transacao,
            IReadOnlyCollection<int> profissionais, DateTime de, DateTime ate)
        {
            var lista = new List<(int, DateTime, DateTime)>();
            if (profissionais.Count == 0) return lista;

            var nomes = profissionais.Select((_, i) => "@f" + i).ToList();
            using var comando = new MySqlCommand($@"
                SELECT id_fun_fk, data_hora, duracao_min FROM agendamento
                 WHERE status = @agendado AND id_fun_fk IN ({string.Join(',', nomes)})
                   AND data_hora < @ate AND DATE_ADD(data_hora, INTERVAL duracao_min MINUTE) > @de",
                conexao, transacao);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);
            var i = 0;
            foreach (var id in profissionais) comando.Parameters.AddWithValue("@f" + i++, id);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var inicio = leitor.GetDateTime("data_hora");
                lista.Add((leitor.GetInt32("id_fun_fk"), inicio, inicio.AddMinutes(leitor.GetInt32("duracao_min"))));
            }

            return lista;
        }

        // ---------- gravacao ----------

        /// <summary>
        /// Marca o horario pedido pelo cliente. Confere tudo de novo, sem
        /// confiar no que a pagina mostrou: a pagina pode estar aberta ha
        /// meia hora, e o pedido pode nao ter vindo dela.
        ///
        /// A disputa pelo horario se resolve na transacao: trava os
        /// profissionais candidatos (a mesma trava do balcao), rele os
        /// horarios em aberto e so entao grava. Quem chegar segundo ve o
        /// horario ocupado e escolhe outro.
        /// </summary>
        public ResultadoOnline Agendar(PedidoOnline pedido, DateTime agora)
        {
            var config = LerConfig();
            if (!config.Ativo)
                throw new AgendaOnlineException("O agendamento online está desligado no momento.");

            var nome = (pedido.Nome ?? "").Trim();
            if (nome.Length < 2) throw new AgendaOnlineException("Informe seu nome.");
            if (nome.Length > TamanhoNome) nome = nome[..TamanhoNome];

            var digitos = Documentos.SoDigitos(pedido.Telefone);
            if (digitos.Length is 12 or 13 && digitos.StartsWith("55")) digitos = digitos[2..];
            if (!Documentos.TelefoneValido(digitos))
                throw new AgendaOnlineException("Telefone incorreto. Use DDD + número.");

            var observacao = string.IsNullOrWhiteSpace(pedido.Observacao) ? null : pedido.Observacao.Trim();
            if (observacao?.Length > TamanhoObservacao) observacao = observacao[..TamanhoObservacao];

            // Servicos: so os do link, na ordem em que o cliente escolheu.
            var oferecidos = ServicosOnline().ToDictionary(s => s.IdServico);
            var servicos = pedido.Servicos.Distinct().ToList();
            if (servicos.Count == 0 || servicos.Any(id => !oferecidos.ContainsKey(id)))
                throw new AgendaOnlineException("Escolha ao menos um serviço da lista.");
            var duracao = servicos.Sum(id => oferecidos[id].DuracaoServico);

            var profissionais = ProfissionaisOnline().Where(p => p.Faz(servicos)).ToList();
            if (pedido.IdFuncionario > 0)
                profissionais = profissionais.Where(p => p.IdFuncionario == pedido.IdFuncionario).ToList();
            if (profissionais.Count == 0)
                throw new AgendaOnlineException("Esse profissional não está disponível para estes serviços.");

            var inicio = pedido.Inicio;
            var fim = inicio.AddMinutes(duracao);
            if (inicio < agora.AddMinutes(config.AntecedenciaMin) || inicio.Date >= agora.Date.AddDays(config.DiasAFrente))
                throw new AgendaOnlineException("Esse horário não está mais disponível. Escolha outro.", true);

            // Jornada, bloqueios e expediente: lidos antes da transacao. O que
            // pode mudar entre um clique e outro e a agenda, relida abaixo.
            var ids = profissionais.Select(p => p.IdFuncionario).OrderBy(id => id).ToList();
            var quadro = CarregarSemAgenda(ids, inicio.Date, inicio.Date.AddDays(1));

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                // Sempre na ordem do id: dois pedidos de "qualquer um" travam
                // na mesma ordem e nao se prendem um ao outro.
                foreach (var id in ids) AgendamentoDAO.TravarProfissional(conexao, transacao, id);

                // Lido depois da trava: e a agenda de agora, com o que o
                // pedido que estava na frente acabou de gravar.
                var agendados = LerAgendados(conexao, transacao, ids, inicio.Date, inicio.Date.AddDays(1));
                foreach (var (id, de, ate) in agendados) quadro.Ocupados[id].Add(new Periodo(de, ate));

                // "Qualquer um": entre os livres, o de agenda mais leve no dia.
                var livre = ids
                    .Where(id => quadro.Livre(id, inicio, duracao, agora.AddMinutes(config.AntecedenciaMin)))
                    .OrderBy(id => agendados.Count(a => a.IdFuncionario == id))
                    .ThenBy(id => id)
                    .Select(id => (int?)id)
                    .FirstOrDefault();

                if (livre is not int idFuncionario)
                    throw new AgendaOnlineException("Esse horário acabou de ser ocupado. Escolha outro.", true);

                var idCliente = GarantirCliente(conexao, transacao, nome, digitos);
                ConferirCliente(conexao, transacao, idCliente, inicio, fim, config.MaximoAbertos);

                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                int idAgendamento;
                using (var comando = new MySqlCommand(@"
                    INSERT INTO agendamento
                        (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status, origem, token_confirmacao)
                    VALUES
                        (@inicio, @duracao, @cliente, @funcionario, @servico, @observacao, @status, @origem, @token);
                    SELECT LAST_INSERT_ID();", conexao, transacao))
                {
                    comando.Parameters.AddWithValue("@inicio", inicio);
                    comando.Parameters.AddWithValue("@duracao", duracao);
                    comando.Parameters.AddWithValue("@cliente", idCliente);
                    comando.Parameters.AddWithValue("@funcionario", idFuncionario);
                    comando.Parameters.AddWithValue("@servico", servicos[0]);
                    comando.Parameters.AddWithValue("@observacao", observacao ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@status", StatusAgendamento.Agendado);
                    comando.Parameters.AddWithValue("@origem", OrigemAgendamento.Online);
                    comando.Parameters.AddWithValue("@token", token);
                    idAgendamento = Convert.ToInt32(comando.ExecuteScalar());
                }

                AgendamentoDAO.GravarServicos(conexao, transacao, idAgendamento, servicos);
                transacao.Commit();

                var profissional = profissionais.First(p => p.IdFuncionario == idFuncionario);
                var preco = servicos.Sum(id => profissional.Precos.TryGetValue(id, out var proprio)
                    ? proprio
                    : (decimal)oferecidos[id].PrecoServico);

                return new ResultadoOnline(
                    idAgendamento, token, inicio, duracao, profissional.Nome,
                    string.Join(" + ", servicos.Select(id => oferecidos[id].NomeServico)), preco);
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        /// <summary>
        /// O cliente pelo telefone, ou um cadastro novo com nome e telefone.
        /// O nome digitado nao muda o cadastro que ja existe: no link nao ha
        /// como saber se quem digitou e o dono do numero.
        /// </summary>
        private static int GarantirCliente(MySqlConnection conexao, MySqlTransaction transacao, string nome, string digitos)
        {
            int? id;
            using (var buscar = new MySqlCommand(@"
                SELECT id_cli FROM cliente
                 WHERE ativo_cli = 1
                   AND REGEXP_REPLACE(COALESCE(telefone_cli, ''), '[^0-9]', '') IN (@d, @d55, @d0)
                 ORDER BY id_cli LIMIT 1", conexao, transacao))
            {
                buscar.Parameters.AddWithValue("@d", digitos);
                buscar.Parameters.AddWithValue("@d55", "55" + digitos);
                buscar.Parameters.AddWithValue("@d0", "0" + digitos);
                var valor = buscar.ExecuteScalar();
                id = valor == null || valor == DBNull.Value ? null : Convert.ToInt32(valor);
            }

            if (id is int existente)
            {
                // Trava so a linha achada (pela chave), e nao a tabela que o
                // REGEXP percorreu: dois pedidos do mesmo telefone fazem fila
                // para a conferencia do limite.
                using var travar = new MySqlCommand(
                    "SELECT id_cli FROM cliente WHERE id_cli = @id FOR UPDATE", conexao, transacao);
                travar.Parameters.AddWithValue("@id", existente);
                travar.ExecuteScalar();
                return existente;
            }

            using var inserir = new MySqlCommand(@"
                INSERT INTO cliente (nome_cli, telefone_cli, ativo_cli) VALUES (@nome, @telefone, 1);
                SELECT LAST_INSERT_ID();", conexao, transacao);
            inserir.Parameters.AddWithValue("@nome", nome);
            inserir.Parameters.AddWithValue("@telefone", Documentos.FormatarTelefone(digitos));
            return Convert.ToInt32(inserir.ExecuteScalar());
        }

        /// <summary>
        /// Limite de horarios abertos pelo link por telefone, e nada de dois
        /// horarios do mesmo cliente que se cruzam -- com "qualquer um", o
        /// segundo clique cairia com outro profissional.
        /// </summary>
        private static void ConferirCliente(
            MySqlConnection conexao, MySqlTransaction transacao, int idCliente, DateTime inicio, DateTime fim, int maximo)
        {
            using var comando = new MySqlCommand(@"
                SELECT
                    SUM(origem = @online AND data_hora > NOW()) AS abertos,
                    SUM(data_hora < @fim AND DATE_ADD(data_hora, INTERVAL duracao_min MINUTE) > @inicio) AS cruzam
                  FROM agendamento
                 WHERE id_cli_fk = @cliente AND status = @agendado", conexao, transacao);
            comando.Parameters.AddWithValue("@online", OrigemAgendamento.Online);
            comando.Parameters.AddWithValue("@cliente", idCliente);
            comando.Parameters.AddWithValue("@agendado", StatusAgendamento.Agendado);
            comando.Parameters.AddWithValue("@inicio", inicio);
            comando.Parameters.AddWithValue("@fim", fim);

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return;

            int Numero(string coluna) => DAOHelper.IsNull(leitor, coluna) ? 0 : Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal(coluna)));

            if (Numero("cruzam") > 0)
                throw new AgendaOnlineException("Você já tem um horário marcado nesse período.");

            if (Numero("abertos") >= maximo)
                throw new AgendaOnlineException(maximo == 1
                    ? "Você já tem um horário marcado pelo link. Para marcar outro, fale com a barbearia."
                    : $"Você já tem {maximo} horários marcados pelo link. Para marcar mais, fale com a barbearia.");
        }
    }
}
