using LucasAguiar.Components;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using LucasAguiar.Data;
using LucasAguiar.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;



var builder = WebApplication.CreateBuilder(args);

// O container nao traz LANG definido e o .NET cai na cultura invariante,
// cujo simbolo de moeda e o "¤": todo ToString("C") saia com ele no lugar
// do R$. Fixa pt-BR para moeda, data e separador decimal.
var culturaPadrao = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culturaPadrao;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culturaPadrao;

// Hospedagem em container: Render, Railway, Fly e afins informam a porta
// pela variavel PORT, mas o ASP.NET Core so olha para ASPNETCORE_URLS.
var porta = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(porta))
{
    builder.WebHost.UseUrls($"http://+:{porta}");
}

// Atras de um proxy que termina o TLS, o pedido chega como http. Sem ler
// o X-Forwarded-Proto, o UseHttpsRedirection abaixo entra em laco infinito
// e o cookie de sessao nao e marcado como seguro.
builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
{
    opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opcoes.KnownNetworks.Clear();
    opcoes.KnownProxies.Clear();
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<Conexao>();
builder.Services.AddScoped<ServicoDAO>();
builder.Services.AddScoped<FornecedorDAO>();
builder.Services.AddScoped<FuncionarioDAO>();
builder.Services.AddScoped<ProdutoDAO>();
builder.Services.AddScoped<CompraDAO>();
builder.Services.AddScoped<ClienteDAO>();
builder.Services.AddScoped<PlanoDAO>();
builder.Services.AddScoped<CategoriaDAO>();
builder.Services.AddScoped<VendaDAO>();
builder.Services.AddScoped<AgendamentoDAO>();
builder.Services.AddScoped<AssinaturaDAO>();
builder.Services.AddScoped<CaixaDAO>();
builder.Services.AddScoped<DespesaDAO>();
builder.Services.AddScoped<PainelDAO>();
builder.Services.AddScoped<RelatoriosDAO>();
builder.Services.AddScoped<ComissaoDAO>();
builder.Services.AddScoped<FolhaDAO>();
builder.Services.AddScoped<UsuarioDAO>();
builder.Services.AddScoped<SessaoDAO>();
builder.Services.AddScoped<TentativaLoginDAO>();
builder.Services.AddScoped<LogDAO>();
builder.Services.AddScoped<BancoInicializador>();
builder.Services.AddScoped<LucasAguiar.Services.AuthenticationService>();
builder.Services.AddScoped<RelatorioPdf>();
builder.Services.AddHttpClient<BuscaCep>();

// Autenticacao por token de sessao guardado no banco. Substitui a sessao
// em memoria do ASP.NET Core, que sumia a cada deploy e nao permitia
// derrubar o acesso de ninguem.
builder.Services
    .AddAuthentication(AutenticacaoPorSessao.Esquema)
    .AddScheme<AuthenticationSchemeOptions, AutenticacaoPorSessao>(
        AutenticacaoPorSessao.Esquema, null);

// Exigir login e permissao e o padrao: uma tela nova nasce protegida, e
// so sai da regra quem tiver [AllowAnonymous] escrito. O caminho oposto
// -- lista de rotas publicas, como era antes -- deixa a porta aberta por
// esquecimento.
//
// A exigencia da rota cobre todo endpoint que nao declarou politica
// propria, inclusive os dezenove downloads de /relatorio e /excel.
builder.Services.AddAuthorization(opcoes =>
{
    opcoes.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new ExigePermissaoDaRota())
        .Build();
});

builder.Services.AddScoped<PermissaoDAO>();
builder.Services.AddScoped<EquipeDAO>();
builder.Services.AddScoped<FuncaoDAO>();
builder.Services.AddScoped<Autorizacao>();
builder.Services.AddScoped<IAuthorizationHandler, ManipuladorDeTela>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, ProvedorDePoliticas>();

// Entrega o estado de autenticacao aos componentes, inclusive dentro do
// circuito, onde nao existe HttpContext.
builder.Services.AddCascadingAuthenticationState();




var app = builder.Build();

// Cria o esquema e o primeiro login se o banco estiver vazio. Roda uma
// vez, na subida, e nao faz nada quando as tabelas ja existem.
using (var escopo = app.Services.CreateScope())
{
    escopo.ServiceProvider.GetRequiredService<BancoInicializador>().Preparar();

    // Faxina do que so cresce: sessoes vencidas, contadores de tentativa de
    // login ja sem efeito e log fora da retencao. Falhar aqui nao impede a
    // aplicacao de subir -- e limpeza, nao requisito.
    try
    {
        escopo.ServiceProvider.GetRequiredService<SessaoDAO>().LimparVencidas();
        escopo.ServiceProvider.GetRequiredService<TentativaLoginDAO>().Limpar();
        escopo.ServiceProvider.GetRequiredService<LogDAO>().AplicarRetencao();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "A limpeza de sessões e logs não pôde ser feita.");
    }
}

// Precisa vir antes de tudo que olha para o esquema ou o IP do pedido.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();

// Senha provisoria obriga a trocar antes de usar o sistema. Sem isto,
// bastava digitar outro endereco para passar direto pela tela de troca e
// a senha definida pelo administrador viraria permanente.
//
// Fica antes da autorizacao para o redirecionamento ser para a troca de
// senha, e nao para o aviso de falta de permissao.
app.Use(async (contexto, proximo) =>
{
    var precisaTrocar = contexto.User
        .FindFirst(AutenticacaoPorSessao.ClaimTrocarSenha)?.Value == "1";

    if (precisaTrocar)
    {
        var caminho = contexto.Request.Path.Value ?? "/";

        var liberado = caminho.StartsWith("/trocar-senha", StringComparison.OrdinalIgnoreCase)
            || caminho.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
            // Entrar como outra pessoa continua possivel sem antes ter de
            // trocar a senha de quem esta logado.
            || caminho.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || caminho.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || caminho.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(caminho);

        if (!liberado)
        {
            contexto.Response.Redirect("/trocar-senha");
            return;
        }
    }

    await proximo();
});

app.UseAuthorization();

app.UseAntiforgery();

// CSS, fontes e scripts ficam fora da exigencia de login: a propria tela
// de entrada precisa deles, e sem isto ela apareceria sem estilo nenhum.
app.MapStaticAssets().AllowAnonymous();

// Sonda de saude da hospedagem. O Railway so manda trafego para a versao
// nova depois que isto responde 200, e reinicia o container se parar de
// responder.
//
// De proposito nao toca no banco: se o MySQL cair por um instante, o
// certo e a aplicacao continuar de pe mostrando o erro, e nao o host
// derrubar o container e tirar todo mundo do ar.
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

app.MapearRelatorios();
app.MapearExcel();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
