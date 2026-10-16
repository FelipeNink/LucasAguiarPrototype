namespace LucasAguiar.Models
{
    public class Usuario
    {
        public int IdUsr { get; set; }
        /// <summary>O login. Unico, sem diferenciar maiusculas.</summary>
        public string? NomeUsr { get; set; }

        /// <summary>
        /// O nome da pessoa, para a lista e a lateral. Quando nao foi
        /// preenchido -- acessos criados antes de o campo existir --,
        /// o login serve de reserva.
        /// </summary>
        public string? NomeExibicao { get; set; }

        public string NomeVisivel =>
            string.IsNullOrWhiteSpace(NomeExibicao) ? (NomeUsr ?? "") : NomeExibicao;
        public string? EmailUsr { get; set; }
        public string? SenhaUsr { get; set; }
        public bool AtivoUsr { get; set; }
        public DateTime DataCriacaoUsr { get; set; }

        /// <summary><see cref="Perfil.Admin"/> ou <see cref="Perfil.Comum"/>.</summary>
        public string PerfilUsr { get; set; } = Perfil.Comum;

        /// <summary>
        /// Super-administrador. Unico que concede ou retira o perfil de
        /// administrador, e nao pode ser excluido nem desativado. Marcado no
        /// banco, nunca por nome fixo no codigo.
        /// </summary>
        public bool RaizUsr { get; set; }

        /// <summary>Senha provisoria: a proxima entrada obriga a trocar.</summary>
        public bool TrocarSenhaUsr { get; set; }

        /// <summary>Em que formato a senha gravada esta. Ver SegurancaSenha.</summary>
        public string? AlgoSenhaUsr { get; set; }

        public int? IdFuncao { get; set; }
        public string? NomeFuncao { get; set; }

        /// <summary>
        /// O profissional da barbearia a quem este acesso pertence, quando
        /// for o caso. Liga o login a comissao da mesma pessoa.
        /// </summary>
        public int? IdFuncionario { get; set; }
        public string? NomeFuncionario { get; set; }

        public DateTime? UltimoLoginUsr { get; set; }
        public DateTime? AtualizadoUsr { get; set; }

        public bool EhAdmin => Perfil.EhAdmin(PerfilUsr);

        /// <summary>Até duas iniciais, para o avatar da lista e da lateral.</summary>
        public string Iniciais
        {
            get
            {
                var partes = NomeVisivel.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (partes.Length == 0) return "?";
                if (partes.Length == 1) return partes[0][..1].ToUpperInvariant();

                return (partes[0][..1] + partes[^1][..1]).ToUpperInvariant();
            }
        }
    }

    /// <summary>Os dois perfis do sistema.</summary>
    public static class Perfil
    {
        public const string Admin = "ADMIN";
        public const string Comum = "COMUM";

        public static bool EhAdmin(string? perfil) =>
            string.Equals(perfil, Admin, StringComparison.OrdinalIgnoreCase);

        public static string Rotulo(string? perfil) =>
            EhAdmin(perfil) ? "Administrador" : "Comum";

        /// <summary>Normaliza o que vem da tela para um dos dois valores.</summary>
        public static string Normalizar(string? perfil) => EhAdmin(perfil) ? Admin : Comum;
    }
}
