namespace LucasAguiar.Models
{
    /// <summary>De onde o agendamento veio. Null e o balcao: a equipe marcou.</summary>
    public static class OrigemAgendamento
    {
        /// <summary>O proprio cliente marcou pelo link publico /agendar.</summary>
        public const string Online = "ONLINE";
    }

    /// <summary>Regras do agendamento pelo link publico. Uma linha so, id 1.</summary>
    public class ConfigAgendaOnline
    {
        /// <summary>Desligado, a pagina publica so mostra o aviso e o WhatsApp.</summary>
        public bool Ativo { get; set; }

        /// <summary>Quanto antes do horario o cliente ainda pode marcar, em minutos.</summary>
        public int AntecedenciaMin { get; set; } = 60;

        /// <summary>Ate quantos dias a frente a agenda fica aberta, contando hoje.</summary>
        public int DiasAFrente { get; set; } = 30;

        /// <summary>De quanto em quanto tempo os horarios sao oferecidos.</summary>
        public int IntervaloMin { get; set; } = 15;

        /// <summary>Horarios futuros marcados pelo link que um mesmo telefone pode ter.</summary>
        public int MaximoAbertos { get; set; } = 2;

        /// <summary>Recado no topo da pagina publica ("chegue 5 minutos antes").</summary>
        public string? Aviso { get; set; }

        /// <summary>WhatsApp da barbearia, para o "fale com a gente" da pagina.</summary>
        public string? WhatsApp { get; set; }

        public static readonly int[] Intervalos = { 10, 15, 20, 30, 60 };

        /// <summary>Primeiro problema da configuracao, ou null se estiver valida.</summary>
        public string? Erro()
        {
            if (AntecedenciaMin is < 0 or > 7 * 24 * 60)
                return "A antecedência mínima deve ficar entre 0 e 7 dias.";
            if (DiasAFrente is < 1 or > 180)
                return "A agenda pode abrir de 1 a 180 dias à frente.";
            if (!Intervalos.Contains(IntervaloMin))
                return "Escolha um dos intervalos da lista.";
            if (MaximoAbertos is < 1 or > 10)
                return "O limite por telefone deve ficar entre 1 e 10 horários.";
            if (!string.IsNullOrWhiteSpace(WhatsApp) && LucasAguiar.Configs.WhatsApp.Numero(WhatsApp) == null)
                return "WhatsApp da barbearia incorreto. Use DDD + número.";
            return null;
        }
    }

    /// <summary>Profissional que aparece no link, com os servicos que faz.</summary>
    public class ProfissionalOnline
    {
        public int IdFuncionario { get; set; }
        public string Nome { get; set; } = "";
        public string? Cor { get; set; }

        /// <summary>Vazio: faz todos. E a mesma leitura do cadastro do profissional.</summary>
        public HashSet<int> Servicos { get; set; } = new();

        /// <summary>Preco proprio por servico, quando o profissional cobra diferente.</summary>
        public Dictionary<int, decimal> Precos { get; set; } = new();

        public bool Faz(IEnumerable<int> servicos) =>
            Servicos.Count == 0 || servicos.All(Servicos.Contains);
    }

    /// <summary>Um periodo ocupado: horario marcado ou bloqueio.</summary>
    public readonly record struct Periodo(DateTime Inicio, DateTime Fim)
    {
        public bool Cruza(DateTime inicio, DateTime fim) => Inicio < fim && Fim > inicio;
    }

    /// <summary>
    /// Tudo o que decide a agenda de alguns profissionais num periodo,
    /// carregado de uma vez do banco. Os horarios livres saem daqui sem
    /// mais nenhuma consulta: a pagina troca de dia sem ir ao banco por
    /// profissional, e a regra e testavel sem MySQL.
    /// </summary>
    public class QuadroAgenda
    {
        public IReadOnlyList<HorarioDia> Grade { get; init; } = new List<HorarioDia>();

        /// <summary>Abrir/fechar na mao, por data.</summary>
        public IReadOnlyDictionary<DateTime, bool> Excecoes { get; init; } = new Dictionary<DateTime, bool>();

        /// <summary>Null no valor: o profissional nao tem jornada e segue a barbearia.</summary>
        public IReadOnlyDictionary<int, List<JornadaDia>?> Jornadas { get; init; } = new Dictionary<int, List<JornadaDia>?>();

        /// <summary>Bloqueios e horarios em aberto, juntos: os dois tiram o profissional da agenda.</summary>
        public IReadOnlyDictionary<int, List<Periodo>> Ocupados { get; init; } = new Dictionary<int, List<Periodo>>();

