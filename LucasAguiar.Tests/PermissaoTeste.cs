using System.Reflection;
using LucasAguiar.Configs;
using LucasAguiar.Data;
using LucasAguiar.Models;
using LucasAguiar.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// O motor de permissao e a amarracao entre as paginas e o catalogo.
    ///
    /// O teste mais importante e o <see cref="Toda_pagina_exige_a_politica_da_propria_tela"/>:
    /// uma tela criada sem politica, ou com a politica de outra tela,
    /// passaria despercebida -- e nenhum dos outros testes pegaria.
    /// </summary>
    public class PermissaoTeste
    {
        /// <summary>
        /// Paginas que de proposito nao exigem permissao de tela: as
        /// publicas e as da propria conta.
        /// </summary>
        private static readonly string[] SemPolitica =
        {
            "Login", "Logout", "Error", "SemPermissao", "TrocarSenha", "Conta"
        };

        // =========================================================
        // REGRAS DO CONJUNTO
        // =========================================================

        [Fact]
        public void Administrador_pode_tudo_que_a_tela_declara()
        {
            var admin = Permissoes.DeAdmin;

            foreach (var tela in Catalogo.Telas)
            {
                foreach (var acao in Catalogo.Acoes.Where(tela.Tem))
                    Assert.True(admin.Pode(tela.Chave, acao), $"{tela.Chave}:{acao}");
            }
        }

        [Fact]
        public void Administrador_nao_ganha_acao_que_a_tela_nao_tem()
        {
            // "Estornar" so existe em Vendas. Nem o admin estorna no Painel.
            Assert.False(Permissoes.DeAdmin.Pode("painel", Acao.Estornar));
            Assert.False(Permissoes.DeAdmin.Pode("clientes", Acao.Liquidar));
            Assert.True(Permissoes.DeAdmin.Pode("vendas", Acao.Estornar));
        }

        [Fact]
        public void Sem_acessar_nenhuma_outra_acao_vale()
        {
            // Linha incoerente no banco -- criar marcado, acessar nao --
            // nao pode virar permissao. A regra e refeita na leitura.
            var permissoes = new Permissoes(false, new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Criar | Acao.Editar | Acao.Excluir
            });

            Assert.False(permissoes.PodeVer("clientes"));
            Assert.False(permissoes.Pode("clientes", Acao.Criar));
            Assert.False(permissoes.Pode("clientes", Acao.Editar));
        }

        [Fact]
        public void Com_acessar_valem_as_acoes_marcadas_e_so_elas()
        {
            var permissoes = new Permissoes(false, new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Ver | Acao.Criar
            });

            Assert.True(permissoes.PodeVer("clientes"));
            Assert.True(permissoes.Pode("clientes", Acao.Criar));
            Assert.False(permissoes.Pode("clientes", Acao.Editar));
            Assert.False(permissoes.Pode("clientes", Acao.Excluir));
            Assert.False(permissoes.Pode("clientes", Acao.Exportar));
        }

        [Fact]
        public void Tela_fora_do_conjunto_e_negada()
        {
            var permissoes = new Permissoes(false, new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Ver
            });

            Assert.False(permissoes.PodeVer("vendas"));
            Assert.False(permissoes.PodeVer("tela-inexistente"));
            Assert.False(permissoes.PodeVer(null));
        }

        [Fact]
        public void Quem_nao_esta_logado_nao_pode_nada()
        {
            foreach (var tela in Catalogo.Telas)
                Assert.False(Permissoes.Nenhuma.PodeVer(tela.Chave));

            Assert.True(Permissoes.Nenhuma.Vazio);
        }

        [Fact]
        public void Telas_visiveis_saem_na_ordem_do_catalogo()
        {
            var permissoes = new Permissoes(false, new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Ver,
                ["painel"] = Acao.Ver,
                ["vendas"] = Acao.Nenhuma
            });

            var chaves = permissoes.TelasVisiveis().Select(t => t.Chave).ToArray();

            Assert.Equal(new[] { "painel", "clientes" }, chaves);
        }

        // =========================================================
        // NORMALIZACAO NA GRAVACAO
        // =========================================================

        [Fact]
        public void Gravacao_descarta_acao_que_a_tela_nao_declara()
        {
            var painel = Catalogo.Por("painel")!;

            // O painel nao tem Criar nem Excluir; a tela pode mandar
            // qualquer coisa, o servidor so grava o que faz sentido.
            var efetivo = PermissaoDAO.Normalizar(
                painel, Acao.Ver | Acao.Criar | Acao.Excluir | Acao.VerValores);

            Assert.Equal(Acao.Ver | Acao.VerValores, efetivo);
        }

        [Fact]
        public void Gravacao_zera_tudo_quando_acessar_nao_vem()
        {
            var clientes = Catalogo.Por("clientes")!;

            Assert.Equal(Acao.Nenhuma,
                PermissaoDAO.Normalizar(clientes, Acao.Criar | Acao.Editar));
        }

        [Fact]
        public void Diferenca_descreve_o_que_mudou_por_tela()
        {
            var antes = new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Ver | Acao.Criar,
                ["vendas"] = Acao.Ver | Acao.Estornar
            };

            var depois = new Dictionary<string, Acao>
            {
                ["clientes"] = Acao.Ver | Acao.Criar | Acao.Editar,
                ["vendas"] = Acao.Ver
            };

            var diferenca = PermissaoDAO.Diferenca(antes, depois);

            Assert.Equal(2, diferenca.Count);
            Assert.Equal("+Editar", diferenca["Clientes"]);
            Assert.Equal("−Estornar", diferenca["Vendas"]);
        }

        [Fact]
        public void Diferenca_vazia_quando_nada_muda()
        {
            var matriz = new Dictionary<string, Acao> { ["clientes"] = Acao.Ver };

            Assert.Empty(PermissaoDAO.Diferenca(matriz, matriz));
        }

        // =========================================================
        // POLITICAS DAS PAGINAS
        // =========================================================

        /// <summary>
        /// Cada pagina tem de exigir a politica da tela a que pertence.
        /// Sem isto, uma pagina nova entraria sem guarda -- ou, pior, com
        /// a guarda de outra tela, liberando quem nao devia.
        /// </summary>
        [Fact]
        public void Toda_pagina_exige_a_politica_da_propria_tela()
        {
            var problemas = new List<string>();

            foreach (var pagina in PaginasComRota())
            {
                var nome = pagina.Tipo.Name;
                if (SemPolitica.Contains(nome)) continue;

                var politica = pagina.Tipo
                    .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Select(a => a.Policy)
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

                if (politica == null)
                {
                    problemas.Add($"{nome} ({pagina.Rota}): sem [Authorize(Policy = ...)]");
                    continue;
                }

                var esperada = Catalogo.PorRota(pagina.Rota);

                if (esperada == null)
                {
                    problemas.Add($"{nome}: a rota {pagina.Rota} nao esta no catalogo");
                    continue;
                }

                if (!politica.StartsWith($"{Politicas.Prefixo}{esperada.Chave}:", StringComparison.Ordinal))
                {
                    problemas.Add(
                        $"{nome} ({pagina.Rota}): exige '{politica}', "
                        + $"mas pertence a tela '{esperada.Chave}'");
                }
            }

            Assert.True(problemas.Count == 0, string.Join("\n", problemas));
        }

        /// <summary>
        /// As paginas publicas precisam continuar publicas: um
        /// [AllowAnonymous] removido por engano deixaria o login atras do
        /// proprio login.
        /// </summary>
        [Fact]
        public void Entrada_e_saida_continuam_publicas()
        {
            foreach (var nome in new[] { "Login", "Logout", "Error" })
            {
                var pagina = PaginasComRota().FirstOrDefault(p => p.Tipo.Name == nome);

                Assert.True(pagina.Tipo != null, $"Pagina {nome} nao encontrada.");
                Assert.NotEmpty(
                    pagina.Tipo!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            }
        }

        [Fact]
        public void Toda_tela_do_catalogo_tem_constante_de_politica()
        {
            var constantes = typeof(Politicas)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            foreach (var tela in Catalogo.Telas)
            {
                Assert.Contains(Politicas.Para(tela.Chave, Acao.Ver), constantes);
            }
        }

        [Fact]
        public void Nome_de_politica_aponta_para_tela_e_acao_existentes()
        {
            var constantes = typeof(Politicas)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                // O proprio Prefixo e uma constante e comeca com ele mesmo.
                .Where(v => v != Politicas.Prefixo
                            && v.StartsWith(Politicas.Prefixo, StringComparison.Ordinal));

            foreach (var nome in constantes)
            {
                var partes = nome[Politicas.Prefixo.Length..].Split(':');

                Assert.Equal(2, partes.Length);
                Assert.NotNull(Catalogo.Por(partes[0]));
                Assert.True(Enum.TryParse<Acao>(partes[1], out var acao));
                Assert.True(Catalogo.Por(partes[0])!.Tem(acao),
                    $"A politica {nome} pede uma acao que a tela nao declara.");
            }
        }

        private static IEnumerable<(Type Tipo, string Rota)> PaginasComRota()
        {
            return typeof(Catalogo).Assembly.GetTypes()
                .Where(t => typeof(IComponent).IsAssignableFrom(t))
                .SelectMany(t => t.GetCustomAttributes<RouteAttribute>(false)
                    .Select(r => (Tipo: t, Rota: r.Template)));
        }
    }
}
