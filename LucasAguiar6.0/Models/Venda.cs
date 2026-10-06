namespace LucasAguiar.Models
{
    /// <summary>Formas de pagamento aceitas. Confirmação é manual nesta fase.</summary>
    public static class FormaPagamento
    {
        public const string Dinheiro = "DINHEIRO";
        public const string Pix = "PIX";
        public const string Debito = "DEBITO";
        public const string Credito = "CREDITO";

        /// <summary>
        /// Atendimento quitado com crédito de plano. Fica fora de `Todas`
        /// porque não é algo a escolher no caixa: o dinheiro entrou quando o
        /// plano foi vendido, e esta venda não movimenta valor nenhum.
        /// </summary>
        public const string Plano = "PLANO";

        public static readonly (string Valor, string Rotulo)[] Todas =
        {
            (Dinheiro, "Dinheiro"),
            (Pix, "PIX"),
            (Debito, "Cartão de débito"),
            (Credito, "Cartão de crédito")
        };

        public static string Rotulo(string? valor) =>
            valor == Plano
                ? "Crédito do plano"
                : Todas.FirstOrDefault(f => f.Valor == valor).Rotulo ?? (valor ?? "—");
    }

    public static class StatusVenda
    {
        public const string Confirmada = "CONFIRMADA";
        public const string Cancelada = "CANCELADA";
    }

    public class Venda
    {
        public int IdVenda { get; set; }
        public DateTime DataVenda { get; set; } = DateTime.Now;

        public int? IdCliente { get; set; }
        public int? IdFuncionario { get; set; }

        /// <summary>Sessão de caixa em que a venda aconteceu.</summary>
        public int? IdCaixa { get; set; }

        public string? FormaPagamentoVenda { get; set; }
        public int? QuantidadeParcelas { get; set; }
        public decimal Desconto { get; set; }
        public string? Descricao { get; set; }
        public string Status { get; set; } = StatusVenda.Confirmada;

        /// <summary>Total gravado no banco (já com desconto aplicado).</summary>
        public decimal ValorVenda { get; set; }

        public List<VendaItem> Itens { get; set; } = new();

        /// <summary>
        /// Preenchido quando esta venda comercializa um plano: ao confirmar,
        /// cria a assinatura com os créditos correspondentes.
        /// </summary>
        public PlanoParaAssinar? PlanoVendido { get; set; }

        public class PlanoParaAssinar
        {
            public int IdPlano { get; set; }
            public decimal ValorPago { get; set; }
            public List<PlanoItem> Componentes { get; set; } = new();
        }

        // Preenchidos por join, para exibicao
        public string? NomeCliente { get; set; }
        public string? NomeFuncionario { get; set; }

        public decimal Subtotal => Itens.Sum(i => i.ValorTotal);

        /// <summary>
        /// Comissão somada no banco. Preenchida na listagem, onde os itens
        /// não são carregados; no detalhe vale o cálculo sobre os itens.
        /// </summary>
        public decimal ComissaoGravada { get; set; }

        /// <summary>Comissão total do profissional nesta venda.</summary>
        public decimal Comissao => Itens.Count > 0 ? Itens.Sum(i => i.ComissaoTotal) : ComissaoGravada;

        /// <summary>Total calculado a partir dos itens, nunca negativo.</summary>
        public decimal Total => Math.Max(0, Math.Round(Subtotal - Desconto, 2));
    }
}
