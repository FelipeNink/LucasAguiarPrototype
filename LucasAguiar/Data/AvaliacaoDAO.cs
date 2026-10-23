using System.Security.Cryptography;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Avaliacao pos-atendimento: uma por venda, pedida pelo WhatsApp com o
    /// link /avaliar/&lt;token&gt;, respondida uma vez so pelo cliente. A nota
    /// fica com o profissional que fez o servico.
    /// </summary>
    public class AvaliacaoDAO
    {
        private readonly Conexao _conexao;

        public AvaliacaoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        private const string Cancelada = "CANCELADA";

        /// <summary>
        /// Codigo do link da venda, criado na primeira vez que alguem vai
        /// pedir a avaliacao. Aleatorio (128 bits): um link nao leva ao de
        /// outro cliente trocando um numero. Null se a venda foi cancelada.
        /// </summary>
        public string? GarantirToken(int idVenda)
        {
            using var conexao = _conexao.GetConnection();

            // O profissional e o do primeiro servico da venda; venda so de
            // produto fica com o da venda.
            using (var criar = new MySqlCommand(@"
                INSERT IGNORE INTO avaliacao (id_vend_fk, id_cli_fk, id_fun_fk, token)
                SELECT v.id_vend, v.id_cli_fk,
                       COALESCE((SELECT vi.id_fun_fk FROM venda_item vi
                                  WHERE vi.id_vend_fk = v.id_vend AND vi.tipo_item = 'SERVICO'
                                    AND vi.id_fun_fk IS NOT NULL
                                  ORDER BY vi.id_vend_item LIMIT 1), v.id_fun_fk),
                       @token
                  FROM venda v
                 WHERE v.id_vend = @id AND COALESCE(v.status_vend, '') <> @cancelada", conexao))
            {
                criar.Parameters.AddWithValue("@token", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                criar.Parameters.AddWithValue("@id", idVenda);
                criar.Parameters.AddWithValue("@cancelada", Cancelada);
                criar.ExecuteNonQuery();
            }

            using var ler = new MySqlCommand("SELECT token FROM avaliacao WHERE id_vend_fk = @id", conexao);
            ler.Parameters.AddWithValue("@id", idVenda);
            return ler.ExecuteScalar() as string;
        }

        private const string Selecao = @"
            SELECT a.*, v.data_vend, v.status_vend, c.nome_cli, f.nome_fun,
                   (SELECT GROUP_CONCAT(vi.descricao ORDER BY vi.id_vend_item SEPARATOR ' + ')
                      FROM venda_item vi
                     WHERE vi.id_vend_fk = a.id_vend_fk AND vi.tipo_item = 'SERVICO') AS servicos
              FROM avaliacao a
              JOIN venda v ON v.id_vend = a.id_vend_fk
              LEFT JOIN cliente c ON c.id_cli = a.id_cli_fk
              LEFT JOIN funcionario f ON f.id_fun = a.id_fun_fk";

        public Avaliacao? BuscarPorToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length != 32) return null;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(Selecao + " WHERE a.token = @token", conexao);
            comando.Parameters.AddWithValue("@token", token);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>A avaliacao da venda, se ja foi pedida.</summary>
        public Avaliacao? BuscarPorVenda(int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(Selecao + " WHERE a.id_vend_fk = @id", conexao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>
        /// Grava a nota. Vale uma vez, dentro do prazo e com a venda de pe:
        /// quem decide e o WHERE, nao a pagina. False se o link nao vale mais.
        /// </summary>
        public bool Responder(string token, int nota, string? comentario)
        {
            if (!NotaAvaliacao.Valida(nota)) throw new Exception("Escolha uma nota de 1 a 5.");

            var texto = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
            if (texto?.Length > NotaAvaliacao.TamanhoComentario) texto = texto[..NotaAvaliacao.TamanhoComentario];

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE avaliacao a
                  JOIN venda v ON v.id_vend = a.id_vend_fk
                   SET a.nota = @nota, a.comentario = @comentario, a.respondido_em = NOW()
                 WHERE a.token = @token AND a.nota IS NULL
                   AND COALESCE(v.status_vend, '') <> @cancelada
                   AND v.data_vend >= NOW() - INTERVAL @prazo DAY", conexao);
            comando.Parameters.AddWithValue("@nota", nota);
            comando.Parameters.AddWithValue("@comentario", texto ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@token", token);
            comando.Parameters.AddWithValue("@cancelada", Cancelada);
            comando.Parameters.AddWithValue("@prazo", NotaAvaliacao.PrazoDias);
            return comando.ExecuteNonQuery() > 0;
        }

        /// <summary>Respondidas no periodo [de, ate), da mais nova para a mais antiga.</summary>
        public List<Avaliacao> Respondidas(DateTime de, DateTime ate, int? idFuncionario = null, int? notaAte = null)
        {
            var lista = new List<Avaliacao>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(Selecao + $@"
                 WHERE a.respondido_em >= @de AND a.respondido_em < @ate
                   {(idFuncionario.HasValue ? "AND a.id_fun_fk = @fun" : "")}
                   {(notaAte.HasValue ? "AND a.nota <= @notaAte" : "")}
                 ORDER BY a.respondido_em DESC", conexao);
            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);
            if (idFuncionario.HasValue) comando.Parameters.AddWithValue("@fun", idFuncionario.Value);
            if (notaAte.HasValue) comando.Parameters.AddWithValue("@notaAte", notaAte.Value);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        /// <summary>Notas por profissional no periodo, a melhor media primeiro.</summary>
        public static List<LinhaAvaliacaoProfissional> PorProfissional(IEnumerable<Avaliacao> respondidas) =>
            respondidas
                .Where(a => a.Nota.HasValue)
                .GroupBy(a => a.NomeFuncionario ?? "Sem profissional")
                .Select(g =>
                {
                    var linha = new LinhaAvaliacaoProfissional { Nome = g.Key };
                    foreach (var a in g) linha.PorNota[a.Nota!.Value]++;
                    return linha;
                })
                .OrderByDescending(l => l.Media)
                .ThenByDescending(l => l.Quantidade)
                .ToList();

        private static Avaliacao Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

            return new Avaliacao
            {
                IdAvaliacao = leitor.GetInt32("id_aval"),
                IdVenda = leitor.GetInt32("id_vend_fk"),
                Token = leitor.GetString("token"),
                Nota = DAOHelper.IsNull(leitor, "nota") ? null : Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("nota"))),
                Comentario = Texto("comentario"),
                RespondidoEm = DAOHelper.GetDateTime(leitor, "respondido_em"),
                DataVenda = DAOHelper.GetDateTime(leitor, "data_vend") ?? DateTime.MinValue,
                VendaCancelada = Texto("status_vend") == Cancelada,
                NomeCliente = Texto("nome_cli"),
                NomeFuncionario = Texto("nome_fun"),
                Servicos = Texto("servicos")
            };
        }
    }
}
