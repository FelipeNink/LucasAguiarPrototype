namespace LucasAguiar.Models
{
    /// <summary>Nota de 1 a 5 que o cliente da ao atendimento, pelo link /avaliar/&lt;token&gt;.</summary>
    public static class NotaAvaliacao
    {
        public const int Minima = 1;
        public const int Maxima = 5;

        /// <summary>Ate quando o link vale, contado da venda.</summary>
        public const int PrazoDias = 30;

        /// <summary>Nota que acende o aviso no painel.</summary>
        public const int Baixa = 3;

        public const int TamanhoComentario = 500;

        /// <summary>Indexado pela nota (0 fica vazio).</summary>
        public static readonly string[] Rotulos = { "", "Péssimo", "Ruim", "Regular", "Bom", "Excelente" };

        public static bool Valida(int nota) => nota is >= Minima and <= Maxima;

        public static string Estrelas(int nota) => new string('★', nota) + new string('☆', Maxima - nota);
    }

    /// <summary>Uma avaliacao, com o que a pagina publica e os relatorios mostram.</summary>
    public class Avaliacao
    {
        public int IdAvaliacao { get; set; }
        public int IdVenda { get; set; }
        public string Token { get; set; } = "";
        public int? Nota { get; set; }
        public string? Comentario { get; set; }
        public DateTime? RespondidoEm { get; set; }

        public DateTime DataVenda { get; set; }
        public bool VendaCancelada { get; set; }
        public string? NomeCliente { get; set; }
        public string? NomeFuncionario { get; set; }
        public string? Servicos { get; set; }

        public bool Respondida => Nota.HasValue;

        public bool Vencida(DateTime agora) => DataVenda.AddDays(NotaAvaliacao.PrazoDias) < agora;
    }

    /// <summary>Notas de um profissional no periodo.</summary>
    public class LinhaAvaliacaoProfissional
    {
        public int? IdFuncionario { get; set; }
        public string Nome { get; set; } = "";

        /// <summary>Quantas de cada nota; indice = nota (0 fica vazio).</summary>
        public int[] PorNota { get; set; } = new int[NotaAvaliacao.Maxima + 1];

        public int Quantidade => PorNota.Sum();

        public decimal Media => Quantidade == 0
            ? 0
            : Math.Round((decimal)Enumerable.Range(1, NotaAvaliacao.Maxima).Sum(n => n * PorNota[n]) / Quantidade, 1);
    }
}
