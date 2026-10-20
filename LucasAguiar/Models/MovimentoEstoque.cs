namespace LucasAguiar.Models
{
    /// <summary>
    /// Por que o saldo mudou. O tipo é o que separa "vendi" de "usei no
    /// atendimento" de "contei e estava diferente" -- três coisas que
    /// antes viravam o mesmo número sem explicação.
    /// </summary>
    public static class TipoMovimentoEstoque
    {
        /// <summary>Saldo que já existia quando o registro passou a existir.</summary>
        public const string Abertura = "ABERTURA";

        /// <summary>Compra recebida.</summary>
        public const string Entrada = "ENTRADA";

        /// <summary>Produto vendido ao cliente.</summary>
        public const string Saida = "SAIDA";

        /// <summary>Venda cancelada: o produto voltou.</summary>
        public const string Devolucao = "DEVOLUCAO";

        /// <summary>Usado no atendimento, sem venda: pomada, lâmina, química.</summary>
        public const string Consumo = "CONSUMO";

        /// <summary>Quebrou, venceu ou sumiu.</summary>
        public const string Perda = "PERDA";

        /// <summary>Inventário: a contagem não bateu e o saldo foi corrigido.</summary>
        public const string Ajuste = "AJUSTE";

        public static readonly (string Valor, string Rotulo, bool Entra)[] Todos =
        {
            (Abertura, "Abertura", true),
            (Entrada, "Entrada", true),
            (Saida, "Venda", false),
            (Devolucao, "Devolução", true),
            (Consumo, "Consumo interno", false),
            (Perda, "Perda", false),
            (Ajuste, "Ajuste de inventário", true)
        };

        public static string Rotulo(string? tipo) =>
            Todos.FirstOrDefault(t => t.Valor == tipo).Rotulo ?? tipo ?? "";

        /// <summary>
        /// Os tipos que alguém lança à mão. Entrada e saída vêm da compra e
        /// da venda; abertura é do sistema.
        /// </summary>
        public static readonly string[] Manuais = { Consumo, Perda };
    }

    /// <summary>Uma linha do extrato de um produto.</summary>
    public class MovimentoEstoque
    {
        public int IdMovimento { get; set; }
        public int IdProduto { get; set; }
        public string NomeProduto { get; set; } = "";
        public DateTime DataHora { get; set; }
        public string Tipo { get; set; } = "";

        /// <summary>Com sinal: positivo entra, negativo sai.</summary>
        public int Quantidade { get; set; }

        /// <summary>O saldo que ficou depois deste movimento.</summary>
        public int SaldoApos { get; set; }

        public string? Motivo { get; set; }

        /// <summary>A venda ou a compra que originou, quando houver.</summary>
        public int? IdReferencia { get; set; }

        public string? Usuario { get; set; }

        public string RotuloTipo => TipoMovimentoEstoque.Rotulo(Tipo);

        public bool Entra => Quantidade > 0;

        /// <summary>"+3" ou "−2", já com o sinal certo para a tela.</summary>
        public string QuantidadeComSinal =>
            Quantidade >= 0 ? $"+{Quantidade}" : $"−{Math.Abs(Quantidade)}";
    }

    /// <summary>Uma linha da tela de estoque: o produto e a situação dele.</summary>
    public class SaldoEstoque
    {
        public int IdProduto { get; set; }
        public string Nome { get; set; } = "";
        public string? Marca { get; set; }
        public int Saldo { get; set; }
        public int Minimo { get; set; }
        public decimal Valor { get; set; }
        public DateTime? UltimoMovimento { get; set; }

        public decimal ValorEmEstoque => Valor * Math.Max(0, Saldo);

        /// <summary>
        /// Saldo negativo não é erro de digitação: é venda que saiu sem o
        /// produto estar no sistema. Aparece como estado próprio justamente
        /// para alguém ir conferir.
        /// </summary>
        public string Situacao =>
            Saldo < 0 ? "negativo"
            : Saldo == 0 ? "zerado"
            : Minimo > 0 && Saldo <= Minimo ? "baixo"
            : "ok";

        public string RotuloSituacao => Situacao switch
        {
            "negativo" => "Negativo",
            "zerado" => "Zerado",
            "baixo" => "Abaixo do mínimo",
            _ => "Em dia"
        };

        public bool PedeAtencao => Situacao != "ok";
    }
}
