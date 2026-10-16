namespace LucasAguiar.Models
{
    /// <summary>
    /// Quem esta usando o sistema agora, resolvido a partir do token da
    /// sessao. E o que o middleware, as guardas de pagina e o servico de
    /// autorizacao recebem.
    /// </summary>
    public record Acesso(Usuario Usuario, int IdSessao, DateTime ExpiraEm)
    {
        public int IdUsuario => Usuario.IdUsr;
        public string Nome => Usuario.NomeUsr ?? "";
        public bool EhAdmin => Usuario.EhAdmin;
        public bool EhRaiz => Usuario.RaizUsr;
        public bool PrecisaTrocarSenha => Usuario.TrocarSenhaUsr;
    }
}
