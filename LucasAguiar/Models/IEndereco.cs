namespace LucasAguiar.Models
{
    /// <summary>
    /// Endereco com CEP, para o mesmo bloco de campos (CamposEndereco)
    /// servir a cliente e funcionario. Estado guarda a UF.
    /// </summary>
    public interface IEndereco
    {
        string? Cep { get; set; }
        string? Rua { get; set; }
        string? Numero { get; set; }
        string? Bairro { get; set; }
        string? Cidade { get; set; }
        string? Estado { get; set; }
    }
}
