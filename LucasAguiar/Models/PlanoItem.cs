namespace LucasAguiar.Models
{
    /// <summary>Serviço ou produto que compõe um plano.</summary>
    public class PlanoItem
    {
        public int IdPlanoItem { get; set; }
        public int IdPlano { get; set; }

        /// <summary>SERVICO ou PRODUTO.</summary>
        public string TipoItem { get; set; } = Models.TipoItem.Servico;

        public int IdReferencia { get; set; }

        /// <summary>Nulo é ilimitado.</summary>
        public int? Quantidade { get; set; } = 1;

        public bool Ilimitado => !Quantidade.HasValue;

        // Preenchidos por join, para exibicao
        public string? Descricao { get; set; }
        public decimal ValorUnitario { get; set; }

        public string RotuloTipo => TipoItem == Models.TipoItem.Produto ? "Produto" : "Serviço";
    }
}
