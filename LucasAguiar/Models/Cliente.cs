
namespace LucasAguiar.Models

{
    public class Cliente : IEndereco
    {
        public int IdCliente{ get; set; }
        public string? NomeCliente { get; set; }
        public string? Telefone { get; set; }
        public string? CPF { get; set; }
        public DateTime? DataNascimento { get; set; }
        public string? RG { get; set; }
        public string? Cep { get; set; }
        public string? Estado { get; set; }
        public string? Cidade { get; set; }
        public string? Bairro { get; set; }
        public string? Rua { get; set; }
        public string? Numero { get; set; }

        public int? IdPlano { get; set; }

        // Preenchidos por join, para exibicao
        public string? NomePlano { get; set; }
        public decimal ValorPlano { get; set; }

        // Historico de vendas, preenchido so por ClienteDAO.ListarComHistorico
        public DateTime? UltimaVisita { get; set; }
        public int Visitas { get; set; }
        public decimal TotalGasto { get; set; }

        /// <summary>Quem mais atendeu o cliente, ate existir o campo no cadastro.</summary>
        public int? IdProfissionalPreferido { get; set; }
        public string? NomeProfissionalPreferido { get; set; }

        public bool TemPlano => IdPlano.HasValue && IdPlano.Value > 0;

        /// <summary>Nome com um complemento para diferenciar homônimos na busca.</summary>
        public string Identificacao =>
            string.IsNullOrWhiteSpace(Telefone)
                ? (NomeCliente ?? "(sem nome)")
                : $"{NomeCliente} — {Telefone}";
    }
}


