using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class UsuarioDAO
    {
        private readonly Conexao _conexao;

        public UsuarioDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Traz as colunas que <see cref="SessaoDAO.LerUsuario"/> espera,
        /// inclusive o nome da funcao e o do profissional ligado.
        /// </summary>
        internal const string SelecaoPadrao = @"
            SELECT u.id_usr, u.nome_usr, u.email_usr, u.senha_usr, u.ativo_usr,
                   u.data_criacao_usr, u.perfil_usr, u.raiz_usr,
                   u.trocar_senha_usr, u.algo_senha_usr,
                   u.id_funcao_fk, f.nome_funcao,
                   u.id_fun_fk, fun.nome_fun,
                   u.ultimo_login_usr, u.atualizado_usr
              FROM usuario u
              LEFT JOIN funcao f ON f.id_funcao = u.id_funcao_fk
              LEFT JOIN funcionario fun ON fun.id_fun = u.id_fun_fk";

        // =============================================================
        // LOGIN
        // =============================================================

        public enum Resultado
        {
            Ok,
            NaoEncontrado,
            SenhaErrada,
            Desativado
        }

        /// <summary>
        /// O desfecho da tentativa. O motivo detalhado existe para o log;
        /// a tela mostra sempre a mesma frase.
        /// </summary>
        public record Tentativa(Resultado Desfecho, Usuario? Usuario)
        {
            public bool Entrou => Desfecho == Resultado.Ok && Usuario != null;

            /// <summary>O que vai para o log, nunca para a tela.</summary>
            public string Motivo => Desfecho switch
            {
                Resultado.NaoEncontrado => "Usuário inexistente.",
                Resultado.SenhaErrada => "Senha incorreta.",
                Resultado.Desativado => "Conta desativada.",
                _ => "Entrou."
            };
        }

        /// <summary>
        /// Confere usuario e senha.
        ///
        /// Tres coisas de proposito, e cada uma corrige um problema:
        ///
        /// 1. Usuario inexistente tambem paga o custo da conferencia de
        ///    senha. Sem isso o tempo de resposta diria quais logins
        ///    existem, mesmo com a mensagem de erro sendo igual.
        /// 2. A conta desativada e detectada DEPOIS de a senha estar
        ///    certa. Avisar antes contaria a quem errou a senha que aquele
        ///    usuario existe.
        /// 3. Senha no formato antigo e regravada em PBKDF2 aqui, no unico
        ///    instante em que a senha em texto existe. E o que permitiu
        ///    trocar o algoritmo sem invalidar as senhas de ninguem.
        /// </summary>
        public Tentativa ValidarLogin(string? nomeUsuario, string? senha)
        {
            if (string.IsNullOrWhiteSpace(nomeUsuario) || string.IsNullOrWhiteSpace(senha))
                return new Tentativa(Resultado.NaoEncontrado, null);

            try
            {
                using var conexao = _conexao.GetConnection();

                Usuario? usuario = null;
                string? gravada = null;

                // Sem COLLATE: a tabela compara sem diferenciar maiusculas, e
                // o indice unico uk_usuario_nome usa a mesma collation --
                // entao "Admin" e "admin" nunca sao dois usuarios diferentes,
                // e tambem nao precisam ser digitados igual.
                using (var comando = new MySqlCommand(
                    SelecaoPadrao + " WHERE u.nome_usr = @nome LIMIT 1", conexao))
                {
                    comando.Parameters.AddWithValue("@nome", nomeUsuario.Trim());

                    using var leitor = comando.ExecuteReader();
                    if (leitor.Read())
                    {
                        usuario = SessaoDAO.LerUsuario(leitor);
                        gravada = DAOHelper.GetString(leitor, "senha_usr");
                    }
                }

                if (usuario == null)
                {
                    SegurancaSenha.GastarTempoDeConferencia();
                    return new Tentativa(Resultado.NaoEncontrado, null);
                }

                if (!SegurancaSenha.Confere(senha, gravada, usuario.AlgoSenhaUsr))
                    return new Tentativa(Resultado.SenhaErrada, usuario);

                if (!usuario.AtivoUsr)
                    return new Tentativa(Resultado.Desativado, usuario);

                if (SegurancaSenha.PrecisaRegravar(usuario.AlgoSenhaUsr))
                {
                    RegravarSenha(conexao, usuario.IdUsr, senha);
                    usuario.AlgoSenhaUsr = SegurancaSenha.Atual;
                }

                MarcarLogin(conexao, usuario.IdUsr);
                return new Tentativa(Resultado.Ok, usuario);
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao validar login: " + ex.Message);
            }
        }

        /// <summary>
        /// Passa a senha para o formato novo sem a pessoa perceber. Nao
        /// mexe em mais nada da linha.
        /// </summary>
        private static void RegravarSenha(MySqlConnection conexao, int idUsuario, string senha)
        {
            using var comando = new MySqlCommand(
                @"UPDATE usuario
                     SET senha_usr = @senha, algo_senha_usr = @algo
                   WHERE id_usr = @id", conexao);

            comando.Parameters.AddWithValue("@senha", SegurancaSenha.Gerar(senha));
            comando.Parameters.AddWithValue("@algo", SegurancaSenha.Atual);
            comando.Parameters.AddWithValue("@id", idUsuario);

            comando.ExecuteNonQuery();
        }

        private static void MarcarLogin(MySqlConnection conexao, int idUsuario)
        {
            using var comando = new MySqlCommand(
                "UPDATE usuario SET ultimo_login_usr = NOW() WHERE id_usr = @id", conexao);

            comando.Parameters.AddWithValue("@id", idUsuario);
            comando.ExecuteNonQuery();
        }

        // =============================================================
        // LEITURA
        // =============================================================

        public Usuario? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + " WHERE u.id_usr = @id LIMIT 1", conexao);

            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? SessaoDAO.LerUsuario(leitor) : null;
        }

        public Usuario? BuscarPorNome(string? nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return null;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                SelecaoPadrao + " WHERE u.nome_usr = @nome LIMIT 1", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? SessaoDAO.LerUsuario(leitor) : null;
        }

        // =============================================================
        // SENHA PROPRIA
        // =============================================================

        /// <summary>
        /// Troca da propria senha: exige a senha atual, aplica a politica e
        /// derruba as outras sessoes -- tudo numa transacao, para nao
        /// existir o estado em que a senha mudou e a sessao antiga
        /// continua de pe.
        ///
        /// <paramref name="tokenAtual"/> e a sessao de quem esta trocando,
        /// que e a unica preservada.
        /// </summary>
        public void AlterarPropriaSenha(
            int idUsuario, string? senhaAtual, string? senhaNova, string? tokenAtual)
        {
            var critica = SegurancaSenha.Criticar(senhaNova);
            if (critica != null) throw new Exception(critica);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            string? gravada;
            string? algoritmo;

            using (var atual = new MySqlCommand(
                @"SELECT senha_usr, algo_senha_usr FROM usuario
                   WHERE id_usr = @id FOR UPDATE", conexao, transacao))
            {
                atual.Parameters.AddWithValue("@id", idUsuario);

                using var leitor = atual.ExecuteReader();
                if (!leitor.Read())
                    throw new Exception("Usuário não encontrado.");

                gravada = DAOHelper.GetString(leitor, "senha_usr");
                algoritmo = DAOHelper.GetString(leitor, "algo_senha_usr");
            }

            if (!SegurancaSenha.Confere(senhaAtual, gravada, algoritmo))
                throw new Exception("A senha atual está incorreta.");

            if (SegurancaSenha.Confere(senhaNova, gravada, algoritmo))
                throw new Exception("A nova senha precisa ser diferente da atual.");

            using (var trocar = new MySqlCommand(
                @"UPDATE usuario
                     SET senha_usr = @senha, algo_senha_usr = @algo,
                         trocar_senha_usr = 0
                   WHERE id_usr = @id", conexao, transacao))
            {
                trocar.Parameters.AddWithValue("@senha", SegurancaSenha.Gerar(senhaNova!));
                trocar.Parameters.AddWithValue("@algo", SegurancaSenha.Atual);
                trocar.Parameters.AddWithValue("@id", idUsuario);

                trocar.ExecuteNonQuery();
            }

            // As outras sessoes caem; a de quem esta trocando continua.
            using (var sessoes = new MySqlCommand(
                @"UPDATE sessao SET encerrada_em = NOW()
                   WHERE id_usr_fk = @usuario AND encerrada_em IS NULL"
                + (string.IsNullOrWhiteSpace(tokenAtual) ? "" : " AND token_hash <> @token"),
                conexao, transacao))
            {
                sessoes.Parameters.AddWithValue("@usuario", idUsuario);

                if (!string.IsNullOrWhiteSpace(tokenAtual))
                {
                    sessoes.Parameters.AddWithValue(
                        "@token", SegurancaSenha.HashDoToken(tokenAtual));
                }

                sessoes.ExecuteNonQuery();
            }

            transacao.Commit();
        }
    }
}
