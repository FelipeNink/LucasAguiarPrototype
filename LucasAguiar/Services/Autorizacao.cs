using System.Security.Claims;
using LucasAguiar.Configs;
using LucasAguiar.Data;
using LucasAguiar.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace LucasAguiar.Services
{
    /// <summary>
    /// O que a pessoa logada pode fazer. Fonte unica consultada pelas
    /// guardas de pagina, pelos endpoints de download, pelas telas que
    /// escondem botoes e pelos tratadores que executam a acao.
    ///
    /// As permissoes sao lidas do banco, e nao do cookie: cookie e estado
    /// congelado no login, e tirar uma permissao de alguem so valeria no
    /// dia seguinte. Para nao consultar o banco a cada botao desenhado, o
    /// resultado vale por <see cref="Validade"/>; depois disso a proxima
    /// pergunta le de novo. Na pratica: mudar a matriz de alguem passa a
    /// valer em no maximo quinze segundos, sem a pessoa recarregar nada.
    /// </summary>
    public class Autorizacao
    {
        private readonly AuthenticationStateProvider _provedor;
        private readonly PermissaoDAO _permissoes;
        private readonly ILogger<Autorizacao> _log;

        /// <summary>Quanto tempo o conjunto lido continua valendo.</summary>
        public static readonly TimeSpan Validade = TimeSpan.FromSeconds(15);

        private Permissoes _atual = Permissoes.Nenhuma;
        private int? _idUsuario;
        private bool _carregado;

        public Autorizacao(
            AuthenticationStateProvider provedor,
            PermissaoDAO permissoes,
            ILogger<Autorizacao> log)
        {
            _provedor = provedor;
            _permissoes = permissoes;
            _log = log;
        }

        public int? IdUsuario => _idUsuario;
        public bool EhAdmin => _atual.EhAdmin;

        /// <summary>
        /// Carrega as permissoes de quem esta logado. Chamar no
        /// OnInitializedAsync da pagina, antes de desenhar qualquer botao
        /// que dependa de permissao.
        /// </summary>
        public async Task<Permissoes> CarregarAsync(bool forcar = false)
        {
            if (!forcar && _carregado && DateTime.Now - _atual.Carregado < Validade)
                return _atual;

            var estado = await _provedor.GetAuthenticationStateAsync();

            _atual = Resolver(estado.User);
            _carregado = true;

            return _atual;
        }

        /// <summary>
        /// Monta o conjunto a partir do principal. Usado tanto aqui quanto
        /// pela checagem de rota no pipeline HTTP, para os dois caminhos
        /// decidirem pela mesma regra.
        /// </summary>
        public Permissoes Resolver(ClaimsPrincipal? usuario)
        {
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                _idUsuario = null;
                return Permissoes.Nenhuma;
            }

            var bruto = usuario.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(bruto, out var id))
            {
                _idUsuario = null;
                return Permissoes.Nenhuma;
            }

            _idUsuario = id;

            var ehAdmin = Perfil.EhAdmin(
                usuario.FindFirst(AutenticacaoPorSessao.ClaimPerfil)?.Value);

            if (ehAdmin) return Permissoes.DeAdmin;

            try
            {
                return _permissoes.Carregar(id, false);
            }
            catch (Exception ex)
            {
                // Banco fora do ar nao pode virar acesso liberado. Recusar
                // tudo e o lado seguro do erro.
                _log.LogError(ex, "Não foi possível carregar as permissões do usuário {Id}.", id);
                return Permissoes.Nenhuma;
            }
        }

        /// <summary>
        /// Pode, pelo que foi carregado. Chamar <see cref="CarregarAsync"/>
        /// antes; sem isso devolve false, que e o lado seguro.
        /// </summary>
        public bool Pode(string tela, Acao acao) => _atual.Pode(tela, acao);

        public bool PodeVer(string tela) => _atual.PodeVer(tela);

        public Permissoes Atuais => _atual;

        /// <summary>
        /// Barra a acao se a pessoa nao puder faze-la. Vai no inicio do
        /// tratador que executa a acao, e nao so no if que desenha o botao:
        /// esconder o botao e cortesia com quem olha a tela, nao controle
        /// de acesso.
        ///
        /// Reconsulta o banco antes de recusar, para que uma permissao
        /// concedida ha poucos segundos nao derrube uma acao legitima.
        /// </summary>
        public async Task ExigirAsync(string tela, Acao acao)
        {
            if (Pode(tela, acao)) return;

            await CarregarAsync(forcar: true);

            if (Pode(tela, acao)) return;

            throw new AcessoNegadoException(tela, acao);
        }
    }

    /// <summary>
    /// Acao recusada por falta de permissao. Mensagem pronta para a tela,
    /// dizendo o que faltou em vez de um "erro inesperado".
    /// </summary>
    public class AcessoNegadoException : Exception
    {
        public string Tela { get; }
        public Acao Acao { get; }

        public AcessoNegadoException(string tela, Acao acao)
            : base(Mensagem(tela, acao))
        {
            Tela = tela;
            Acao = acao;
        }

        private static string Mensagem(string tela, Acao acao)
        {
            var nome = Catalogo.Por(tela)?.Nome ?? tela;

            return acao == Acao.Ver
                ? $"Você não tem permissão para acessar {nome}."
                : $"Você não tem permissão para {Catalogo.Rotulo(acao).ToLowerInvariant()} em {nome}.";
        }
    }
}
