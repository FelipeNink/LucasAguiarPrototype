namespace LucasAguiar.Models
{
    public class Plano

    {
        public int IdPlano { get; set; }

        public string? NomePlano { get; set; }

        public string? Descricao { get; set; }

       
        public float? Valor { get; set; }

        /// <summary>Soma das quantidades dos componentes. Preenchido na listagem.</summary>
        public int TotalItens { get; set; }
    }
}
