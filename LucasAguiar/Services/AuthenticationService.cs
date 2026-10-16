using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Quem esta usando o sistema, do ponto de vista de um componente.
    ///
    /// Antes isto lia a sessao pelo IHttpContextAccessor. Em Blazor Server
    /// isso so funciona enquanto a pagina e renderizada no servidor: depois
    /// que o circuito liga, nao existe mais HttpContext, e o nome do usuario
    /// vinha vazio. O estado agora vem do AuthenticationStateProvider, que
    /// vale nos dois momentos.
    ///
    /// Como ler o estado e assincrono e a maioria dos usos esta dentro de
    /// propriedades e tratadores sincronos, a pagina chama
    /// <see cref="PrepararAsync"/> uma vez no OnInitializedAsync e os
    /// acessos seguintes saem do que ficou carregado. O servico e scoped,
    /// portanto o carregado vale por circuito.
    /// </summary>
    public class AuthenticationService
    {
        private readonly AuthenticationStateProvider _provedor;
        private ClaimsPrincipal? _usuario;

        public AuthenticationService(AuthenticationStateProvider provedor)
        {
            _provedor = provedor;
        }

        /// <summary>
        /// Carrega o estado de autenticacao. Chamar no OnInitializedAsync
        /// de quem precisa saber quem esta logado.
        /// </summary>
        public async Task<ClaimsPrincipal> PrepararAsync()
        {
            var estado = await _provedor.GetAuthenticationStateAsync();
            _usuario = estado.User;
            return _usuario;
        }

        public bool IsAuthenticated() => _usuario?.Identity?.IsAuthenticated == true;

        public string? GetUsuarioNome() =>
            IsAuthenticated() ? _usuario!.FindFirst(ClaimTypes.Name)?.Value : null;

        public int? GetUsuarioId()
        {
            var bruto = _usuario?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(bruto, out var id) ? id : null;
        }

        public string? GetPerfil() =>
            _usuario?.FindFirst(AutenticacaoPorSessao.ClaimPerfil)?.Value;

        public string? GetFuncao() =>
            _usuario?.FindFirst(AutenticacaoPorSessao.ClaimFuncao)?.Value;

        public bool EhAdmin() =>
            Models.Perfil.EhAdmin(GetPerfil());

        public bool EhRaiz() =>
            _usuario?.FindFirst(AutenticacaoPorSessao.ClaimRaiz)?.Value == "1";

        public bool PrecisaTrocarSenha() =>
            _usuario?.FindFirst(AutenticacaoPorSessao.ClaimTrocarSenha)?.Value == "1";

        /// <summary>
        /// O token da sessao em curso. Serve para preservar esta sessao ao
        /// derrubar as outras, e para marcar "este dispositivo" na lista.
        /// </summary>
        public string? GetToken() =>
            _usuario?.FindFirst(AutenticacaoPorSessao.ClaimToken)?.Value;

        /// <summary>Até duas iniciais do nome, para o avatar da lateral.</summary>
        public string Iniciais
        {
            get
            {
                var partes = (GetUsuarioNome() ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (partes.Length == 0) return "?";
                if (partes.Length == 1) return partes[0][..1].ToUpperInvariant();

                return (partes[0][..1] + partes[^1][..1]).ToUpperInvariant();
            }
        }
    }
}
