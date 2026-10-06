namespace LucasAguiar.Models
{
    public class Usuario
    {
        public int IdUsr { get; set; }
        public string? NomeUsr { get; set; }
        public string? EmailUsr { get; set; }
        public string? SenhaUsr { get; set; }
        public bool AtivoUsr { get; set; }
        public DateTime DataCriacaoUsr { get; set; }
    }
}
