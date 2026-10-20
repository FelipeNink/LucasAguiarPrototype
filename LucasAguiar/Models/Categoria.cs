namespace LucasAguiar.Models
{
    /// <summary>Cada tipo tem a sua tabela: categoria_servico, _produto e _despesa.</summary>
    public enum TipoCategoria { Servico, Produto, Despesa }

    public class Categoria
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";

        /// <summary>So despesa: e o que fica gravado em despesa.categoria.</summary>
        public string? Codigo { get; set; }

        public bool Ativo { get; set; } = true;
    }
}
