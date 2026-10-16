using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// A matriz de permissoes: uma linha por par (usuario, tela).
    ///
    /// Administrador nao tem linha nenhuma aqui -- para ele a matriz e
    /// ignorada. Guardar linhas para admin so criaria a chance de um dia
    /// elas discordarem do perfil.
    /// </summary>
    public class PermissaoDAO
    {
        private readonly Conexao _conexao;

        public PermissaoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>Colunas de acao, na ordem do catalogo.</summary>
        private static readonly string Colunas =
            string.Join(", ", Catalogo.Acoes.Select(Catalogo.Coluna));

        public Permissoes Carregar(int idUsuario, bool ehAdmin)
        {
            if (ehAdmin) return Permissoes.DeAdmin;

            return new Permissoes(false, Ler(idUsuario));
        }

        public Dictionary<string, Acao> Ler(int idUsuario)
        {
            var mapa = new Dictionary<string, Acao>(StringComparer.OrdinalIgnoreCase);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                $"SELECT tela, {Colunas} FROM permissao WHERE id_usr_fk = @id", conexao);

            comando.Parameters.AddWithValue("@id", idUsuario);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var tela = leitor.GetString("tela");

                // Tela que saiu do catalogo mas cuja linha ficou no banco:
                // ignorada, para nao dar permissao a nada.
                if (Catalogo.Por(tela) == null) continue;

                mapa[tela] = LerAcoes(leitor);
            }

            return mapa;
        }

        private static Acao LerAcoes(MySqlDataReader leitor)
        {
            var acoes = Acao.Nenhuma;

            foreach (var acao in Catalogo.Acoes)
            {
                if (leitor.GetBoolean(Catalogo.Coluna(acao)))
                    acoes |= acao;
            }

            return acoes;
        }

        /// <summary>
        /// Grava a matriz inteira de um usuario numa transacao: ou todas as
        /// telas mudam, ou nenhuma. Salvar tela a tela deixaria o acesso
        /// pela metade se a conexao caisse no meio.
        ///
        /// As acoes sao filtradas pelo que a tela declara no catalogo, e
        /// qualquer tela sem "Acessar" tem o resto zerado: a tela nao e a
        /// unica fonte de verdade, o servidor refaz a regra aqui.
        /// </summary>
        public void Salvar(int idUsuario, IDictionary<string, Acao> matriz)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            SalvarNaTransacao(conexao, transacao, idUsuario, matriz);

            transacao.Commit();
        }

        /// <summary>
        /// Mesma gravacao, para quando criar o usuario e definir a matriz
        /// precisam ser uma operacao so.
        /// </summary>
        public static void SalvarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao,
            int idUsuario, IDictionary<string, Acao> matriz)
        {
            var atribuicoes = string.Join(", ",
                Catalogo.Acoes.Select(a => $"`{Catalogo.Coluna(a)}` = @{Catalogo.Coluna(a)}"));

            var campos = string.Join(", ", Catalogo.Acoes.Select(a => $"`{Catalogo.Coluna(a)}`"));
            var valores = string.Join(", ", Catalogo.Acoes.Select(a => $"@{Catalogo.Coluna(a)}"));

            foreach (var tela in Catalogo.Telas)
            {
                var pedido = matriz.TryGetValue(tela.Chave, out var a) ? a : Acao.Nenhuma;
                var efetivo = Normalizar(tela, pedido);

                using var comando = new MySqlCommand(
                    $@"INSERT INTO permissao (id_usr_fk, tela, {campos})
                       VALUES (@usuario, @tela, {valores})
                       ON DUPLICATE KEY UPDATE {atribuicoes}", conexao, transacao);

                comando.Parameters.AddWithValue("@usuario", idUsuario);
                comando.Parameters.AddWithValue("@tela", tela.Chave);

                foreach (var acao in Catalogo.Acoes)
                {
                    comando.Parameters.AddWithValue(
                        "@" + Catalogo.Coluna(acao), (efetivo & acao) == acao);
                }

                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Duas regras aplicadas no servidor, independentemente do que a
        /// tela mandou: a tela so recebe as acoes que ela declara, e sem
        /// "Acessar" o resto e zerado.
        /// </summary>
        public static Acao Normalizar(Tela tela, Acao pedido)
        {
            var dentroDoCatalogo = pedido & tela.Acoes;

            return (dentroDoCatalogo & Acao.Ver) == Acao.Ver
                ? dentroDoCatalogo
                : Acao.Nenhuma;
        }

        /// <summary>
        /// Apaga a matriz do usuario. Usado ao promover alguem a
        /// administrador, quando as linhas deixam de ter sentido.
        /// </summary>
        public static void ApagarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idUsuario)
        {
            using var comando = new MySqlCommand(
                "DELETE FROM permissao WHERE id_usr_fk = @id", conexao, transacao);

            comando.Parameters.AddWithValue("@id", idUsuario);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Garante a linha negada de cada tela do catalogo para o usuario.
        /// Mesma sincronizacao da subida, aplicada a um usuario so -- usada
        /// ao rebaixar um admin para comum, que nasce sem nenhuma linha.
        /// </summary>
        public static void SincronizarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idUsuario)
        {
            foreach (var tela in Catalogo.Telas)
            {
                using var comando = new MySqlCommand(
                    @"INSERT IGNORE INTO permissao (id_usr_fk, tela)
                      VALUES (@usuario, @tela)", conexao, transacao);

                comando.Parameters.AddWithValue("@usuario", idUsuario);
                comando.Parameters.AddWithValue("@tela", tela.Chave);
                comando.ExecuteNonQuery();
            }
        }

        // ---------- diferenca, para a auditoria ----------

        /// <summary>
        /// O que mudou entre duas matrizes, em texto legivel por tela:
        /// "Vendas: +Criar, −Estornar". O log guarda isso em vez de um
        /// generico "permissões atualizadas", que nao diz nada a quem
        /// precisar investigar depois.
        /// </summary>
        public static Dictionary<string, object?> Diferenca(
            IDictionary<string, Acao> antes, IDictionary<string, Acao> depois)
        {
            var mudancas = new Dictionary<string, object?>();

            foreach (var tela in Catalogo.Telas)
            {
                var de = antes.TryGetValue(tela.Chave, out var a) ? a : Acao.Nenhuma;
                var para = depois.TryGetValue(tela.Chave, out var d) ? d : Acao.Nenhuma;

                if (de == para) continue;

                var ganhou = Catalogo.Acoes
                    .Where(x => (para & x) == x && (de & x) != x)
                    .Select(x => "+" + Catalogo.Rotulo(x));

                var perdeu = Catalogo.Acoes
                    .Where(x => (de & x) == x && (para & x) != x)
                    .Select(x => "−" + Catalogo.Rotulo(x));

                mudancas[tela.Nome] = string.Join(", ", ganhou.Concat(perdeu));
            }

            return mudancas;
        }
    }
}
