using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class PlanoDAO
    {
        private readonly Conexao _conexao;

        public PlanoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        public int Inserir(Plano plano)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand(@"
                    INSERT INTO plano (nome_plan, descricao_plan, valor_plan)
                    VALUES (@nome, @descricao, @valor);
                    SELECT LAST_INSERT_ID();", conexao);

                comando.Parameters.AddWithValue("@nome", plano.NomePlano ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@descricao", plano.Descricao ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@valor", plano.Valor);

                return Convert.ToInt32(comando.ExecuteScalar());
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir plano: " + ex.Message);
            }
        }

        public List<Plano> ListarTodos()
        {
            var lista = new List<Plano>();
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT p.*,
                       (SELECT COALESCE(SUM(pi.quantidade), 0)
                          FROM plano_item pi WHERE pi.id_plan_fk = p.id_plan) AS total_itens
                FROM plano p
                ORDER BY p.nome_plan", conexao);

            using var leitor = comando.ExecuteReader();
            var iTotal = leitor.GetOrdinal("total_itens");

            while (leitor.Read())
            {
                var plano = Ler(leitor);
                plano.TotalItens = leitor.IsDBNull(iTotal) ? 0 : Convert.ToInt32(leitor.GetValue(iTotal));
                lista.Add(plano);
            }

            return lista;
        }

        public Plano? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT * FROM plano WHERE id_plan = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);
            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>
        /// Componentes do plano, já com nome e preço vindos de servico/produto.
        /// </summary>
        public List<PlanoItem> ListarItens(int idPlano)
        {
            var lista = new List<PlanoItem>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT pi.id_plan_item, pi.id_plan_fk, pi.tipo_item, pi.id_ref, pi.quantidade,
                       COALESCE(s.nome_serv, pr.nome_prod) AS descricao,
                       COALESCE(s.preco_serv, pr.valor_prod) AS valor
                FROM plano_item pi
                LEFT JOIN servico s ON pi.tipo_item = 'SERVICO' AND s.id_serv = pi.id_ref
                LEFT JOIN produto pr ON pi.tipo_item = 'PRODUTO' AND pr.id_prod = pi.id_ref
                WHERE pi.id_plan_fk = @idPlano
                ORDER BY pi.id_plan_item", conexao);
            comando.Parameters.AddWithValue("@idPlano", idPlano);

            using var leitor = comando.ExecuteReader();
            var iDescricao = leitor.GetOrdinal("descricao");
            var iValor = leitor.GetOrdinal("valor");

            while (leitor.Read())
            {
                lista.Add(new PlanoItem
                {
                    IdPlanoItem = leitor.GetInt32("id_plan_item"),
                    IdPlano = leitor.GetInt32("id_plan_fk"),
                    TipoItem = leitor.GetString("tipo_item"),
                    IdReferencia = leitor.GetInt32("id_ref"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    Descricao = leitor.IsDBNull(iDescricao) ? "(item removido)" : leitor.GetString(iDescricao),
                    ValorUnitario = leitor.IsDBNull(iValor) ? 0m : Convert.ToDecimal(leitor.GetValue(iValor))
                });
            }

            return lista;
        }

        /// <summary>Substitui os componentes do plano numa transação.</summary>
        public void SalvarItens(int idPlano, IEnumerable<PlanoItem> itens)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                using (var limpar = new MySqlCommand(
                    "DELETE FROM plano_item WHERE id_plan_fk = @idPlano", conexao, transacao))
                {
                    limpar.Parameters.AddWithValue("@idPlano", idPlano);
                    limpar.ExecuteNonQuery();
                }

                foreach (var item in itens)
                {
                    using var inserir = new MySqlCommand(@"
                        INSERT INTO plano_item (id_plan_fk, tipo_item, id_ref, quantidade)
                        VALUES (@idPlano, @tipo, @idRef, @qtd)", conexao, transacao);

                    inserir.Parameters.AddWithValue("@idPlano", idPlano);
                    inserir.Parameters.AddWithValue("@tipo", item.TipoItem);
                    inserir.Parameters.AddWithValue("@idRef", item.IdReferencia);
                    inserir.Parameters.AddWithValue("@qtd", Math.Max(1, item.Quantidade));
                    inserir.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao salvar os itens do plano: " + ex.Message);
            }
        }

        private static Plano Ler(MySqlDataReader leitor)
        {
            var iNome = leitor.GetOrdinal("nome_plan");
            var iDesc = leitor.GetOrdinal("descricao_plan");
            var iValor = leitor.GetOrdinal("valor_plan");

            return new Plano
            {
                IdPlano = leitor.GetInt32("id_plan"),
                NomePlano = leitor.IsDBNull(iNome) ? "" : leitor.GetString(iNome),
                Descricao = leitor.IsDBNull(iDesc) ? "" : leitor.GetString(iDesc),
                Valor = leitor.IsDBNull(iValor) ? 0f : leitor.GetFloat(iValor)
            };
        }
    }
}
