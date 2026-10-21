namespace LucasAguiar.Models
{
    /// <summary>
    /// Jornada de um profissional num dia da semana. Sem trabalhar, os
    /// horarios nao valem. O almoco e opcional: os dois ou nenhum.
    /// </summary>
    public class JornadaDia
    {
        public DayOfWeek Dia { get; set; }
        public bool Trabalha { get; set; }
        public TimeSpan Entrada { get; set; } = new(9, 0, 0);
        public TimeSpan Saida { get; set; } = new(19, 0, 0);
        public TimeSpan? AlmocoInicio { get; set; }
        public TimeSpan? AlmocoFim { get; set; }

        public bool TemAlmoco => AlmocoInicio != null && AlmocoFim != null;
    }

    /// <summary>Folga, ferias ou "saio as 15h hoje": o profissional nao atende no periodo.</summary>
    public class BloqueioFuncionario
    {
        public int IdBloqueio { get; set; }
        public int IdFuncionario { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fim { get; set; }
        public string? Motivo { get; set; }

        public bool Cruza(DateTime inicio, DateTime fim) => Inicio < fim && Fim > inicio;

        /// <summary>"15:00–18:00" no mesmo dia; "20/10 09:00 a 25/10 19:00" se atravessa dias.</summary>
        public string Periodo => Inicio.Date == Fim.Date
            ? $"{Inicio:dd/MM} {Inicio:HH:mm}–{Fim:HH:mm}"
            : $"{Inicio:dd/MM HH:mm} a {Fim:dd/MM HH:mm}";
    }

    /// <summary>
    /// Regras da disponibilidade do profissional, sem banco: jornada da
    /// semana, almoco e bloqueios. So avisa, como o expediente da barbearia.
    /// </summary>
    public static class Disponibilidade
    {
        private static readonly string[] NosDias =
            { "aos domingos", "às segundas", "às terças", "às quartas", "às quintas", "às sextas", "aos sábados" };

        /// <summary>Jornada inicial de quem ainda nao tem uma: a da barbearia, sem almoco.</summary>
        public static List<JornadaDia> CopiarDaBarbearia(IEnumerable<HorarioDia> grade) =>
            Enum.GetValues<DayOfWeek>()
                .Select(d => grade.FirstOrDefault(h => h.Dia == d) is { } h
                    ? new JornadaDia { Dia = d, Trabalha = h.Aberto, Entrada = h.Abre, Saida = h.Fecha }
                    : new JornadaDia { Dia = d })
                .ToList();

        /// <summary>Primeiro problema da jornada, ou null se estiver valida.</summary>
        public static string? ErroJornada(IEnumerable<JornadaDia> jornada)
        {
            foreach (var d in jornada.Where(d => d.Trabalha))
            {
                var dia = Expediente.NomeDia[(int)d.Dia];

                if (d.Saida <= d.Entrada)
                    return $"{dia}: a saída deve ser depois da entrada.";

                if ((d.AlmocoInicio == null) != (d.AlmocoFim == null))
                    return $"{dia}: informe o início e o fim do almoço, ou nenhum dos dois.";

                if (d.TemAlmoco && (d.AlmocoFim <= d.AlmocoInicio
                                    || d.AlmocoInicio < d.Entrada || d.AlmocoFim > d.Saida))
                    return $"{dia}: o almoço deve ficar dentro da jornada.";
            }

            return null;
        }

        /// <param name="jornada">Null quando o profissional ainda nao tem jornada: so os bloqueios valem.</param>
        public static string? AvisoAgendamento(
            IReadOnlyList<JornadaDia>? jornada, IEnumerable<BloqueioFuncionario> bloqueios,
            DateTime inicio, DateTime fim, string nome)
        {
            if (bloqueios.FirstOrDefault(b => b.Cruza(inicio, fim)) is { } b)
                return $"{nome} está indisponível em {b.Periodo}"
                       + (string.IsNullOrWhiteSpace(b.Motivo) ? "." : $" ({b.Motivo}).");

            var dia = jornada?.FirstOrDefault(j => j.Dia == inicio.DayOfWeek);
            if (dia == null) return null;

            if (!dia.Trabalha)
                return $"{nome} não trabalha {NosDias[(int)inicio.DayOfWeek]}.";

            if (inicio.TimeOfDay < dia.Entrada || fim > inicio.Date + dia.Saida)
                return $"{nome} trabalha das {Expediente.Hora(dia.Entrada)} às {Expediente.Hora(dia.Saida)} nesse dia.";

            if (dia.TemAlmoco
                && inicio < inicio.Date + dia.AlmocoFim!.Value
                && fim > inicio.Date + dia.AlmocoInicio!.Value)
                return $"O horário cai no almoço de {nome} "
                       + $"({Expediente.Hora(dia.AlmocoInicio.Value)}–{Expediente.Hora(dia.AlmocoFim.Value)}).";

            return null;
        }
    }
}
