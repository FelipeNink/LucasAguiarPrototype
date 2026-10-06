namespace LucasAguiar.Models
{
    public class Produto
    {
        public int IdProduto { get; set; }
        public string? NomeProduto { get; set; }
        public int Quantidade { get; set; }
        public float Valor { get; set; }
        public string? Descricao { get; set; }
        public string? Marca { get; set; }
        public int IdFornecedor { get; set; }

        // Nome do fornecedor via JOIN
        public string? NomeFornecedor { get; set; }
    }
}
