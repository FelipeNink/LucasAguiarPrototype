namespace LucasAguiar.Models
{
    public class Compra
    {
        public int IdCompra { get; set; }
        public DateTime DataCompra { get; set; } = DateTime.Today;

        /// <summary>
        /// Era varchar no banco, o que impedia somar e ordenava "10" antes
        /// de "9". Agora é DECIMAL.
        /// </summary>
        public decimal ValorCompra { get; set; }

        public string? ItemCompra { get; set; }
        public int Quantidade { get; set; } = 1;

        public int IdProduto { get; set; }
        public int IdFornecedor { get; set; }
        public int IdFuncionario { get; set; }

        /// <summary>Despesa gerada por esta compra.</summary>
        public int? IdDespesa { get; set; }

        // Preenchidos por join
        public string? NomeProduto { get; set; }
        public string? NomeFornecedor { get; set; }
        public string? NomeFuncionario { get; set; }

        public decimal ValorUnitario =>
            Quantidade > 0 ? Math.Round(ValorCompra / Quantidade, 2) : ValorCompra;
    }
}
