using LucasAguiar.Configs;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Freio de forca bruta no login.
    ///
    /// Conta as falhas por usuario e por IP, e bloqueia as duas chaves
    /// depois de cinco erros em quinze minutos. Acertar a senha zera a
    /// contagem.
    ///
    /// A contagem vive no banco, nao em memoria: reiniciar a aplicacao --
    /// o que no Railway acontece a cada deploy -- nao pode servir de
    /// escapatoria para quem esta tentando adivinhar senha, e com mais de
    /// uma instancia a contagem em memoria contaria errado.
    ///
    /// As duas chaves fazem trabalhos diferentes: a de usuario protege uma
    /// conta de ataque vindo de varios IPs; a de IP impede varrer muitos
    /// nomes de usuario a partir do mesmo lugar.
    /// </summary>
    public class TentativaLoginDAO
    {
        private readonly Conexao _conexao;

        public const int MaximoFalhas = 5;
        public static readonly TimeSpan Janela = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan Bloqueio = TimeSpan.FromMinutes(15);

        public TentativaLoginDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        public static string ChaveDeUsuario(string? nome) =>
            "usr:" + (nome ?? "").Trim().ToLowerInvariant();

        public static string ChaveDeIp(string? ip) =>
            "ip:" + (ip ?? "?").Trim();

        /// <summary>
        /// Ate quando o acesso esta barrado, ou null se esta liberado.
        /// Devolve o maior prazo entre as chaves consultadas.
        /// </summary>
        public DateTime? BloqueadoAte(params string[] chaves)
        {
            if (chaves.Length == 0) return null;

            var nomes = chaves.Select((_, i) => "@c" + i).ToArray();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                $@"SELECT MAX(bloqueado_ate) FROM tentativa_login
                    WHERE chave IN ({string.Join(',', nomes)})
                      AND bloqueado_ate > NOW()", conexao);

            for (var i = 0; i < chaves.Length; i++)
                comando.Parameters.AddWithValue(nomes[i], Recortar(chaves[i]));

            var valor = comando.ExecuteScalar();
            return valor == null || valor == DBNull.Value ? null : Convert.ToDateTime(valor);
        }

        /// <summary>
        /// Soma uma falha em cada chave. Devolve ate quando ficou bloqueado,
        /// ou null se ainda ha tentativas sobrando.
        ///
        /// A contagem reinicia quando a ultima falha foi ha mais tempo que a
        /// janela: cinco erros espalhados por um mes nao sao um ataque.
        /// </summary>
        public DateTime? RegistrarFalha(params string[] chaves)
        {
            DateTime? bloqueio = null;

            using var conexao = _conexao.GetConnection();

            foreach (var chave in chaves)
            {
                var resultado = Somar(conexao, chave);

                if (resultado.HasValue && (!bloqueio.HasValue || resultado > bloqueio))
                    bloqueio = resultado;
            }

            return bloqueio;
        }

        private static DateTime? Somar(MySqlConnection conexao, string chave)
        {
            using var transacao = conexao.BeginTransaction();

            var contagem = 1;

            // FOR UPDATE: duas tentativas simultaneas na mesma chave nao
            // podem ler a mesma contagem e gravar o mesmo numero.
            using (var atual = new MySqlCommand(
                @"SELECT contagem, atualizado_em FROM tentativa_login
                   WHERE chave = @chave FOR UPDATE", conexao, transacao))
            {
                atual.Parameters.AddWithValue("@chave", Recortar(chave));

                using var leitor = atual.ExecuteReader();
                if (leitor.Read())
                {
                    var quando = leitor.GetDateTime("atualizado_em");

                    contagem = DateTime.Now - quando > Janela
                        ? 1
                        : leitor.GetInt32("contagem") + 1;
                }
            }

            DateTime? bloqueadoAte = contagem >= MaximoFalhas
                ? DateTime.Now.Add(Bloqueio)
                : null;

            using (var gravar = new MySqlCommand(
                @"INSERT INTO tentativa_login (chave, contagem, bloqueado_ate, atualizado_em)
                  VALUES (@chave, @contagem, @bloqueio, NOW())
                  ON DUPLICATE KEY UPDATE
                      contagem = @contagem,
                      bloqueado_ate = @bloqueio,
                      atualizado_em = NOW()", conexao, transacao))
            {
                gravar.Parameters.AddWithValue("@chave", Recortar(chave));
                gravar.Parameters.AddWithValue("@contagem", contagem);
                gravar.Parameters.AddWithValue(
                    "@bloqueio", bloqueadoAte.HasValue ? bloqueadoAte.Value : DBNull.Value);

                gravar.ExecuteNonQuery();
            }

            transacao.Commit();
            return bloqueadoAte;
        }

        /// <summary>Entrou: a contagem das chaves dele volta a zero.</summary>
        public void Zerar(params string[] chaves)
        {
            if (chaves.Length == 0) return;

            var nomes = chaves.Select((_, i) => "@c" + i).ToArray();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                $"DELETE FROM tentativa_login WHERE chave IN ({string.Join(',', nomes)})",
                conexao);

            for (var i = 0; i < chaves.Length; i++)
                comando.Parameters.AddWithValue(nomes[i], Recortar(chaves[i]));

            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Faxina das linhas que ja nao contam para nada: a janela passou e
        /// o bloqueio venceu. Roda na subida da aplicacao.
        /// </summary>
        public int Limpar()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"DELETE FROM tentativa_login
                   WHERE atualizado_em < NOW() - INTERVAL 1 DAY
                     AND (bloqueado_ate IS NULL OR bloqueado_ate < NOW())", conexao);

            return comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Quantos minutos faltam, arredondando para cima: a mensagem diz
        /// "1 minuto" em vez de "0 minutos" nos ultimos segundos.
        /// </summary>
        public static int MinutosRestantes(DateTime bloqueadoAte)
        {
            var falta = bloqueadoAte - DateTime.Now;
            return falta <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(falta.TotalMinutes);
        }

        private static string Recortar(string chave) =>
            chave.Length <= 190 ? chave : chave[..190];
    }
}
