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

        /// <summary>Soma das quantidades de serviços. Preenchido na listagem.</summary>
        public int TotalServicos { get; set; }

        /// <summary>Assinaturas ativas e dentro da validade. Preenchido na listagem.</summary>
        public int AssinantesAtivos { get; set; }
    }
}
