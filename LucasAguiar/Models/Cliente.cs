
namespace LucasAguiar.Models

{
    public class Cliente
    {
        public int IdCliente{ get; set; }
        public string? NomeCliente { get; set; }
        public string? Telefone { get; set; }
        public string? CPF { get; set; }
        public DateTime? DataNascimento { get; set; }
        public string? RG { get; set; }
        public string? Estado { get; set; }
        public string? Cidade { get; set; }
        public string? Bairro { get; set; }
        public string? Rua { get; set; }
        public string? Numero { get; set; }

        public int? IdPlano { get; set; }

        // Preenchidos por join, para exibicao
        public string? NomePlano { get; set; }
        public decimal ValorPlano { get; set; }

        public bool TemPlano => IdPlano.HasValue && IdPlano.Value > 0;

        /// <summary>Nome com um complemento para diferenciar homônimos na busca.</summary>
        public string Identificacao =>
            string.IsNullOrWhiteSpace(Telefone)
                ? (NomeCliente ?? "(sem nome)")
                : $"{NomeCliente} — {Telefone}";
    }
}


