namespace LucasAguiar.Models
{
    public static class CategoriaDespesa
    {
        public static readonly (string Valor, string Rotulo)[] Todas =
        {
            ("PRODUTOS", "Produtos e insumos"),
            ("ALUGUEL", "Aluguel"),
            ("SALARIO", "Salários e comissões"),
            ("ENERGIA", "Água, luz e internet"),
            ("MARKETING", "Marketing"),
            ("MANUTENCAO", "Manutenção e equipamentos"),
            ("IMPOSTOS", "Impostos e taxas"),
            ("OUTROS", "Outros")
        };

        public static string Rotulo(string? valor) =>
            Todas.FirstOrDefault(c => c.Valor == valor).Rotulo ?? (valor ?? "—");
    }

    public class Despesa
    {
        public int IdDespesa { get; set; }
        public DateTime DataDespesa { get; set; } = DateTime.Today;

        public string Descricao { get; set; } = "";
        public string Categoria { get; set; } = "OUTROS";
        public decimal Valor { get; set; }

        public string FormaPagamentoDespesa { get; set; } = FormaPagamento.Dinheiro;

        /// <summary>Despesa não paga não sai da gaveta nem entra no fechamento.</summary>
        public bool Pago { get; set; } = true;

        public int? IdFornecedor { get; set; }
        public int? IdCaixa { get; set; }
        public string? Observacao { get; set; }

        // Preenchido por join
        public string? NomeFornecedor { get; set; }

        public string RotuloCategoria => CategoriaDespesa.Rotulo(Categoria);

        /// <summary>Só dinheiro pago sai fisicamente da gaveta.</summary>
        public bool AfetaGaveta => Pago && FormaPagamentoDespesa == FormaPagamento.Dinheiro;
    }
}
