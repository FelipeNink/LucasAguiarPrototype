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

        /// <summary>
        /// Data que os relatorios usam: a do pagamento na despesa paga e o
        /// vencimento na despesa em aberto. O DAO a calcula ao gravar.
        /// </summary>
        public DateTime DataDespesa { get; set; } = DateTime.Today;

        public DateTime? DataVencimento { get; set; } = DateTime.Today;
        public DateTime? DataPagamento { get; set; } = DateTime.Today;
        public int Parcelas { get; set; } = 1;
        public bool Recorrente { get; set; }

        /// <summary>Funcionário ligado à despesa (vale, adiantamento, salário).</summary>
        public int? IdFuncionario { get; set; }

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
        public string? NomeFuncionario { get; set; }
        public string? NomeCategoria { get; set; }
        public int QuantidadeAnexos { get; set; }

        /// <summary>O nome vem de categoria_despesa; a lista fixa cobre o que vier sem join.</summary>
        public string RotuloCategoria => NomeCategoria ?? CategoriaDespesa.Rotulo(Categoria);

        /// <summary>Só dinheiro pago sai fisicamente da gaveta.</summary>
        public bool AfetaGaveta => Pago && FormaPagamentoDespesa == FormaPagamento.Dinheiro;
    }

    public class DespesaAnexo
    {
        public int IdAnexo { get; set; }
        public string Nome { get; set; } = "";
        public string Tipo { get; set; } = "";
        public int Tamanho { get; set; }
        public byte[]? Conteudo { get; set; }
    }
}
