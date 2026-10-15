namespace LucasAguiar.Components.Shared
{
    /// <summary>
    /// Par valor/rotulo para o Seletor quando a origem nao e uma entidade
    /// do dominio -- listas fixas, constantes de tipo, filtros com "Todos".
    /// </summary>
    public record Opcao(string Valor, string Rotulo, string? Cor = null, string? Contagem = null);

    /// <summary>O mesmo, para quando o valor e o id numerico de um registro.</summary>
    public record OpcaoId(int Id, string Rotulo, string? Cor = null, string? Contagem = null);
}
