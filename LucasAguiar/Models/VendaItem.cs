namespace LucasAguiar.Models
{
    /// <summary>Tipos de item que podem entrar numa venda.</summary>
    public static class TipoItem
    {
        public const string Servico = "SERVICO";
        public const string Produto = "PRODUTO";
        public const string Plano = "PLANO";
    }

    public class VendaItem
    {
        public int IdVendaItem { get; set; }
        public int IdVenda { get; set; }

        /// <summary>SERVICO, PRODUTO ou PLANO.</summary>
        public string TipoItem { get; set; } = Models.TipoItem.Servico;

        /// <summary>Id do servico, produto ou plano de origem.</summary>
        public int? IdReferencia { get; set; }

        public string Descricao { get; set; } = "";
        public int Quantidade { get; set; } = 1;
        public decimal ValorUnitario { get; set; }

        /// <summary>
        /// Comissão unitária congelada no momento da venda. Preço e comissão
        /// de um serviço mudam com o tempo; recalcular depois daria outro valor.
        /// </summary>
        public decimal ComissaoUnitaria { get; set; }

        /// <summary>
        /// Item pago pelo plano: entra na venda por R$ 0,00 e abate crédito.
        /// A comissão continua valendo — o profissional executou o serviço.
        /// </summary>
        public bool CobertoPlano { get; set; }

        /// <summary>Crédito da assinatura que este item consome.</summary>
        public int? IdSaldo { get; set; }

        /// <summary>Preço avulso, exibido riscado quando o plano cobre.</summary>
        public decimal ValorReferencia { get; set; }

        public decimal ValorTotal => Math.Round(ValorUnitario * Quantidade, 2);

        public decimal ComissaoTotal => Math.Round(ComissaoUnitaria * Quantidade, 2);

        public string RotuloTipo => TipoItem switch
        {
            Models.TipoItem.Servico => "Serviço",
            Models.TipoItem.Produto => "Produto",
            Models.TipoItem.Plano => "Plano",
            _ => TipoItem
        };
    }
}
