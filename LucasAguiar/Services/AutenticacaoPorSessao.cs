using System.Security.Claims;
using System.Text.Encodings.Web;
using LucasAguiar.Data;
using LucasAguiar.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Autenticacao por token de sessao guardado no banco.
    ///
    /// O cookie leva apenas o token -- 32 bytes aleatorios --, e quem e o
    /// dono dele e resolvido no banco a cada requisicao. Duas consequencias
    /// que o desenho anterior nao tinha:
    ///
    /// 1. Nada do usuario fica congelado no cookie. Perfil, "ativo" e troca
    ///    de senha obrigatoria vem do banco sempre, entao desativar alguem
    ///    ou mudar o perfil dele vale na requisicao seguinte, nao no
    ///    proximo login.
    /// 2. Nao depende do Data Protection. O ticket cifrado padrao do
    ///    ASP.NET Core usa chaves que, num container, morrem a cada deploy
    ///    -- era isso que deslogava todo mundo a cada publicacao. O token
    ///    nao precisa de cifra: ele ja e segredo, e o banco guarda so o
    ///    hash dele.
    /// </summary>
    public class AutenticacaoPorSessao : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string Esquema = "SessaoBarbearia";

        /// <summary>Nome do cookie que leva o token.</summary>
        public const string NomeCookie = "barbearia_sessao";

        public const string ClaimPerfil = "perfil";
        public const string ClaimRaiz = "raiz";
        public const string ClaimToken = "sessao";
        public const string ClaimTrocarSenha = "trocar_senha";
        public const string ClaimFuncao = "funcao";

        /// <summary>Para onde voltar depois de entrar.</summary>
        public const string ParametroVolta = "voltar";

        private readonly SessaoDAO _sessoes;

        public AutenticacaoPorSessao(
            IOptionsMonitor<AuthenticationSchemeOptions> opcoes,
            ILoggerFactory registro,
            UrlEncoder codificador,
            SessaoDAO sessoes)
            : base(opcoes, registro, codificador)
        {
            _sessoes = sessoes;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Cookies[NomeCookie];

            if (string.IsNullOrWhiteSpace(token))
                return Task.FromResult(AuthenticateResult.NoResult());

            Acesso? acesso;
            try
            {
                acesso = _sessoes.Validar(token);
            }
            catch (Exception ex)
            {
                // Banco fora do ar nao pode virar "sessao invalida": isso
                // apagaria o cookie de todo mundo numa queda passageira.
                Logger.LogError(ex, "Não foi possível validar a sessão.");
                return Task.FromResult(AuthenticateResult.Fail("Falha ao validar a sessão."));
            }

            if (acesso == null)
            {
                // Sessao expirada, encerrada, ou de conta desativada.
                //
                // O cookie NAO e apagado aqui, embora fosse a reacao
                // obvia: o Delete remove pelo nome, sem olhar o valor, e
                // um pedido atrasado carregando um token velho -- uma aba
                // antiga, ou uma requisicao em voo durante a troca de
                // usuario -- derrubaria o cookie novo que outro pedido
                // acabou de gravar. A pessoa era deslogada no meio do uso,
                // sem motivo aparente.
                //
                // Nao apagar nao deixa nada aberto: o token invalido
                // continua invalido, e quem apaga o cookie de verdade e a
                // saida, que sabe qual token esta encerrando.
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var bilhete = new AuthenticationTicket(Principal(acesso, token), Esquema);
            return Task.FromResult(AuthenticateResult.Success(bilhete));
        }

        /// <summary>
        /// Sem login: manda para a tela de entrada guardando o endereco
        /// pedido, para voltar a ele depois.
        /// </summary>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var pedido = Request.Path + Request.QueryString;
            var destino = "/login";

            if (!string.IsNullOrWhiteSpace(pedido) && pedido != "/")
            {
                destino += "?" + ParametroVolta + "=" + Uri.EscapeDataString(pedido);
            }

            Response.Redirect(destino);
            return Task.CompletedTask;
        }

        /// <summary>Logado, mas sem permissao para aquilo.</summary>
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.Redirect("/sem-permissao?de="
                + Uri.EscapeDataString(Request.Path + Request.QueryString));

            return Task.CompletedTask;
        }

        // ---------- cookie ----------

        /// <summary>
        /// Grava o cookie da sessao. Chamado pela tela de login, que roda
        /// em renderizacao estatica e por isso ainda tem HttpContext com a
        /// resposta sem comecar.
        /// </summary>
        public static void GravarCookie(HttpContext contexto, string token, DateTime expiraEm)
        {
            contexto.Response.Cookies.Append(NomeCookie, token, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                // Secure acompanha o pedido, em vez de ser sempre true: em
                // desenvolvimento o sistema roda em http e um cookie Secure
                // simplesmente nao chegaria. Em producao o Railway atende em
                // https, e o UseForwardedHeaders ja faz o IsHttps saber disso
                // mesmo com o proxy entregando http ao container.
                Secure = contexto.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = expiraEm
            });
        }

        public static void ApagarCookie(HttpContext contexto)
        {
            // O Delete tem de repetir Path e SameSite do Append, senao o
            // navegador entende como outro cookie e o original fica.
            contexto.Response.Cookies.Delete(NomeCookie, new CookieOptions
            {
                HttpOnly = true,
                Secure = contexto.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }

        public static string? TokenDoCookie(HttpContext? contexto) =>
            contexto?.Request.Cookies[NomeCookie];

        // ---------- claims ----------

        private static ClaimsPrincipal Principal(Acesso acesso, string token)
        {
            var identidade = new ClaimsIdentity(Esquema);

            identidade.AddClaim(new Claim(
                ClaimTypes.NameIdentifier, acesso.IdUsuario.ToString()));
            identidade.AddClaim(new Claim(ClaimTypes.Name, acesso.Nome));
            identidade.AddClaim(new Claim(ClaimPerfil, acesso.Usuario.PerfilUsr));
            identidade.AddClaim(new Claim(ClaimRaiz, acesso.EhRaiz ? "1" : "0"));
            identidade.AddClaim(new Claim(
                ClaimTrocarSenha, acesso.PrecisaTrocarSenha ? "1" : "0"));
            identidade.AddClaim(new Claim(ClaimToken, token));

            if (!string.IsNullOrWhiteSpace(acesso.Usuario.NomeFuncao))
                identidade.AddClaim(new Claim(ClaimFuncao, acesso.Usuario.NomeFuncao));

            // O perfil tambem entra como papel, para [Authorize(Roles = ...)]
            // e <AuthorizeView Roles="ADMIN"> funcionarem sem codigo extra.
            identidade.AddClaim(new Claim(ClaimTypes.Role, acesso.Usuario.PerfilUsr));

            return new ClaimsPrincipal(identidade);
        }
    }
}
