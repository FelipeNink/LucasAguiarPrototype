namespace LucasAguiar.Models
{
    public class Funcionario
    {
        public int IdFuncionario { get; set; }
        public string? NomeFuncionario { get; set; }
        public string? Telefone { get; set; }
        public string? CPF { get; set; }
        public DateTime? DataNascimento { get; set; }
        public string? CTPS { get; set; }
        public string? RG { get; set; }
        public string? Email { get; set; }
        public string? Estado { get; set; }
        public string? Cidade { get; set; }
        public string? Bairro { get; set; }
        public string? Rua { get; set; }
        public string? Numero { get; set; }

        /// <summary>Salário fixo mensal, somado às comissões na folha.</summary>
        public decimal SalarioFixo { get; set; }
    }
}
