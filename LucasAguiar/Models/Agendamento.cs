namespace LucasAguiar.Models
{
    public static class StatusAgendamento
    {
        public const string Agendado = "AGENDADO";
        public const string Concluido = "CONCLUIDO";
        public const string Cancelado = "CANCELADO";

        public static readonly (string Valor, string Rotulo)[] Todos =
        {
            (Agendado, "Agendado"),
            (Concluido, "Concluído"),
            (Cancelado, "Cancelado")
        };

        public static string Rotulo(string? status) =>
            Todos.FirstOrDefault(s => s.Valor == status).Rotulo ?? status ?? "";
    }

    public class Agendamento
    {
        public int IdAgendamento { get; set; }

        public DateTime DataHora { get; set; }

        /// <summary>Copiada do serviço ao agendar; pode ser ajustada.</summary>
        public int DuracaoMin { get; set; } = 30;

        public int IdCliente { get; set; }
        public int IdFuncionario { get; set; }

        /// <summary>Primeiro serviço da lista; é o que agendamento.id_serv_fk guarda.</summary>
        public int IdServico { get; set; }

        /// <summary>Serviços do horário, na ordem em que serão feitos.</summary>
        public List<int> IdsServicos { get; set; } = new();

        public string? Observacao { get; set; }
        public string Status { get; set; } = StatusAgendamento.Agendado;

        /// <summary>Venda gerada a partir deste agendamento.</summary>
        public int? IdVenda { get; set; }

        // Preenchidos por join, para exibicao
        public string? NomeCliente { get; set; }
        public string? TelefoneCliente { get; set; }
        public string? NomeFuncionario { get; set; }

        /// <summary>Nomes de todos os serviços, "Corte + Barba".</summary>
        public string? NomeServico { get; set; }

        /// <summary>Soma dos serviços, já com o preço próprio do profissional quando houver.</summary>
        public decimal PrecoServico { get; set; }

        public DateTime Fim => DataHora.AddMinutes(DuracaoMin);

        public bool Aberto => Status == StatusAgendamento.Agendado;
    }
}
