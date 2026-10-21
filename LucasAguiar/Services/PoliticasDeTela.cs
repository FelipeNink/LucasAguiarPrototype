using LucasAguiar.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Nomes das politicas de acesso por tela.
    ///
    /// Constante, e nao string solta em cada pagina: [Authorize(Policy =
    /// ...)] exige literal, e um erro de digitacao viraria uma politica
    /// inexistente -- que o ASP.NET Core trata como recusa, deixando a
    /// tela inacessivel sem ninguem entender por que. Com as constantes,
    /// o compilador pega.
    ///
    /// O teste PoliticaTeste confere que toda tela do catalogo tem a sua e
    /// que toda pagina usa a da propria tela.
    /// </summary>
    public static class Politicas
    {
        public const string Prefixo = "tela:";

        /// <summary>Monta o nome. Para atributo, use as constantes abaixo.</summary>
        public static string Para(string tela, Acao acao) => $"{Prefixo}{tela}:{acao}";

        public const string Painel = "tela:painel:Ver";
        public const string Vendas = "tela:vendas:Ver";
        public const string Agendamentos = "tela:agendamentos:Ver";
        public const string Caixa = "tela:caixa:Ver";
        public const string Relatorios = "tela:relatorios:Ver";
        public const string FluxoCaixa = "tela:fluxo:Ver";
        public const string Despesas = "tela:despesas:Ver";
        public const string Comissoes = "tela:comissoes:Ver";
        public const string Folha = "tela:folha:Ver";
        public const string Compras = "tela:compras:Ver";
        public const string NotasFiscais = "tela:notas:Ver";
        public const string Clientes = "tela:clientes:Ver";
        public const string Servicos = "tela:servicos:Ver";
        public const string Produtos = "tela:produtos:Ver";
        public const string Estoque = "tela:estoque:Ver";
        public const string Planos = "tela:planos:Ver";
        public const string Assinaturas = "tela:assinaturas:Ver";
        public const string Funcionarios = "tela:funcionarios:Ver";
        public const string Fornecedores = "tela:fornecedores:Ver";
        public const string Equipe = "tela:equipe:Ver";
        public const string Funcoes = "tela:funcoes:Ver";
        public const string Logs = "tela:logs:Ver";
        public const string Horario = "tela:horario:Ver";

        /// <summary>Criar e o que as paginas de cadastro exigem.</summary>
        public const string CriarVenda = "tela:vendas:Criar";
        public const string CriarAgendamento = "tela:agendamentos:Criar";
        public const string CriarCompra = "tela:compras:Criar";
        public const string CriarCliente = "tela:clientes:Criar";
        public const string CriarServico = "tela:servicos:Criar";
        public const string CriarProduto = "tela:produtos:Criar";
        public const string CriarPlano = "tela:planos:Criar";
        public const string CriarFuncionario = "tela:funcionarios:Criar";
        public const string CriarFornecedor = "tela:fornecedores:Criar";
        public const string CriarMembro = "tela:equipe:Criar";

    /// <summary>A tela de componentes do plano so existe para editar.</summary>
    public const string EditarPlano = "tela:planos:Editar";

    /// <summary>Paginas de edicao dos cadastros, que sairam dos modais da listagem.</summary>
    public const string EditarCliente = "tela:clientes:Editar";
    public const string EditarVenda = "tela:vendas:Editar";
    public const string EditarServico = "tela:servicos:Editar";
    public const string EditarProduto = "tela:produtos:Editar";
    public const string EditarFuncionario = "tela:funcionarios:Editar";
    public const string EditarFornecedor = "tela:fornecedores:Editar";
    public const string EditarCompra = "tela:compras:Editar";
    public const string EditarAgendamento = "tela:agendamentos:Editar";
    public const string CriarDespesa = "tela:despesas:Criar";
    public const string EditarDespesa = "tela:despesas:Editar";
    public const string ConfigurarNotas = "tela:notas:Editar";
    }

    /// <summary>Exige uma acao numa tela nomeada.</summary>
    public class ExigeTela : IAuthorizationRequirement
    {
        public string Tela { get; }
        public Acao Acao { get; }

        public ExigeTela(string tela, Acao acao)
        {
            Tela = tela;
            Acao = acao;
        }
    }

    /// <summary>
    /// Exige a permissao correspondente ao caminho da requisicao, descoberta
    /// no catalogo.
    ///
    /// E a rede de seguranca: vale para todo endpoint que nao declarou
    /// politica propria -- os dezenove downloads de /relatorio e /excel,
    /// e qualquer endpoint novo que alguem acrescentar sem lembrar de
    /// proteger. Caminho que nao casa com tela nenhuma (o circuito do
    /// Blazor, a troca de senha) exige apenas estar logado.
    /// </summary>
    public class ExigePermissaoDaRota : IAuthorizationRequirement
    {
    }

    /// <summary>
    /// Decide as duas exigencias. Ambas passam pelo mesmo
    /// <see cref="Autorizacao"/>, entao a regra e uma so, esteja a recusa
    /// vindo do pipeline HTTP ou da navegacao dentro do circuito.
    /// </summary>
    public class ManipuladorDeTela :
        IAuthorizationHandler
    {
        private readonly Autorizacao _autorizacao;
        private readonly IHttpContextAccessor _http;

        public ManipuladorDeTela(Autorizacao autorizacao, IHttpContextAccessor http)
        {
            _autorizacao = autorizacao;
            _http = http;
        }

        public async Task HandleAsync(AuthorizationHandlerContext contexto)
        {
            if (contexto.User.Identity?.IsAuthenticated != true) return;

            Models.Permissoes? permissoes = null;

            foreach (var exigencia in contexto.PendingRequirements.ToList())
            {
                switch (exigencia)
                {
                    case ExigeTela porTela:
                        permissoes ??= _autorizacao.Resolver(contexto.User);

                        if (permissoes.Pode(porTela.Tela, porTela.Acao))
                            contexto.Succeed(exigencia);

                        break;

                    case ExigePermissaoDaRota:
                        if (await PodeNaRota(contexto))
                            contexto.Succeed(exigencia);

                        break;
                }
            }
        }

        private Task<bool> PodeNaRota(AuthorizationHandlerContext contexto)
        {
            var caminho = _http.HttpContext?.Request.Path.Value;

            // Fora de uma requisicao HTTP nao ha rota a conferir: quem
            // protege a navegacao no circuito sao as politicas nomeadas
            // das paginas.
            if (string.IsNullOrWhiteSpace(caminho)) return Task.FromResult(true);

            var tela = Catalogo.PorRota(caminho);

            // Caminho sem tela correspondente: basta estar logado.
            if (tela == null) return Task.FromResult(true);

            var permissoes = _autorizacao.Resolver(contexto.User);

            return Task.FromResult(permissoes.Pode(tela.Chave, Catalogo.AcaoDaRota(caminho)));
        }
    }

    /// <summary>
    /// Cria as politicas "tela:chave:Acao" sob demanda. Sem isto seria
    /// preciso registrar dezessete telas vezes oito acoes na subida, e uma
    /// tela nova exigiria lembrar de registrar a dela.
    /// </summary>
    public class ProvedorDePoliticas : DefaultAuthorizationPolicyProvider
    {
        public ProvedorDePoliticas(IOptions<AuthorizationOptions> opcoes) : base(opcoes)
        {
        }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string nome)
        {
            var existente = await base.GetPolicyAsync(nome);
            if (existente != null) return existente;

            if (!nome.StartsWith(Politicas.Prefixo, StringComparison.Ordinal))
                return null;

            var partes = nome[Politicas.Prefixo.Length..].Split(':');
            if (partes.Length != 2) return null;

            if (Catalogo.Por(partes[0]) == null) return null;
            if (!Enum.TryParse<Acao>(partes[1], out var acao)) return null;

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ExigeTela(partes[0], acao))
                .Build();
        }
    }
}
