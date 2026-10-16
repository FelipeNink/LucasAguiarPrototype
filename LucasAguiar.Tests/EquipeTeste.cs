using LucasAguiar.Configs;
using LucasAguiar.Data;
using LucasAguiar.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// As protecoes da Equipe, contra um MySQL de verdade.
    ///
    /// Sao as regras cuja falha e cara e silenciosa: um sistema sem
    /// administrador ativo so se conserta escrevendo no banco a mao, e um
    /// administrador comum que consiga se promover anula o conceito de
    /// super-administrador. Cada uma tem teste proprio, inclusive o caso
    /// negado.
    /// </summary>
    public class EquipeTeste
    {
        private static EquipeDAO Dao() =>
            new(BancoDeTeste.Fabricar(),
                new LogDAO(BancoDeTeste.Fabricar(), NullLogger<LogDAO>.Instance));

        private static Autor Raiz(int id) => new(id, "raiz-teste", true, true);
        private static Autor Admin(int id) => new(id, "admin-teste", true, false);

        /// <summary>Matriz com Acessar em todas as telas.</summary>
        private static Dictionary<string, Acao> TudoVisivel() =>
            Catalogo.Telas.ToDictionary(t => t.Chave, _ => Acao.Ver);

        // =========================================================
        // CRIAR
        // =========================================================

        [FatoComBanco]
        public void Criar_grava_usuario_e_matriz_na_mesma_operacao()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("novo");

            var id = dao.Criar(
                new Usuario { NomeUsr = nome, PerfilUsr = Perfil.Comum },
                "barbearia10",
                new Dictionary<string, Acao> { ["clientes"] = Acao.Ver | Acao.Criar },
                exigirTrocaDeSenha: true,
                Raiz(0));

            try
            {
                var criado = dao.BuscarPorId(id)!;

                Assert.Equal(nome.ToLowerInvariant(), criado.NomeUsr);
                Assert.True(criado.AtivoUsr);
                Assert.True(criado.TrocarSenhaUsr);
                Assert.False(criado.EhAdmin);

                var permissoes = new PermissaoDAO(BancoDeTeste.Fabricar())
                    .Carregar(id, false);

                Assert.True(permissoes.Pode("clientes", Acao.Criar));
                Assert.False(permissoes.PodeVer("vendas"));

                // A senha definida entra de verdade.
                Assert.True(new UsuarioDAO(BancoDeTeste.Fabricar())
                    .ValidarLogin(nome, "barbearia10").Entrou);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Criar_recusa_usuario_repetido_sem_diferenciar_maiusculas()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("repetido");

            var id = dao.Criar(
                new Usuario { NomeUsr = nome }, "barbearia10",
                TudoVisivel(), false, Raiz(0));

            try
            {
                var erro = Assert.Throws<Exception>(() => dao.Criar(
                    new Usuario { NomeUsr = nome.ToUpperInvariant() }, "barbearia10",
                    TudoVisivel(), false, Raiz(0)));

                Assert.Contains("já está cadastrado", erro.Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Criar_aplica_a_politica_de_senha()
        {
            var dao = Dao();

            Assert.Throws<Exception>(() => dao.Criar(
                new Usuario { NomeUsr = BancoDeTeste.NomeUnico("fraca") }, "123",
                TudoVisivel(), false, Raiz(0)));
        }

        /// <summary>Protecao 4: so a raiz cria administrador.</summary>
        [FatoComBanco]
        public void Admin_comum_nao_cria_administrador()
        {
            var dao = Dao();

            var erro = Assert.Throws<Exception>(() => dao.Criar(
                new Usuario { NomeUsr = BancoDeTeste.NomeUnico("promovido"), PerfilUsr = Perfil.Admin },
                "barbearia10", TudoVisivel(), false, Admin(0)));

            Assert.Contains("super-administrador", erro.Message);
        }

        // =========================================================
        // EDITAR
        // =========================================================

        /// <summary>
        /// A falha do sistema de referencia que nao podia ser repetida:
        /// salvar o formulario reativava quem estava desativado.
        /// </summary>
        [FatoComBanco]
        public void Editar_nao_reativa_quem_esta_desativado()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("inativo");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10", ativo: false);

            try
            {
                dao.Atualizar(
                    new Usuario { IdUsr = id, NomeUsr = nome, EmailUsr = "novo@teste.local" },
                    null, Raiz(0));

                Assert.False(dao.BuscarPorId(id)!.AtivoUsr);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Editar_avisa_quando_outra_pessoa_alterou_antes()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("corrida");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                // Carimbo antigo: simula a tela aberta ha algum tempo.
                var carimboVelho = DateTime.Now.AddMinutes(-10);

                var erro = Assert.Throws<Exception>(() => dao.Atualizar(
                    new Usuario { IdUsr = id, NomeUsr = nome }, carimboVelho, Raiz(0)));

                Assert.Contains("alterado por outra pessoa", erro.Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Editar_recusa_nome_de_outro_membro()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();

            var um = BancoDeTeste.NomeUnico("um");
            var outro = BancoDeTeste.NomeUnico("outro");
            var idUm = BancoDeTeste.CriarUsuario(conexao, um, "barbearia10");
            var idOutro = BancoDeTeste.CriarUsuario(conexao, outro, "barbearia10");

            try
            {
                Assert.Throws<Exception>(() => dao.Atualizar(
                    new Usuario { IdUsr = idOutro, NomeUsr = um }, null, Raiz(0)));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, idUm);
                BancoDeTeste.ApagarUsuario(conexao, idOutro);
            }
        }

        // =========================================================
        // ATIVAR E DESATIVAR
        // =========================================================

        [FatoComBanco]
        public void Desativar_encerra_as_sessoes_do_alvo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("sessoes"), "barbearia10");

            try
            {
                var sessoes = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = sessoes.Criar(id, null, null);

                Assert.NotNull(sessoes.Validar(token));

                dao.AlterarStatus(id, ativar: false, Raiz(0));

                Assert.Null(sessoes.Validar(token));
                Assert.False(dao.BuscarPorId(id)!.AtivoUsr);

                dao.AlterarStatus(id, ativar: true, Raiz(0));

                Assert.True(dao.BuscarPorId(id)!.AtivoUsr);

                // Reativar nao pode ressuscitar a sessao antiga.
                Assert.Null(sessoes.Validar(token));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        /// <summary>Protecao 1.</summary>
        [FatoComBanco]
        public void Ninguem_desativa_a_si_mesmo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("eu"), "barbearia10");

            try
            {
                var erro = Assert.Throws<Exception>(() =>
                    dao.AlterarStatus(id, false, new Autor(id, "eu", true, true)));

                Assert.Contains("a si mesmo", erro.Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        /// <summary>Protecao 2.</summary>
        [FatoComBanco]
        public void Raiz_nao_pode_ser_desativada_nem_excluida()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("raiz"), "barbearia10", perfil: Perfil.Admin);

            MarcarRaiz(conexao, id);

            try
            {
                Assert.Contains("super-administrador",
                    Assert.Throws<Exception>(() => dao.AlterarStatus(id, false, Raiz(0))).Message);

                Assert.Contains("super-administrador",
                    Assert.Throws<Exception>(() => dao.Excluir(id, Raiz(0))).Message);

                Assert.Contains("super-administrador",
                    Assert.Throws<Exception>(() =>
                        dao.AlterarPerfil(id, Perfil.Comum, Raiz(0))).Message);
            }
            finally
            {
                DesmarcarRaiz(conexao, id);
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // EXCLUIR
        // =========================================================

        [FatoComBanco]
        public void Excluir_apaga_o_acesso_e_as_permissoes_dele()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("sumir");

            var id = dao.Criar(
                new Usuario { NomeUsr = nome }, "barbearia10",
                TudoVisivel(), false, Raiz(0));

            Assert.NotEmpty(new PermissaoDAO(BancoDeTeste.Fabricar()).Ler(id));

            dao.Excluir(id, Raiz(0));

            Assert.Null(dao.BuscarPorId(id));
            Assert.Empty(new PermissaoDAO(BancoDeTeste.Fabricar()).Ler(id));
        }

        [FatoComBanco]
        public void Ninguem_exclui_a_si_mesmo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("eu2"), "barbearia10");

            try
            {
                Assert.Contains("a si mesmo",
                    Assert.Throws<Exception>(() =>
                        dao.Excluir(id, new Autor(id, "eu", true, true))).Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // ULTIMO ADMINISTRADOR  (protecao 3)
        // =========================================================

        /// <summary>
        /// O cenario que trava o sistema inteiro: sem administrador ativo,
        /// ninguem mais cria usuario nem concede permissao. As tres portas
        /// -- desativar, excluir e rebaixar -- precisam estar fechadas.
        /// </summary>
        [FatoComBanco]
        public void Nao_deixa_o_sistema_sem_administrador_ativo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();

            // Tira os administradores reais de cena para que o do teste
            // seja mesmo o ultimo, e devolve tudo no fim.
            var suspensos = SuspenderAdmins(conexao);
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("unico"), "barbearia10", perfil: Perfil.Admin);

            try
            {
                var outro = new Autor(0, "outro-admin", true, true);

                Assert.Contains("ao menos um administrador ativo",
                    Assert.Throws<Exception>(() => dao.AlterarStatus(id, false, outro)).Message);

                Assert.Contains("ao menos um administrador ativo",
                    Assert.Throws<Exception>(() => dao.Excluir(id, outro)).Message);

                Assert.Contains("ao menos um administrador ativo",
                    Assert.Throws<Exception>(() =>
                        dao.AlterarPerfil(id, Perfil.Comum, outro)).Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
                Restaurar(conexao, suspensos);
            }
        }

        [FatoComBanco]
        public void Com_dois_administradores_desativar_um_e_permitido()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();

            var suspensos = SuspenderAdmins(conexao);
            var um = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("adm1"), "barbearia10", perfil: Perfil.Admin);
            var dois = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("adm2"), "barbearia10", perfil: Perfil.Admin);

            try
            {
                dao.AlterarStatus(um, false, new Autor(dois, "adm2", true, true));

                Assert.False(dao.BuscarPorId(um)!.AtivoUsr);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, um);
                BancoDeTeste.ApagarUsuario(conexao, dois);
                Restaurar(conexao, suspensos);
            }
        }

        // =========================================================
        // PERFIL  (protecao 4)
        // =========================================================

        [FatoComBanco]
        public void Admin_comum_nao_promove_ninguem()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("comum"), "barbearia10");

            try
            {
                Assert.Contains("super-administrador",
                    Assert.Throws<Exception>(() =>
                        dao.AlterarPerfil(id, Perfil.Admin, Admin(0))).Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Promover_apaga_a_matriz_e_rebaixar_devolve_tudo_negado()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var permissoes = new PermissaoDAO(BancoDeTeste.Fabricar());

            var id = dao.Criar(
                new Usuario { NomeUsr = BancoDeTeste.NomeUnico("subiu") }, "barbearia10",
                new Dictionary<string, Acao> { ["clientes"] = Acao.Ver | Acao.Criar },
                false, Raiz(0));

            try
            {
                dao.AlterarPerfil(id, Perfil.Admin, Raiz(0));

                Assert.True(dao.BuscarPorId(id)!.EhAdmin);
                Assert.Empty(permissoes.Ler(id));
                Assert.True(permissoes.Carregar(id, true).PodeVer("vendas"));

                dao.AlterarPerfil(id, Perfil.Comum, Raiz(0));

                // Volta com todas as telas presentes e todas negadas: o
                // ex-administrador nao herda acesso nenhum.
                var depois = permissoes.Ler(id);

                Assert.Equal(Catalogo.Telas.Count, depois.Count);
                Assert.All(depois.Values, a => Assert.Equal(Acao.Nenhuma, a));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // SENHA REDEFINIDA PELO ADMINISTRADOR
        // =========================================================

        [FatoComBanco]
        public void Redefinir_senha_derruba_as_sessoes_e_obriga_a_trocar()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var nome = BancoDeTeste.NomeUnico("redefinir");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                var sessoes = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = sessoes.Criar(id, null, null);

                dao.RedefinirSenha(id, "barbearia20", exigirTroca: true, Raiz(0));

                Assert.Null(sessoes.Validar(token));

                var usuarios = new UsuarioDAO(BancoDeTeste.Fabricar());

                Assert.False(usuarios.ValidarLogin(nome, "barbearia10").Entrou);
                Assert.True(usuarios.ValidarLogin(nome, "barbearia20").Entrou);
                Assert.True(dao.BuscarPorId(id)!.TrocarSenhaUsr);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // MATRIZ
        // =========================================================

        [FatoComBanco]
        public void Salvar_matriz_zera_as_acoes_de_quem_perdeu_o_acessar()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var permissoes = new PermissaoDAO(BancoDeTeste.Fabricar());

            var id = dao.Criar(
                new Usuario { NomeUsr = BancoDeTeste.NomeUnico("matriz") }, "barbearia10",
                new Dictionary<string, Acao>
                {
                    ["clientes"] = Acao.Ver | Acao.Criar | Acao.Editar
                },
                false, Raiz(0));

            try
            {
                Assert.True(permissoes.Carregar(id, false).Pode("clientes", Acao.Editar));

                // Tira o Acessar e mantem Criar marcado: o servidor zera.
                dao.SalvarPermissoes(id,
                    new Dictionary<string, Acao> { ["clientes"] = Acao.Criar }, Raiz(0));

                var depois = permissoes.Carregar(id, false);

                Assert.False(depois.PodeVer("clientes"));
                Assert.False(depois.Pode("clientes", Acao.Criar));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Matriz_nao_se_aplica_a_administrador()
        {
            using var conexao = BancoDeTeste.Abrir();
            var dao = Dao();
            var id = BancoDeTeste.CriarUsuario(
                conexao, BancoDeTeste.NomeUnico("adm"), "barbearia10", perfil: Perfil.Admin);

            try
            {
                Assert.Contains("acesso total",
                    Assert.Throws<Exception>(() => dao.SalvarPermissoes(
                        id, new Dictionary<string, Acao>(), Raiz(0))).Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // FUNCOES
        // =========================================================

        [FatoComBanco]
        public void Funcao_renomeada_vale_para_quem_esta_ligado_a_ela()
        {
            using var conexao = BancoDeTeste.Abrir();
            var funcoes = new FuncaoDAO(BancoDeTeste.Fabricar(),
                new LogDAO(BancoDeTeste.Fabricar(), NullLogger<LogDAO>.Instance));
            var equipe = Dao();

            var idFuncao = funcoes.Criar(BancoDeTeste.NomeUnico("Barbeiro"), Raiz(0));
            var idUsuario = equipe.Criar(
                new Usuario { NomeUsr = BancoDeTeste.NomeUnico("ligado"), IdFuncao = idFuncao },
                "barbearia10", TudoVisivel(), false, Raiz(0));

            try
            {
                var novo = BancoDeTeste.NomeUnico("Barbeiro-senior");
                funcoes.Renomear(idFuncao, novo, Raiz(0));

                // O vinculo e por id: o nome novo aparece sem tocar no usuario.
                Assert.Equal(novo, equipe.BuscarPorId(idUsuario)!.NomeFuncao);

                // E excluir uma funcao em uso e barrado, dizendo quantos usam.
                Assert.Contains("em uso por 1 membro",
                    Assert.Throws<Exception>(() => funcoes.Excluir(idFuncao, Raiz(0))).Message);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, idUsuario);
                funcoes.Excluir(idFuncao, Raiz(0));
            }
        }

        [FatoComBanco]
        public void Funcao_repetida_e_recusada_sem_diferenciar_maiusculas()
        {
            var funcoes = new FuncaoDAO(BancoDeTeste.Fabricar(),
                new LogDAO(BancoDeTeste.Fabricar(), NullLogger<LogDAO>.Instance));

            var nome = BancoDeTeste.NomeUnico("Recepcao");
            var id = funcoes.Criar(nome, Raiz(0));

            try
            {
                Assert.Contains("Já existe a função",
                    Assert.Throws<Exception>(() =>
                        funcoes.Criar(nome.ToUpperInvariant(), Raiz(0))).Message);
            }
            finally
            {
                funcoes.Excluir(id, Raiz(0));
            }
        }

        // ---------- apoio ----------

        private static void MarcarRaiz(MySqlConnection conexao, int id) =>
            Executar(conexao, "UPDATE usuario SET raiz_usr = 1 WHERE id_usr = @id", id);

        private static void DesmarcarRaiz(MySqlConnection conexao, int id) =>
            Executar(conexao, "UPDATE usuario SET raiz_usr = 0 WHERE id_usr = @id", id);

        /// <summary>
        /// Desativa os administradores reais do banco para o teste poder
        /// criar "o ultimo". Devolve os ids para restaurar depois.
        /// </summary>
        private static List<int> SuspenderAdmins(MySqlConnection conexao)
        {
            var ids = new List<int>();

            using (var ler = new MySqlCommand(
                "SELECT id_usr FROM usuario WHERE perfil_usr = 'ADMIN' AND ativo_usr = 1",
                conexao))
            using (var leitor = ler.ExecuteReader())
            {
                while (leitor.Read()) ids.Add(leitor.GetInt32("id_usr"));
            }

            foreach (var id in ids)
                Executar(conexao, "UPDATE usuario SET ativo_usr = 0 WHERE id_usr = @id", id);

            return ids;
        }

        private static void Restaurar(MySqlConnection conexao, List<int> ids)
        {
            foreach (var id in ids)
                Executar(conexao, "UPDATE usuario SET ativo_usr = 1 WHERE id_usr = @id", id);
        }

        private static void Executar(MySqlConnection conexao, string sql, int id)
        {
            using var comando = new MySqlCommand(sql, conexao);
            comando.Parameters.AddWithValue("@id", id);
            comando.ExecuteNonQuery();
        }
    }
}
