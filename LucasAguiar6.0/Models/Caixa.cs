namespace LucasAguiar.Models
{
    public static class StatusCaixa
    {
        public const string Aberto = "ABERTO";
        public const string Fechado = "FECHADO";
    }

    public static class TipoMovimento
    {
        public const string Sangria = "SANGRIA";
        public const string Suprimento = "SUPRIMENTO";
    }

    public class MovimentoCaixa
    {
        public int IdMovimento { get; set; }
        public int IdCaixa { get; set; }
        public string Tipo { get; set; } = TipoMovimento.Sangria;
        public decimal Valor { get; set; }
        public string? Descricao { get; set; }
        public DateTime DataHora { get; set; } = DateTime.Now;
        public string? Usuario { get; set; }

        public bool ERetirada => Tipo == TipoMovimento.Sangria;
        public string RotuloTipo => ERetirada ? "Sangria" : "Suprimento";
    }

    /// <summary>
    /// Sessão de caixa. Só movimento em dinheiro afeta a gaveta — cartão e PIX
    /// entram no faturamento mas não mudam o que está fisicamente lá dentro.
    /// </summary>
    public class Caixa
    {
        public int IdCaixa { get; set; }
        public DateTime DataAbertura { get; set; } = DateTime.Now;
        public DateTime? DataFechamento { get; set; }

        public decimal ValorAbertura { get; set; }
        public decimal? ValorInformado { get; set; }
        public decimal? ValorCalculado { get; set; }
        public decimal? Diferenca { get; set; }

        public string Status { get; set; } = StatusCaixa.Aberto;
        public string? UsuarioAbertura { get; set; }
        public string? UsuarioFechamento { get; set; }
        public string? Observacao { get; set; }

        public List<MovimentoCaixa> Movimentos { get; set; } = new();

        // Totais do periodo, calculados pelo DAO
        public ResumoCaixa Resumo { get; set; } = new();

        public bool EstaAberto => Status == StatusCaixa.Aberto;

        public string RotuloDiferenca
        {
            get
            {
                if (!Diferenca.HasValue) return "—";
                if (Diferenca.Value == 0) return "Confere";
                return Diferenca.Value > 0
                    ? $"Sobra de {Diferenca.Value:C}"
                    : $"Falta de {Math.Abs(Diferenca.Value):C}";
            }
        }
    }

    /// <summary>Números apurados de uma sessão de caixa.</summary>
    public class ResumoCaixa
    {
        public decimal VendasDinheiro { get; set; }
        public decimal VendasPix { get; set; }
        public decimal VendasDebito { get; set; }
        public decimal VendasCredito { get; set; }

        public decimal DespesasDinheiro { get; set; }
        public decimal DespesasOutras { get; set; }

        public decimal Sangrias { get; set; }
        public decimal Suprimentos { get; set; }

        public decimal Comissoes { get; set; }
        public int QuantidadeVendas { get; set; }

        public decimal FaturamentoTotal =>
            VendasDinheiro + VendasPix + VendasDebito + VendasCredito;

        public decimal DespesasTotal => DespesasDinheiro + DespesasOutras;

        /// <summary>
        /// Quanto deve haver em dinheiro na gaveta. Cartão e PIX ficam de fora
        /// de propósito: não passam pela gaveta.
        /// </summary>
        public decimal SaldoEsperadoGaveta(decimal valorAbertura) =>
            valorAbertura + VendasDinheiro + Suprimentos - Sangrias - DespesasDinheiro;

        public decimal ResultadoPeriodo => FaturamentoTotal - DespesasTotal;
    }
}
