namespace LucasAguiar.Models
{
    /// <summary>
    /// Uma sessao aberta, como aparece na tela "Minhas sessões". O token
    /// nunca sai do banco em texto, por isso nao esta aqui.
    /// </summary>
    public class Sessao
    {
        public int IdSessao { get; set; }
        public int IdUsuario { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime ExpiraEm { get; set; }
        public DateTime? EncerradaEm { get; set; }
        public string? Ip { get; set; }
        public string? Agente { get; set; }

        /// <summary>A sessao de quem esta olhando a tela.</summary>
        public bool EstaAtual { get; set; }

        public bool EstaValida =>
            EncerradaEm == null && ExpiraEm > DateTime.Now;

        /// <summary>
        /// Navegador e sistema a partir do user-agent. Nao precisa ser
        /// exato: serve para a pessoa reconhecer "fui eu" na lista.
        /// </summary>
        public string Dispositivo
        {
            get
            {
                var agente = Agente ?? "";

                var navegador =
                    agente.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
                    agente.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera" :
                    agente.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
                    agente.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
                    agente.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
                    "Navegador";

                var sistema =
                    agente.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
                    agente.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone" :
                    agente.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" :
                    agente.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
                    agente.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "Mac" :
                    agente.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" :
                    "";

                return string.IsNullOrEmpty(sistema) ? navegador : $"{navegador} · {sistema}";
            }
        }
    }
}
