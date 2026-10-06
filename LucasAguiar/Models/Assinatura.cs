namespace LucasAguiar.Models
{
    public static class StatusAssinatura
    {
        public const string Ativa = "ATIVA";
        public const string Encerrada = "ENCERRADA";
    }

    /// <summary>
    /// Plano comprado por um cliente. O pagamento acontece uma vez, aqui;
    /// as visitas seguintes consomem crédito em vez de cobrar de novo.
    /// </summary>
    public class Assinatura
    {
        public int IdAssinatura { get; set; }
        public int IdCliente { get; set; }
        public int IdPlano { get; set; }

        public DateTime DataInicio { get; set; } = DateTime.Today;
        public DateTime? DataFim { get; set; }

        public decimal ValorPago { get; set; }
        public int? IdVenda { get; set; }
        public string Status { get; set; } = StatusAssinatura.Ativa;

        public List<AssinaturaSaldo> Saldos { get; set; } = new();

        // Preenchidos por join
        public string? NomePlano { get; set; }
        public string? NomeCliente { get; set; }

        public bool Vencida => DataFim.HasValue && DataFim.Value < DateTime.Today;

        public bool Utilizavel =>
            Status == StatusAssinatura.Ativa && !Vencida && Saldos.Any(s => s.Restante > 0);

        public int TotalRestante => Saldos.Sum(s => s.Restante);

        public string ValidadeTexto =>
            DataFim.HasValue
                ? (Vencida ? $"vencida em {DataFim.Value:dd/MM/yyyy}" : $"válida até {DataFim.Value:dd/MM/yyyy}")
                : "sem prazo";
    }

    /// <summary>Crédito de um item dentro da assinatura.</summary>
    public class AssinaturaSaldo
    {
        public int IdSaldo { get; set; }
        public int IdAssinatura { get; set; }

        public string TipoItem { get; set; } = Models.TipoItem.Servico;
        public int IdReferencia { get; set; }
        public string Descricao { get; set; } = "";

        /// <summary>Preço avulso na época da compra, guardado para relatório.</summary>
        public decimal ValorReferencia { get; set; }

        public int QuantidadeTotal { get; set; }
        public int QuantidadeUsada { get; set; }

        public int Restante => Math.Max(0, QuantidadeTotal - QuantidadeUsada);

        public string RotuloTipo => TipoItem == Models.TipoItem.Produto ? "Produto" : "Serviço";
    }
}
