using LucasAguiar.Configs;

namespace LucasAguiar.Models
{
    /// <summary>
    /// O que um usuario pode fazer, tela por tela.
    ///
    /// Para administrador a matriz nao e consultada: ele pode tudo, e e por
    /// isso que <see cref="EhAdmin"/> curto-circuita o <see cref="Pode"/>.
    /// Guardar linhas de permissao para admin so criaria a chance de um dia
    /// elas discordarem do perfil.
    /// </summary>
    public class Permissoes
    {
        private readonly Dictionary<string, Acao> _porTela;

        public bool EhAdmin { get; }

        /// <summary>Quando o conjunto foi lido do banco. Serve ao cache curto.</summary>
        public DateTime Carregado { get; } = DateTime.Now;

        public Permissoes(bool ehAdmin, Dictionary<string, Acao>? porTela = null)
        {
            EhAdmin = ehAdmin;
            _porTela = porTela ?? new Dictionary<string, Acao>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Ninguem pode nada. Usado para quem nao esta logado.</summary>
        public static Permissoes Nenhuma { get; } = new(false);

        /// <summary>Tudo liberado, sem consultar a matriz.</summary>
        public static Permissoes DeAdmin { get; } = new(true);

        public Acao De(string? tela)
        {
            if (string.IsNullOrWhiteSpace(tela)) return Acao.Nenhuma;

            if (EhAdmin)
            {
                // O admin tem as acoes que a tela declara, e nada alem:
                // marcar "Estornar" numa tela que nao estorna nada nao
                // significa coisa nenhuma.
                return Catalogo.Por(tela)?.Acoes ?? Acao.Nenhuma;
            }

            return _porTela.TryGetValue(tela, out var acoes) ? acoes : Acao.Nenhuma;
        }

        /// <summary>
        /// Pode fazer <paramref name="acao"/> em <paramref name="tela"/>?
        ///
        /// Qualquer acao exige tambem o Acessar da mesma tela: criar sem
        /// poder abrir a tela nao e um estado que deva existir, e a regra
        /// aqui garante isso mesmo que a linha do banco diga o contrario.
        /// </summary>
        public bool Pode(string? tela, Acao acao)
        {
            if (acao == Acao.Nenhuma) return true;

            var minhas = De(tela);

            if ((minhas & Acao.Ver) != Acao.Ver) return false;

            return (minhas & acao) == acao;
        }

        public bool PodeVer(string? tela) => Pode(tela, Acao.Ver);

        /// <summary>As telas que a pessoa abre, na ordem do catalogo.</summary>
        public IEnumerable<Tela> TelasVisiveis() =>
            Catalogo.Telas.Where(t => PodeVer(t.Chave));

        /// <summary>Nenhuma tela liberada: a conta existe mas nao serve para nada.</summary>
        public bool Vazio => !EhAdmin && !Catalogo.Telas.Any(t => PodeVer(t.Chave));
    }
}
