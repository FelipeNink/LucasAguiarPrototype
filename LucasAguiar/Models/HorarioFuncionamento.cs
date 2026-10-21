namespace LucasAguiar.Models
{
    /// <summary>Expediente de um dia da semana. Fechado, os horarios nao valem.</summary>
    public class HorarioDia
    {
        public DayOfWeek Dia { get; set; }
        public bool Aberto { get; set; }
        public TimeSpan Abre { get; set; } = new(9, 0, 0);
        public TimeSpan Fecha { get; set; } = new(19, 0, 0);
    }

    /// <summary>Aberta ou fechada agora, com o texto curto do painel.</summary>
    public record SituacaoExpediente(bool Aberta, string Texto, bool Manual);

    /// <summary>
    /// Regras do horario de funcionamento, sem banco: a grade semanal manda,
    /// a menos que alguem tenha aberto ou fechado na mao naquela data.
    /// </summary>
    public static class Expediente
    {
        /// <summary>Indexado pelo DayOfWeek (0 = domingo).</summary>
        public static readonly string[] NomeDia =
            { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };

        private static readonly string[] NosDias =
            { "aos domingos", "às segundas", "às terças", "às quartas", "às quintas", "às sextas", "aos sábados" };

        private static readonly string[] Abreviado =
            { "dom.", "seg.", "ter.", "qua.", "qui.", "sex.", "sáb." };

        /// <summary>Segunda primeiro, como a semana de uma barbearia e lida.</summary>
        public static readonly DayOfWeek[] OrdemDaSemana =
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        public static string Hora(TimeSpan hora) => hora.ToString(@"hh\:mm");

        private static HorarioDia? Dia(IEnumerable<HorarioDia> grade, DayOfWeek dia) =>
            grade.FirstOrDefault(h => h.Dia == dia);

        /// <param name="excecaoHoje">true/false quando abriram ou fecharam na mao hoje.</param>
        public static SituacaoExpediente Situacao(
            IReadOnlyList<HorarioDia> grade, bool? excecaoHoje, DateTime agora)
        {
            if (excecaoHoje == true) return new(true, "Aberta · ajuste manual", true);
            if (excecaoHoje == false) return new(false, "Fechada hoje · ajuste manual", true);

            var hoje = Dia(grade, agora.DayOfWeek);
            var hora = agora.TimeOfDay;

            if (hoje is { Aberto: true } && hora >= hoje.Abre && hora < hoje.Fecha)
                return new(true, $"Aberta · fecha às {Hora(hoje.Fecha)}", false);

            if (hoje is { Aberto: true } && hora < hoje.Abre)
                return new(false, $"Fechada · abre às {Hora(hoje.Abre)}", false);

            for (var i = 1; i <= 7; i++)
            {
                var data = agora.Date.AddDays(i);
                var dia = Dia(grade, data.DayOfWeek);
                if (dia is not { Aberto: true }) continue;

                var quando = i == 1 ? "amanhã" : Abreviado[(int)data.DayOfWeek];
                return new(false, $"Fechada · abre {quando} às {Hora(dia.Abre)}", false);
            }

            return new(false, "Fechada", false);
        }

        /// <summary>
        /// Aviso para um agendamento fora do expediente, ou null se estiver
        /// dentro. So avisa: encaixe fora do horario acontece.
        /// </summary>
        public static string? AvisoAgendamento(
            IReadOnlyList<HorarioDia> grade, bool? excecaoNoDia, DateTime inicio, DateTime fim)
        {
            if (excecaoNoDia == true) return null;
            if (excecaoNoDia == false) return "Fora do expediente: a barbearia foi marcada como fechada neste dia.";

            var dia = Dia(grade, inicio.DayOfWeek);
            if (dia == null) return null;

            if (!dia.Aberto)
                return $"Fora do expediente: a barbearia não abre {NosDias[(int)inicio.DayOfWeek]}.";

            if (inicio.TimeOfDay < dia.Abre || fim > inicio.Date + dia.Fecha)
                return $"Fora do expediente: nesse dia a barbearia funciona das {Hora(dia.Abre)} às {Hora(dia.Fecha)}.";

            return null;
        }
    }
}
