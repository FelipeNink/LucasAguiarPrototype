using LucasAguiar.Configs;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// O catalogo e a fonte unica das telas e acoes. Se ele estiver
    /// incompleto ou incoerente, a matriz de permissoes, o menu e as
    /// guardas de pagina herdam o erro -- por isso ele tem teste proprio.
    ///
    /// O teste que mais importa aqui e o <see cref="Toda_rota_do_sistema_esta_no_catalogo"/>:
    /// uma tela nova criada sem entrada no catalogo ficaria sem permissao
    /// nenhuma e, pior, sem ninguem notar.
    /// </summary>
    public class CatalogoTeste
    {
        /// <summary>Rotas que existem de proposito fora do controle de acesso.</summary>
        private static readonly string[] Publicas = { "/login", "/logout", "/error" };

        [Fact]
        public void Toda_tela_exige_pelo_menos_acessar()
        {
            foreach (var tela in Catalogo.Telas)
            {
                Assert.True(tela.Tem(Acao.Ver),
                    $"A tela '{tela.Chave}' nao declara Acao.Ver: sem ela as outras "
                    + "acoes nunca sao alcancaveis.");
            }
        }

        [Fact]
        public void Chaves_nao_se_repetem()
        {
            var repetidas = Catalogo.Telas
                .GroupBy(t => t.Chave, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            Assert.Empty(repetidas);
        }

        [Fact]
        public void Toda_secao_declarada_esta_na_ordem_da_lateral()
        {
            foreach (var tela in Catalogo.Telas)
            {
                Assert.Contains(tela.Secao, Catalogo.Secoes);
            }
        }

        [Fact]
        public void Cada_acao_tem_coluna_propria_no_banco()
        {
            var colunas = Catalogo.Acoes.Select(Catalogo.Coluna).ToList();

            Assert.Equal(colunas.Count, colunas.Distinct().Count());
            Assert.All(colunas, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        }

        /// <summary>
        /// Varre as diretivas @page do assembly e exige que cada uma caia
        /// numa tela do catalogo. E o que impede uma tela nova de nascer
        /// fora do controle de acesso.
        /// </summary>
        [Fact]
        public void Toda_rota_do_sistema_esta_no_catalogo()
        {
            var faltando = new List<string>();

            foreach (var rota in RotasDoSistema())
            {
                if (Publicas.Contains(rota, StringComparer.OrdinalIgnoreCase)) continue;

                if (Catalogo.PorRota(rota) == null)
                    faltando.Add(rota);
            }

            Assert.True(faltando.Count == 0,
                "Rotas sem entrada no catalogo (nascem sem controle de acesso): "
                + string.Join(", ", faltando));
        }

        [Fact]
        public void Endpoint_de_download_pertence_a_propria_tela_e_exige_exportar()
        {
            foreach (var tela in Catalogo.Telas)
            {
                foreach (var download in tela.Download)
                {
                    Assert.True(tela.Tem(Acao.Exportar),
                        $"A tela '{tela.Chave}' tem download mas nao declara Acao.Exportar.");

                    Assert.Equal(Acao.Exportar, Catalogo.AcaoDaRota(download));

                    var resolvida = Catalogo.PorRota(download);
                    Assert.Equal(tela.Chave, resolvida?.Chave);
                }
            }
        }

        [Theory]
        [InlineData("/", "painel")]
        [InlineData("/vendas", "vendas")]
        [InlineData("/venda/nova", "vendas")]
        [InlineData("/plano/3/componentes", "planos")]   // prefixo mais longo
        [InlineData("/relatorio/caixa/12", "caixa")]
        [InlineData("/excel/despesas", "despesas")]
        [InlineData("vendas", "vendas")]                 // sem barra inicial
        [InlineData("/VENDAS", "vendas")]                // sem diferenciar caixa
        public void Resolve_o_caminho_na_tela_certa(string caminho, string esperada)
        {
            Assert.Equal(esperada, Catalogo.PorRota(caminho)?.Chave);
        }

        [Theory]
        [InlineData("/tela-que-nao-existe")]
        [InlineData("")]
        [InlineData(null)]
        public void Caminho_desconhecido_nao_resolve_em_tela_nenhuma(string? caminho)
        {
            Assert.Null(Catalogo.PorRota(caminho));
        }

        [Fact]
        public void Acao_de_pagina_e_ver_e_de_download_e_exportar()
        {
            Assert.Equal(Acao.Ver, Catalogo.AcaoDaRota("/vendas"));
            Assert.Equal(Acao.Exportar, Catalogo.AcaoDaRota("/relatorio/vendas"));
            Assert.Equal(Acao.Exportar, Catalogo.AcaoDaRota("/excel/vendas"));
        }

        [Fact]
        public void Acoes_sensiveis_sao_as_quatro_esperadas()
        {
            var sensiveis = Catalogo.Acoes.Where(Catalogo.EhSensivel).ToArray();

            Assert.Equal(
                new[] { Acao.Excluir, Acao.Estornar, Acao.Liquidar, Acao.VerValores },
                sensiveis);
        }

        /// <summary>Toda diretiva @page compilada no assembly do sistema.</summary>
        private static IEnumerable<string> RotasDoSistema()
        {
            var assembly = typeof(Catalogo).Assembly;

            return assembly.GetTypes()
                .Where(t => typeof(IComponent).IsAssignableFrom(t))
                .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false)
                    .Cast<RouteAttribute>())
                .Select(r => r.Template)
                .Distinct();
        }
    }
}
