namespace LucasAguiar.Models
{
    public class Fornecedor : IEndereco
        {
            public int IdFornecedor { get; set; }

            /// <summary>Nome (PF) ou razão social (PJ).</summary>
            public string? NomeFornecedor { get; set; }

            /// <summary>PF ou PJ.</summary>
            public string TipoPessoa { get; set; } = "PJ";

            public string? NomeFantasia { get; set; }

            /// <summary>Pessoa de contato no fornecedor.</summary>
            public string? Contato { get; set; }

            public string? Cep { get; set; }
            public string? Rua { get; set; }
            public string? Numero { get; set; }
            public string? Bairro { get; set; }
            public string? Cidade { get; set; }
            public string? Estado { get; set; }

            public string? Observacoes { get; set; }
            public bool Ativo { get; set; } = true;

            /// <summary>CPF ou CNPJ, com pontuacao.</summary>
            public string? Documento { get; set; }

            public string? Email { get; set; }

            public string? Telefone { get; set; }

            public string? TipoProd { get; set; }

            /// <summary>Data da compra mais recente. Preenchido so na listagem.</summary>
            public DateTime? UltimaCompra { get; set; }
        }
}
