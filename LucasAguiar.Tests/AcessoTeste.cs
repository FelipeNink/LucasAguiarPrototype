using LucasAguiar.Configs;
using LucasAguiar.Data;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Sessao, bloqueio por tentativas e login contra um MySQL de verdade.
    /// Ver <see cref="BancoDeTeste"/> para como habilitar.
    /// </summary>
    public class AcessoTeste
    {
        // =========================================================
        // SESSAO
        // =========================================================

        [FatoComBanco]
        public void Sessao_criada_vale_e_traz_o_dono()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = BancoDeTeste.CriarUsuario(conexao, BancoDeTeste.NomeUnico("sessao"));

            try
            {
                var dao = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = dao.Criar(id, "10.0.0.1", "Teste/1.0");

                var acesso = dao.Validar(token);

                Assert.NotNull(acesso);
                Assert.Equal(id, acesso!.IdUsuario);
                Assert.False(acesso.EhAdmin);
                Assert.True(acesso.ExpiraEm > DateTime.Now.AddHours(7));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Token_desconhecido_nao_vale()
        {
            var dao = new SessaoDAO(BancoDeTeste.Fabricar());

            Assert.Null(dao.Validar(SegurancaSenha.TokenNovo()));
            Assert.Null(dao.Validar(""));
            Assert.Null(dao.Validar(null));
        }

        [FatoComBanco]
        public void Encerrar_derruba_a_sessao_na_hora()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = BancoDeTeste.CriarUsuario(conexao, BancoDeTeste.NomeUnico("sair"));

            try
            {
                var dao = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = dao.Criar(id, null, null);

                Assert.NotNull(dao.Validar(token));

                dao.Encerrar(token);

                Assert.Null(dao.Validar(token));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        /// <summary>
        /// A falha que o sistema tinha: desativar alguem deixava a sessao
        /// dele de pe. Aqui a sessao continua existindo e mesmo assim
        /// deixa de valer, porque o "ativo" e conferido na mesma consulta.
        /// </summary>
        [FatoComBanco]
        public void Desativar_o_usuario_invalida_a_sessao_que_ja_estava_aberta()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = BancoDeTeste.CriarUsuario(conexao, BancoDeTeste.NomeUnico("inativo"));

            try
            {
                var dao = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = dao.Criar(id, null, null);

                Assert.NotNull(dao.Validar(token));

                using (var desativar = new MySqlConnector.MySqlCommand(
                    "UPDATE usuario SET ativo_usr = 0 WHERE id_usr = @id", conexao))
                {
                    desativar.Parameters.AddWithValue("@id", id);
                    desativar.ExecuteNonQuery();
                }

                Assert.Null(dao.Validar(token));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Sessao_vencida_nao_vale()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = BancoDeTeste.CriarUsuario(conexao, BancoDeTeste.NomeUnico("vencida"));

            try
            {
                var dao = new SessaoDAO(BancoDeTeste.Fabricar());
                var token = dao.Criar(id, null, null);

                using (var vencer = new MySqlConnector.MySqlCommand(
                    @"UPDATE sessao SET expira_em = NOW() - INTERVAL 1 MINUTE
                       WHERE id_usr_fk = @id", conexao))
                {
                    vencer.Parameters.AddWithValue("@id", id);
                    vencer.ExecuteNonQuery();
                }

                Assert.Null(dao.Validar(token));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Encerrar_todas_preserva_a_sessao_de_quem_pediu()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = BancoDeTeste.CriarUsuario(conexao, BancoDeTeste.NomeUnico("varias"));

            try
            {
                var dao = new SessaoDAO(BancoDeTeste.Fabricar());

                var minha = dao.Criar(id, "10.0.0.1", "Aqui");
                var outra = dao.Criar(id, "10.0.0.2", "Outro aparelho");
                var terceira = dao.Criar(id, "10.0.0.3", "Mais um");

                var derrubadas = dao.EncerrarTodasDoUsuario(id, minha);

                Assert.Equal(2, derrubadas);
                Assert.NotNull(dao.Validar(minha));
                Assert.Null(dao.Validar(outra));
                Assert.Null(dao.Validar(terceira));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // BLOQUEIO POR TENTATIVAS
        // =========================================================

        [FatoComBanco]
        public void Bloqueia_na_quinta_falha_e_libera_quem_acerta()
        {
            using var conexao = BancoDeTeste.Abrir();

            var chave = "usr:" + BancoDeTeste.NomeUnico("forca");
            var dao = new TentativaLoginDAO(BancoDeTeste.Fabricar());

            try
            {
                Assert.Null(dao.BloqueadoAte(chave));

                for (var i = 1; i < TentativaLoginDAO.MaximoFalhas; i++)
                {
                    Assert.Null(dao.RegistrarFalha(chave));
                    Assert.Null(dao.BloqueadoAte(chave));
                }

                var bloqueio = dao.RegistrarFalha(chave);

                Assert.NotNull(bloqueio);
                Assert.NotNull(dao.BloqueadoAte(chave));
                Assert.InRange(TentativaLoginDAO.MinutosRestantes(bloqueio!.Value), 1, 15);

                // Entrar com acerto zera a contagem.
                dao.Zerar(chave);
                Assert.Null(dao.BloqueadoAte(chave));
            }
            finally
            {
                BancoDeTeste.ApagarTentativas(conexao, chave);
            }
        }

        /// <summary>
        /// As duas chaves sao independentes: bloquear um nome de usuario
        /// nao pode barrar todo mundo que vem do mesmo lugar, nem o
        /// contrario.
        /// </summary>
        [FatoComBanco]
        public void Chaves_de_usuario_e_de_ip_contam_separado()
        {
            using var conexao = BancoDeTeste.Abrir();

            var doUsuario = "usr:" + BancoDeTeste.NomeUnico("alvo");
            var doIp = "ip:" + BancoDeTeste.NomeUnico("198.51.100");
            var dao = new TentativaLoginDAO(BancoDeTeste.Fabricar());

            try
            {
                for (var i = 0; i < TentativaLoginDAO.MaximoFalhas; i++)
                    dao.RegistrarFalha(doUsuario);

                Assert.NotNull(dao.BloqueadoAte(doUsuario));
                Assert.Null(dao.BloqueadoAte(doIp));
            }
            finally
            {
                BancoDeTeste.ApagarTentativas(conexao, doUsuario, doIp);
            }
        }

        [FatoComBanco]
        public void Contagem_reinicia_quando_a_janela_passa()
        {
            using var conexao = BancoDeTeste.Abrir();

            var chave = "usr:" + BancoDeTeste.NomeUnico("antigo");
            var dao = new TentativaLoginDAO(BancoDeTeste.Fabricar());

            try
            {
                for (var i = 0; i < TentativaLoginDAO.MaximoFalhas - 1; i++)
                    dao.RegistrarFalha(chave);

                // Envelhece a ultima falha para alem da janela.
                using (var envelhecer = new MySqlConnector.MySqlCommand(
                    @"UPDATE tentativa_login
                         SET atualizado_em = NOW() - INTERVAL 1 HOUR
                       WHERE chave = @chave", conexao))
                {
                    envelhecer.Parameters.AddWithValue("@chave", chave);
                    envelhecer.ExecuteNonQuery();
                }

                // A proxima falha recomeca do 1, e nao bloqueia.
                Assert.Null(dao.RegistrarFalha(chave));
                Assert.Null(dao.BloqueadoAte(chave));
            }
            finally
            {
                BancoDeTeste.ApagarTentativas(conexao, chave);
            }
        }

        // =========================================================
        // LOGIN
        // =========================================================

        [FatoComBanco]
        public void Login_certo_entra_e_o_errado_nao()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("login");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                Assert.True(dao.ValidarLogin(nome, "barbearia10").Entrou);

                var errada = dao.ValidarLogin(nome, "barbearia11");
                Assert.False(errada.Entrou);
                Assert.Equal(UsuarioDAO.Resultado.SenhaErrada, errada.Desfecho);

                var inexistente = dao.ValidarLogin(nome + "zzz", "barbearia10");
                Assert.False(inexistente.Entrou);
                Assert.Equal(UsuarioDAO.Resultado.NaoEncontrado, inexistente.Desfecho);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Nome_de_usuario_nao_diferencia_maiusculas()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("Caixa");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                Assert.True(dao.ValidarLogin(nome.ToUpperInvariant(), "barbearia10").Entrou);
                Assert.True(dao.ValidarLogin(nome.ToLowerInvariant(), "barbearia10").Entrou);
                Assert.True(dao.ValidarLogin("  " + nome + "  ", "barbearia10").Entrou);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        /// <summary>
        /// Conta desativada so e revelada depois de a senha estar certa.
        /// Com a senha errada, a resposta e a mesma de qualquer outro erro.
        /// </summary>
        [FatoComBanco]
        public void Conta_desativada_so_aparece_com_a_senha_certa()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("desativado");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10", ativo: false);

            try
            {
                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                Assert.Equal(UsuarioDAO.Resultado.Desativado,
                    dao.ValidarLogin(nome, "barbearia10").Desfecho);

                Assert.Equal(UsuarioDAO.Resultado.SenhaErrada,
                    dao.ValidarLogin(nome, "errada123").Desfecho);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        /// <summary>
        /// O teste que garante que ninguem perdeu o acesso na troca de
        /// algoritmo: entra com a senha antiga e, dali em diante, a linha
        /// esta em pbkdf2 -- e a mesma senha continua entrando.
        /// </summary>
        [FatoComBanco]
        public void Senha_antiga_entra_e_e_regravada_no_formato_novo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("legado");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                BancoDeTeste.GravarSenhaAntiga(conexao, id, "senhaantiga1");

                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                var primeira = dao.ValidarLogin(nome, "senhaantiga1");
                Assert.True(primeira.Entrou);

                Assert.Equal(SegurancaSenha.Atual, AlgoritmoDe(conexao, id));

                // A mesma senha continua valendo depois da regravacao.
                Assert.True(dao.ValidarLogin(nome, "senhaantiga1").Entrou);
                Assert.False(dao.ValidarLogin(nome, "senhaantiga2").Entrou);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        [FatoComBanco]
        public void Login_marca_o_ultimo_acesso()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("ultimo");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                Assert.Null(dao.BuscarPorId(id)!.UltimoLoginUsr);

                dao.ValidarLogin(nome, "barbearia10");

                Assert.NotNull(dao.BuscarPorId(id)!.UltimoLoginUsr);
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        // =========================================================
        // TROCA DA PROPRIA SENHA
        // =========================================================

        [FatoComBanco]
        public void Trocar_a_propria_senha_exige_a_atual_e_derruba_as_outras_sessoes()
        {
            using var conexao = BancoDeTeste.Abrir();
            var nome = BancoDeTeste.NomeUnico("troca");
            var id = BancoDeTeste.CriarUsuario(conexao, nome, "barbearia10");

            try
            {
                var sessoes = new SessaoDAO(BancoDeTeste.Fabricar());
                var dao = new UsuarioDAO(BancoDeTeste.Fabricar());

                var minha = sessoes.Criar(id, null, "Aqui");
                var outra = sessoes.Criar(id, null, "Outro aparelho");

                // Senha atual errada nao troca nada.
                Assert.Throws<Exception>(() =>
                    dao.AlterarPropriaSenha(id, "errada999", "barbearia20", minha));

                // Nem senha nova fora da politica.
                Assert.Throws<Exception>(() =>
                    dao.AlterarPropriaSenha(id, "barbearia10", "curta", minha));

                // Nem repetir a senha atual.
                Assert.Throws<Exception>(() =>
                    dao.AlterarPropriaSenha(id, "barbearia10", "barbearia10", minha));

                Assert.True(dao.ValidarLogin(nome, "barbearia10").Entrou);

                dao.AlterarPropriaSenha(id, "barbearia10", "barbearia20", minha);

                Assert.True(dao.ValidarLogin(nome, "barbearia20").Entrou);
                Assert.False(dao.ValidarLogin(nome, "barbearia10").Entrou);

                Assert.NotNull(sessoes.Validar(minha));
                Assert.Null(sessoes.Validar(outra));
            }
            finally
            {
                BancoDeTeste.ApagarUsuario(conexao, id);
            }
        }

        private static string AlgoritmoDe(MySqlConnector.MySqlConnection conexao, int id)
        {
            using var comando = new MySqlConnector.MySqlCommand(
                "SELECT algo_senha_usr FROM usuario WHERE id_usr = @id", conexao);

            comando.Parameters.AddWithValue("@id", id);
            return Convert.ToString(comando.ExecuteScalar()) ?? "";
        }
    }
}
