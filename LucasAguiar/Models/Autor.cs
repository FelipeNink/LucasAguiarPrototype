namespace LucasAguiar.Models
{
    /// <summary>
    /// Quem esta executando a acao. Vai junto em toda operacao da Equipe:
    /// e com ele que o servidor decide o que a pessoa pode fazer (so a
    /// raiz mexe em perfil, ninguem se exclui) e e dele que sai o "quem"
    /// gravado na auditoria.
    ///
    /// Passado como parametro em vez de lido de dentro do DAO: assim a
    /// regra fica testavel sem subir uma requisicao HTTP inteira.
    /// </summary>
    public record Autor(int Id, string Nome, bool EhAdmin, bool EhRaiz, string? Ip = null)
    {
        public static Autor De(Acesso acesso, string? ip = null) =>
            new(acesso.IdUsuario, acesso.Nome, acesso.EhAdmin, acesso.EhRaiz, ip);

        /// <summary>
        /// Autor do proprio sistema, para operacoes sem ninguem logado
        /// (migracao, assistente de instalacao).
        /// </summary>
        public static Autor Sistema { get; } = new(0, "Sistema", true, true);
    }
}
