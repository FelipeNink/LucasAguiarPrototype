namespace LucasAguiar.Models
{
    public class Fornecedor
        {
            public int IdFornecedor { get; set; }

            public string? NomeFornecedor { get; set; }

            /// <summary>CPF ou CNPJ, com pontuacao.</summary>
            public string? Documento { get; set; }

            public string? Email { get; set; }

            public string? Telefone { get; set; }

            public string? TipoProd { get; set; }

            /// <summary>Data da compra mais recente. Preenchido so na listagem.</summary>
            public DateTime? UltimaCompra { get; set; }
        }
}
