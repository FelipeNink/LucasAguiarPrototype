using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Sessoes no banco, nao na memoria do processo.
    ///
    /// Antes a sessao era do ASP.NET Core com cache em memoria: todo deploy
    /// deslogava todo mundo, e nao havia como derrubar o acesso de alguem
    /// -- desativar um usuario deixava a sessao dele de pe ate expirar.
    ///
    /// Com a linha no banco, desativar, excluir ou redefinir a senha de
    /// alguem corta o acesso na requisicao seguinte, porque
    /// <see cref="Validar"/> roda a cada pedido e confere as duas coisas
    /// numa consulta so: a sessao continua valida e o usuario continua ativo.
    ///
    /// A coluna guarda o hash do token. Quem ler a tabela nao consegue
    /// entrar como ninguem.
    /// </summary>
    public class SessaoDAO
    {
        private readonly Conexao _conexao;

        /// <summary>
        /// Validade absoluta, sem renovacao: oito horas cobrem um dia de
        /// trabalho e obrigam a entrar de novo no dia seguinte.
        /// </summary>
        public static readonly TimeSpan Duracao = TimeSpan.FromHours(8);

        public SessaoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Abre a sessao e devolve o token em texto -- a unica vez que ele
        /// existe fora do cookie. O banco fica so com o hash.
        /// </summary>
        public string Criar(int idUsuario, string? ip, string? agente)
        {
            var token = SegurancaSenha.TokenNovo();

            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand(
                    @"INSERT INTO sessao (token_hash, id_usr_fk, expira_em, ip, agente)
                      VALUES (@hash, @usuario, @expira, @ip, @agente)", conexao);

                comando.Parameters.AddWithValue("@hash", SegurancaSenha.HashDoToken(token));
                comando.Parameters.AddWithValue("@usuario", idUsuario);
                comando.Parameters.AddWithValue("@expira", DateTime.Now.Add(Duracao));
                comando.Parameters.AddWithValue("@ip", Recortar(ip, 45));
                comando.Parameters.AddWithValue("@agente", Recortar(agente, 255));

                comando.ExecuteNonQuery();
                return token;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao abrir a sessão: " + ex.Message);
            }
        }

        /// <summary>
        /// Quem e o dono do token, ou null se a sessao nao serve mais:
        /// expirada, encerrada, ou de um usuario desativado. Roda a cada
        /// requisicao autenticada.
        /// </summary>
        public Acesso? Validar(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"SELECT s.id_sessao, s.expira_em,
                         u.id_usr, u.nome_usr, u.email_usr, u.ativo_usr,
                         u.data_criacao_usr, u.perfil_usr, u.raiz_usr,
                         u.trocar_senha_usr, u.algo_senha_usr,
                         u.id_funcao_fk, f.nome_funcao,
                         u.id_fun_fk, fun.nome_fun,
                         u.ultimo_login_usr, u.atualizado_usr
                    FROM sessao s
                    JOIN usuario u ON u.id_usr = s.id_usr_fk
                    LEFT JOIN funcao f ON f.id_funcao = u.id_funcao_fk
                    LEFT JOIN funcionario fun ON fun.id_fun = u.id_fun_fk
                   WHERE s.token_hash = @hash
                     AND s.encerrada_em IS NULL
                     AND s.expira_em > NOW()
                     AND u.ativo_usr = 1", conexao);

            comando.Parameters.AddWithValue("@hash", SegurancaSenha.HashDoToken(token));

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return null;

            return new Acesso(
                LerUsuario(leitor),
                leitor.GetInt32("id_sessao"),
                leitor.GetDateTime("expira_em"));
        }

        public void Encerrar(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"UPDATE sessao SET encerrada_em = NOW()
                   WHERE token_hash = @hash AND encerrada_em IS NULL", conexao);

            comando.Parameters.AddWithValue("@hash", SegurancaSenha.HashDoToken(token));
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Derruba as sessoes do usuario. Chamado ao desativar, excluir ou
        /// redefinir a senha de alguem -- e, com
        /// <paramref name="excetoToken"/>, ao trocar a propria senha, que
        /// desfaz as outras e mantem a de quem esta trocando.
        /// </summary>
        public int EncerrarTodasDoUsuario(int idUsuario, string? excetoToken = null)
        {
            var temExcecao = !string.IsNullOrWhiteSpace(excetoToken);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"UPDATE sessao SET encerrada_em = NOW()
                   WHERE id_usr_fk = @usuario AND encerrada_em IS NULL"
                + (temExcecao ? " AND token_hash <> @exceto" : ""), conexao);

            comando.Parameters.AddWithValue("@usuario", idUsuario);

            if (temExcecao)
            {
                comando.Parameters.AddWithValue(
                    "@exceto", SegurancaSenha.HashDoToken(excetoToken!));
            }

            return comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Mesma coisa dentro de uma transacao em andamento: redefinir a
        /// senha e derrubar as sessoes tem de ser uma coisa so, senao a
        /// sessao antiga sobrevive a troca.
        /// </summary>
        public static void EncerrarTodasNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idUsuario)
        {
            using var comando = new MySqlCommand(
                @"UPDATE sessao SET encerrada_em = NOW()
                   WHERE id_usr_fk = @usuario AND encerrada_em IS NULL",
                conexao, transacao);

            comando.Parameters.AddWithValue("@usuario", idUsuario);
            comando.ExecuteNonQuery();
        }

        public List<Sessao> ListarDoUsuario(int idUsuario, string? tokenAtual = null)
        {
            var lista = new List<Sessao>();
            var hashAtual = string.IsNullOrWhiteSpace(tokenAtual)
                ? null
                : SegurancaSenha.HashDoToken(tokenAtual);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"SELECT id_sessao, id_usr_fk, criado_em, expira_em, encerrada_em,
                         ip, agente, token_hash
                    FROM sessao
                   WHERE id_usr_fk = @usuario
                     AND encerrada_em IS NULL
                     AND expira_em > NOW()
                   ORDER BY criado_em DESC", conexao);

            comando.Parameters.AddWithValue("@usuario", idUsuario);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new Sessao
                {
                    IdSessao = leitor.GetInt32("id_sessao"),
                    IdUsuario = leitor.GetInt32("id_usr_fk"),
                    CriadoEm = leitor.GetDateTime("criado_em"),
                    ExpiraEm = leitor.GetDateTime("expira_em"),
                    EncerradaEm = DAOHelper.GetDateTime(leitor, "encerrada_em"),
                    Ip = DAOHelper.GetString(leitor, "ip"),
                    Agente = DAOHelper.GetString(leitor, "agente"),
                    EstaAtual = hashAtual != null
                        && leitor.GetString("token_hash") == hashAtual
                });
            }

            return lista;
        }

        /// <summary>Encerra uma sessao especifica, so do proprio usuario.</summary>
        public void EncerrarPorId(int idSessao, int idUsuario)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"UPDATE sessao SET encerrada_em = NOW()
                   WHERE id_sessao = @id AND id_usr_fk = @usuario
                     AND encerrada_em IS NULL", conexao);

            comando.Parameters.AddWithValue("@id", idSessao);
            comando.Parameters.AddWithValue("@usuario", idUsuario);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Apaga o que ja nao serve para nada. Sessao vencida ha mais de
        /// sete dias nao e historico: o que precisa ficar registrado esta
        /// no log, nao aqui.
        /// </summary>
        public int LimparVencidas()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"DELETE FROM sessao
                   WHERE expira_em < NOW() - INTERVAL 7 DAY
                      OR (encerrada_em IS NOT NULL
                          AND encerrada_em < NOW() - INTERVAL 7 DAY)", conexao);

            return comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Monta o usuario a partir de uma consulta que trouxe as colunas de
        /// <c>usuario</c> mais <c>nome_funcao</c> e <c>nome_fun</c>.
        /// Compartilhado com o UsuarioDAO para os dois lerem igual.
        /// </summary>
        internal static Usuario LerUsuario(MySqlDataReader leitor) => new()
        {
            IdUsr = leitor.GetInt32("id_usr"),
            NomeUsr = DAOHelper.GetString(leitor, "nome_usr"),
            EmailUsr = DAOHelper.GetString(leitor, "email_usr"),
            AtivoUsr = leitor.GetBoolean("ativo_usr"),
            DataCriacaoUsr = leitor.GetDateTime("data_criacao_usr"),
            PerfilUsr = DAOHelper.GetString(leitor, "perfil_usr"),
            RaizUsr = leitor.GetBoolean("raiz_usr"),
            TrocarSenhaUsr = leitor.GetBoolean("trocar_senha_usr"),
            AlgoSenhaUsr = DAOHelper.GetString(leitor, "algo_senha_usr"),
            IdFuncao = DAOHelper.IsNull(leitor, "id_funcao_fk")
                ? null : leitor.GetInt32("id_funcao_fk"),
            NomeFuncao = DAOHelper.GetString(leitor, "nome_funcao"),
            IdFuncionario = DAOHelper.IsNull(leitor, "id_fun_fk")
                ? null : leitor.GetInt32("id_fun_fk"),
            NomeFuncionario = DAOHelper.GetString(leitor, "nome_fun"),
            UltimoLoginUsr = DAOHelper.GetDateTime(leitor, "ultimo_login_usr"),
            AtualizadoUsr = DAOHelper.GetDateTime(leitor, "atualizado_usr")
        };

        private static object Recortar(string? texto, int tamanho)
        {
            if (string.IsNullOrWhiteSpace(texto)) return DBNull.Value;

            return texto.Length <= tamanho ? texto : texto[..tamanho];
        }
    }
}
