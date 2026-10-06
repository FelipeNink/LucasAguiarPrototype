using Microsoft.AspNetCore.Http;

namespace LucasAguiar.Services
{
    public class AuthenticationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthenticationService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public bool IsAuthenticated()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null)
                return false;

            var usuarioId = httpContext.Session.GetString("UsuarioId");
            return !string.IsNullOrEmpty(usuarioId);
        }

        public string? GetUsuarioNome()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null)
                return null;

            return httpContext.Session.GetString("UsuarioNome");
        }

        public string? GetUsuarioId()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null)
                return null;

            return httpContext.Session.GetString("UsuarioId");
        }

        public void Logout()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                httpContext.Session.Clear();
            }
        }
    }
}
