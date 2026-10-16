using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Funcoes da equipe -- "Barbeiro", "Recepção", "Gerente".
    ///
    /// O vinculo e por id, e nao por texto copiado na linha do usuario:
    /// assim renomear a funcao vale para todo mundo de uma vez, em vez de
    /// deixar o nome antigo espalhado.
    /// </summary>
    public class FuncaoDAO
    {
        private readonly Conexao _conexao;
        private readonly LogDAO _logs;

        public FuncaoDAO(Conexao conexao, LogDAO logs)
        {
            _conexao = conexao;
            _logs = logs;
        }

        public List<Funcao> Listar()
        {
            var lista = new List<Funcao>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                @"SELECT f.id_funcao, f.nome_funcao,
                         (SELECT COUNT(*) FROM usuario u
                           WHERE u.id_funcao_fk = f.id_funcao) AS usados
                    FROM funcao f
                   ORDER BY f.nome_funcao", conexao);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new Funcao
                {
                    IdFuncao = leitor.GetInt32("id_funcao"),
                    Nome = leitor.GetString("nome_funcao"),
                    Membros = leitor.GetInt32("usados")
                });
            }

            return lista;
        }

        /// <summary>
        /// Cria e devolve o id. Usada tambem pelo "Nova" do formulario do
        /// membro, para nao obrigar a sair da tela no meio do cadastro.
        /// </summary>
        public int Criar(string? nome, Autor autor)
        {
            var limpo = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(limpo))
                throw new Exception("Informe o nome da função.");

            if (limpo.Length > 60)
                throw new Exception("O nome da função pode ter no máximo 60 caracteres.");

            using var conexao = _conexao.GetConnection();

            // A collation da tabela nao diferencia maiusculas, entao
            // "Barbeiro" e "barbeiro" colidiriam no indice unico. Avisar
            // antes e melhor que deixar o banco estourar.
            if (NomeEmUso(conexao, null, limpo, ignorar: null))
                throw new Exception($"Já existe a função \"{limpo}\".");

            using var comando = new MySqlCommand(
                @"INSERT INTO funcao (nome_funcao) VALUES (@nome);
                  SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", limpo);

            var id = Convert.ToInt32(comando.ExecuteScalar());

            _logs.Registrar(new Evento
            {
                Acao = Acoes.FuncaoCriada,
                Descricao = $"Criou a função \"{limpo}\".",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = limpo
            });

            return id;
        }

        /// <summary>
        /// Renomear vale na hora para todos os membros ligados, porque o
        /// vinculo e por id.
        /// </summary>
        public void Renomear(int id, string? nome, Autor autor)
        {
            var limpo = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(limpo))
                throw new Exception("Informe o nome da função.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var antes = Ler(conexao, transacao, id);

            if (string.Equals(antes, limpo, StringComparison.Ordinal)) return;

            if (NomeEmUso(conexao, transacao, limpo, ignorar: id))
                throw new Exception($"Já existe a função \"{limpo}\".");

            using (var comando = new MySqlCommand(
                "UPDATE funcao SET nome_funcao = @nome WHERE id_funcao = @id",
                conexao, transacao))
            {
                comando.Parameters.AddWithValue("@nome", limpo);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.FuncaoRenomeada,
                Descricao = $"Renomeou a função: {antes} → {limpo}.",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = limpo,
                Meta = new Dictionary<string, object?> { ["de"] = antes, ["para"] = limpo }
            });
        }

        /// <summary>
        /// Excluir funcao em uso e barrado, dizendo quantos membros estao
        /// nela. Apagar por cima deixaria os acessos sem funcao sem
        /// ninguem perceber -- e a decisao de para onde vao essas pessoas
        /// nao e do codigo.
        /// </summary>
        public void Excluir(int id, Autor autor)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var nome = Ler(conexao, transacao, id);

            using (var contar = new MySqlCommand(
                "SELECT COUNT(*) FROM usuario WHERE id_funcao_fk = @id", conexao, transacao))
            {
                contar.Parameters.AddWithValue("@id", id);
                var usados = Convert.ToInt32(contar.ExecuteScalar());

                if (usados > 0)
                {
                    throw new Exception(
                        $"A função \"{nome}\" está em uso por {usados} "
                        + (usados == 1 ? "membro" : "membros")
                        + ". Mude a função dessas pessoas antes de excluir.");
                }
            }

            using (var apagar = new MySqlCommand(
                "DELETE FROM funcao WHERE id_funcao = @id", conexao, transacao))
            {
                apagar.Parameters.AddWithValue("@id", id);
                apagar.ExecuteNonQuery();
            }

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.FuncaoExcluida,
                Descricao = $"Excluiu a função \"{nome}\".",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = nome
            });
        }

        private static string Ler(MySqlConnection conexao, MySqlTransaction? transacao, int id)
        {
            using var comando = new MySqlCommand(
                "SELECT nome_funcao FROM funcao WHERE id_funcao = @id", conexao, transacao);

            comando.Parameters.AddWithValue("@id", id);

            var nome = comando.ExecuteScalar() as string;

            return nome ?? throw new Exception("Função não encontrada.");
        }

        private static bool NomeEmUso(
            MySqlConnection conexao, MySqlTransaction? transacao, string nome, int? ignorar)
        {
            using var comando = new MySqlCommand(
                "SELECT COUNT(*) FROM funcao WHERE nome_funcao = @nome AND id_funcao <> @ignorar",
                conexao, transacao);

            comando.Parameters.AddWithValue("@nome", nome);
            comando.Parameters.AddWithValue("@ignorar", ignorar ?? 0);

            return Convert.ToInt32(comando.ExecuteScalar()) > 0;
        }
    }
}
