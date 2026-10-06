using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class ServicoDAO
    {
        private readonly Conexao _conexao;

        public ServicoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Cadastro mínimo feito de dentro de outra tela, sem perder o que
        /// já estava preenchido lá. Devolve o id gerado.
        /// </summary>
        public int InserirRapido(string nome, decimal preco, decimal comissao, int duracaoMin = 30)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do serviço.");

            if (preco < 0) throw new Exception("O preço não pode ser negativo.");
            if (comissao < 0) throw new Exception("A comissão não pode ser negativa.");

            if (comissao > preco)
                throw new Exception("A comissão não pode ser maior que o preço do serviço.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli)
                VALUES (@nome, @preco, @duracao, @comissao);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@preco", preco);
            comando.Parameters.AddWithValue("@duracao", Math.Max(1, duracaoMin));
            comando.Parameters.AddWithValue("@comissao", comissao);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Inserir(Servico servico)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(@"INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli) VALUES (@_nome, @_preco, @_duracao, @_comissao)
                "))
                {
                    comando.Parameters.AddWithValue("@_nome", servico.NomeServico ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_preco", servico.PrecoServico);
                    comando.Parameters.AddWithValue("@_duracao", servico.DuracaoServico);
                    comando.Parameters.AddWithValue("@_comissao", servico.ComissaoServico);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir Serviço: " + ex.Message);
            }
        }

        public List<Servico> ListarTodos()
        {
            var lista = new List<Servico>();

            using (var comando = _conexao.CreateCommand("SELECT * FROM servico"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    var servico = new Servico
                    {
                        IdServico = leitor.GetInt32("id_serv"),
                        NomeServico = leitor.IsDBNull(leitor.GetOrdinal("nome_serv")) ? "" : leitor.GetString("nome_serv"),
                        PrecoServico = leitor.IsDBNull(leitor.GetOrdinal("preco_serv")) ? 0f : leitor.GetFloat(leitor.GetOrdinal("preco_serv")),
                        DuracaoServico = leitor.GetInt32("duracao_min"),
                        ComissaoServico = leitor.IsDBNull(leitor.GetOrdinal("comis_funcionario_cli")) ? 0f : leitor.GetFloat(leitor.GetOrdinal("comis_funcionario_cli"))
                    };

                    lista.Add(servico);
                }
            }

            return lista;
        }
    }
}