        /// <summary>Horarios livres de um profissional no dia, do mais cedo ao mais tarde.</summary>
        public List<DateTime> Livres(int idFuncionario, DateTime dia, int duracaoMin, int intervaloMin, DateTime aPartirDe)
        {
            var livres = new List<DateTime>();
            if (duracaoMin <= 0 || intervaloMin <= 0) return livres;

            dia = dia.Date;
            var janela = Janela(idFuncionario, dia);
            if (janela == null) return livres;
            var (abre, fecha, almoco) = janela.Value;

            var ocupados = OcupadosDoDia(idFuncionario, almoco);

            // Candidatos: a grade do intervalo, contada da meia-noite (9:00,
            // 9:15...), mais o fim de cada periodo ocupado. Sem o segundo, um
            // corte que acaba 9:40 deixaria o buraco ate 9:45 sem uso.
            var candidatos = new SortedSet<DateTime>();
            var primeiro = dia.AddMinutes(Math.Ceiling((abre - dia).TotalMinutes / intervaloMin) * intervaloMin);
            for (var t = primeiro; t < fecha; t = t.AddMinutes(intervaloMin)) candidatos.Add(t);
            foreach (var o in ocupados)
                if (o.Fim > abre && o.Fim < fecha) candidatos.Add(o.Fim);

            foreach (var inicio in candidatos)
                if (Cabe(inicio, duracaoMin, abre, fecha, ocupados, aPartirDe)) livres.Add(inicio);

            return livres;
        }

        /// <summary>
        /// O horario exato esta livre para o profissional? E a conferencia da
        /// gravacao: nao depende da grade de candidatos, so da regra.
        /// </summary>
        public bool Livre(int idFuncionario, DateTime inicio, int duracaoMin, DateTime aPartirDe)
        {
            if (duracaoMin <= 0) return false;

            var janela = Janela(idFuncionario, inicio.Date);
            if (janela == null) return false;
            var (abre, fecha, almoco) = janela.Value;

            return Cabe(inicio, duracaoMin, abre, fecha, OcupadosDoDia(idFuncionario, almoco), aPartirDe);
        }

        private static bool Cabe(
            DateTime inicio, int duracaoMin, DateTime abre, DateTime fecha, List<Periodo> ocupados, DateTime aPartirDe)
        {
            var fim = inicio.AddMinutes(duracaoMin);
            if (inicio < abre || inicio < aPartirDe || fim > fecha) return false;
            return !ocupados.Any(o => o.Cruza(inicio, fim));
        }

        private List<Periodo> OcupadosDoDia(int idFuncionario, Periodo? almoco)
        {
            var ocupados = Ocupados.TryGetValue(idFuncionario, out var lista) ? lista.ToList() : new List<Periodo>();
            if (almoco is Periodo a) ocupados.Add(a);
            return ocupados;
        }

        /// <summary>
        /// Horarios livres de varios profissionais juntos, cada um com quem
        /// pode atender. E o "qualquer profissional" da pagina.
        /// </summary>
        public SortedDictionary<DateTime, List<int>> LivresDeTodos(
            IEnumerable<int> profissionais, DateTime dia, int duracaoMin, int intervaloMin, DateTime aPartirDe)
        {
            var mapa = new SortedDictionary<DateTime, List<int>>();
            foreach (var id in profissionais)
            {
                foreach (var inicio in Livres(id, dia, duracaoMin, intervaloMin, aPartirDe))
                {
                    if (!mapa.TryGetValue(inicio, out var quem)) mapa[inicio] = quem = new List<int>();
                    quem.Add(id);
                }
            }
            return mapa;
        }

        /// <summary>
        /// Abertura, fechamento e almoco do profissional no dia, ou null se
        /// ele nao atende. A barbearia fechada na mao fecha para todos; aberta
        /// na mao, vale so a jornada de cada um.
        /// </summary>
        private (DateTime Abre, DateTime Fecha, Periodo? Almoco)? Janela(int idFuncionario, DateTime dia)
        {
            var excecao = Excecoes.TryGetValue(dia, out var e) ? e : (bool?)null;
            if (excecao == false) return null;

            var barbearia = Grade.FirstOrDefault(h => h.Dia == dia.DayOfWeek);
            if (excecao == null && barbearia is not { Aberto: true }) return null;

            Jornadas.TryGetValue(idFuncionario, out var jornada);
            var j = jornada?.FirstOrDefault(d => d.Dia == dia.DayOfWeek);

            TimeSpan abre, fecha;
            Periodo? almoco = null;

            if (j == null)
            {
                // Sem jornada, o profissional fica o expediente inteiro.
                if (barbearia == null) return null;
                abre = barbearia.Abre;
                fecha = barbearia.Fecha;
            }
            else
            {
                if (!j.Trabalha) return null;
                abre = j.Entrada;
                fecha = j.Saida;
                if (j.TemAlmoco) almoco = new Periodo(dia + j.AlmocoInicio!.Value, dia + j.AlmocoFim!.Value);

                // Pela grade, ninguem atende fora do expediente da casa.
                if (excecao == null && barbearia != null)
                {
                    if (barbearia.Abre > abre) abre = barbearia.Abre;
                    if (barbearia.Fecha < fecha) fecha = barbearia.Fecha;
                }
            }

            return fecha > abre ? (dia + abre, dia + fecha, almoco) : null;
        }
    }
}
