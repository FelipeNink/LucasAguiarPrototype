using LucasAguiar.Components;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using LucasAguiar.Data;
using LucasAguiar.Services;
using Microsoft.AspNetCore.HttpOverrides;



var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<VendaDAO>();
builder.Services.AddScoped<AssinaturaDAO>();
builder.Services.AddScoped<CaixaDAO>();
builder.Services.AddScoped<DespesaDAO>();
builder.Services.AddScoped<PainelDAO>();
builder.Services.AddScoped<ComissaoDAO>();
builder.Services.AddScoped<FolhaDAO>();
builder.Services.AddScoped<UsuarioDAO>();
builder.Services.AddScoped<BancoInicializador>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<RelatorioPdf>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});




var app = builder.Build();

// Cria o esquema e o primeiro login se o banco estiver vazio. Roda uma
// vez, na subida, e nao faz nada quando as tabelas ja existem.
using (var escopo = app.Services.CreateScope())
{
    escopo.ServiceProvider.GetRequiredService<BancoInicializador>().Preparar();
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

app.UseSession();

// Bloqueia qualquer pagina do sistema para quem nao esta autenticado.
// Somente /login, /logout e /Error sao publicos.
app.Use(async (context, next) =>
{
    var caminho = context.Request.Path.Value ?? "/";

    var ehPublico = caminho.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
        || caminho.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
        || Path.HasExtension(caminho);

    var autenticado = !string.IsNullOrEmpty(context.Session.GetString("UsuarioId"));

    if (!ehPublico && !autenticado)
    {
        context.Response.Redirect("/login");
        return;
    }

    await next();
});

app.UseAntiforgery();

app.MapStaticAssets();

// Sonda de saude da hospedagem. O Railway so manda trafego para a versao
// nova depois que isto responde 200, e reinicia o container se parar de
// responder.
//
// De proposito nao toca no banco: se o MySQL cair por um instante, o
// certo e a aplicacao continuar de pe mostrando o erro, e nao o host
// derrubar o container e tirar todo mundo do ar.
app.MapGet("/health", () => Results.Ok("ok"));

app.MapearRelatorios();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
