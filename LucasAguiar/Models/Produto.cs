namespace LucasAguiar.Models
{
    public class Produto
    {
        public int IdProduto { get; set; }
        public string? NomeProduto { get; set; }
        public int Quantidade { get; set; }
        public float Valor { get; set; }
        public string? Descricao { get; set; }
        public string? Marca { get; set; }
        public int IdFornecedor { get; set; }

        public int? IdCategoria { get; set; }
        public string? CodigoBarras { get; set; }
        public string? Sku { get; set; }
        public string Unidade { get; set; } = "UN";
        public decimal? PrecoCusto { get; set; }

        /// <summary>% de comissão; nulo usa o % padrão do profissional.</summary>
        public decimal? Comissao { get; set; }

        public bool Ativo { get; set; } = true;

        // Nome do fornecedor via JOIN
        public string? NomeFornecedor { get; set; }
        public string? NomeCategoria { get; set; }

        public static readonly string[] Unidades = { "UN", "CX", "PCT", "KG", "G", "L", "ML" };
    }
}
