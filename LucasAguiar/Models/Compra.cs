namespace LucasAguiar.Models
{
    /// <summary>
    /// Nota de compra: o cabeçalho fica em compras e os produtos em
    /// compra_item. item_comp, quantidade e id_prod_fk do cabeçalho viram
    /// um resumo dos itens, para listagem e exportação seguirem iguais.
    /// </summary>
    public class Compra
    {
        public int IdCompra { get; set; }
        public DateTime DataCompra { get; set; } = DateTime.Today;

        /// <summary>
        /// Era varchar no banco, o que impedia somar e ordenava "10" antes
        /// de "9". Agora é DECIMAL. Itens + frete − desconto.
        /// </summary>
        public decimal ValorCompra { get; set; }

        public string? ItemCompra { get; set; }
        public int Quantidade { get; set; } = 1;

        public int IdProduto { get; set; }
        public int IdFornecedor { get; set; }
        public int IdFuncionario { get; set; }

        /// <summary>Despesa gerada por esta compra.</summary>
        public int? IdDespesa { get; set; }

        public string? NumeroNota { get; set; }
        public decimal Frete { get; set; }
        public decimal Desconto { get; set; }
        public string? Observacao { get; set; }

        public string FormaPagamento { get; set; } = Models.FormaPagamento.Dinheiro;
        public int Parcelas { get; set; } = 1;
        public DateTime? VencimentoPrimeira { get; set; }

        /// <summary>Falso enquanto a mercadoria não chega: o estoque só entra no recebimento.</summary>
        public bool Recebida { get; set; } = true;
        public DateTime? DataRecebimento { get; set; }

        public List<CompraItem> Itens { get; set; } = new();

        // Preenchidos por join
        public string? NomeProduto { get; set; }
        public string? NomeFornecedor { get; set; }
        public string? NomeFuncionario { get; set; }

        /// <summary>Produtos da nota, para o filtro da listagem.</summary>
        public List<int> IdsProdutos { get; set; } = new();

        // Da despesa gerada, preenchidos so na listagem. Nulos sem despesa.
        public string? FormaPagamentoDespesa { get; set; }
        public bool? Pago { get; set; }

        public decimal ValorUnitario =>
            Quantidade > 0 ? Math.Round(ValorCompra / Quantidade, 2) : ValorCompra;

        public decimal TotalItens => Itens.Sum(i => i.Total);

        public decimal TotalNota => Math.Max(0, Math.Round(TotalItens + Frete - Desconto, 2));
    }

    public class CompraItem
    {
        public int IdItem { get; set; }

        /// <summary>Nulo para item sem cadastro (não mexe em estoque).</summary>
        public int? IdProduto { get; set; }

        public string Descricao { get; set; } = "";
        public int Quantidade { get; set; } = 1;
        public decimal CustoUnitario { get; set; }

        public decimal Total => Math.Round(CustoUnitario * Quantidade, 2);
    }
}
