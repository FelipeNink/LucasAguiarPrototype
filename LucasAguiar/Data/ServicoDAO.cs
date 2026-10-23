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
            Validar(servico);

            try
            {
                using (var comando = _conexao.CreateCommand(@"INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli,
                        tipo_comissao, id_cat_serv_fk, custo_estimado, ativo_serv)
                    VALUES (@_nome, @_preco, @_duracao, @_comissao, @tipo, @idCategoria, @custo, @ativo)
                "))
                {
                    comando.Parameters.AddWithValue("@_nome", servico.NomeServico ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_preco", servico.PrecoServico);
                    comando.Parameters.AddWithValue("@_duracao", servico.DuracaoServico);
                    comando.Parameters.AddWithValue("@_comissao", servico.ComissaoServico);
                    PreencherNovos(comando, servico);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir Serviço: " + ex.Message);
            }
        }

        public Servico? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(SelecaoPadrao + " WHERE s.id_serv = @id", conexao);
            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        public void Atualizar(Servico servico)
        {
            Validar(servico);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE servico SET
                    nome_serv = @nome, preco_serv = @preco,
                    duracao_min = @duracao, comis_funcionario_cli = @comissao,
                    tipo_comissao = @tipo, id_cat_serv_fk = @idCategoria,
                    custo_estimado = @custo, ativo_serv = @ativo
                WHERE id_serv = @id", conexao);

            comando.Parameters.AddWithValue("@nome", servico.NomeServico ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@preco", servico.PrecoServico);
            comando.Parameters.AddWithValue("@duracao", servico.DuracaoServico);
            comando.Parameters.AddWithValue("@comissao", servico.ComissaoServico);
            comando.Parameters.AddWithValue("@id", servico.IdServico);
            PreencherNovos(comando, servico);

            comando.ExecuteNonQuery();
        }

        private static void Validar(Servico servico)
        {
            if (servico.PrecoServico < 0) throw new Exception("O preço não pode ser negativo.");
            if (servico.ComissaoServico < 0) throw new Exception("A comissão não pode ser negativa.");
            if (servico.CustoEstimado < 0) throw new Exception("O custo estimado não pode ser negativo.");
            if (servico.ComissaoPercentual && servico.ComissaoServico > 100)
                throw new Exception("A comissão em % não pode passar de 100%.");
        }

        private static void PreencherNovos(MySqlCommand comando, Servico servico)
        {
            comando.Parameters.AddWithValue("@tipo", servico.TipoComissao);
            comando.Parameters.AddWithValue("@idCategoria",
                servico.IdCategoria is > 0 ? servico.IdCategoria.Value : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@custo", servico.CustoEstimado ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@ativo", servico.Ativo ? 1 : 0);
        }

        private const string SelecaoPadrao = @"
            SELECT s.*, cs.nome_cat
            FROM servico s
            LEFT JOIN categoria_servico cs ON cs.id_cat_serv = s.id_cat_serv_fk";

        private static Servico Ler(MySqlDataReader leitor)
        {
            var iCusto = leitor.GetOrdinal("custo_estimado");
            var iCategoria = leitor.GetOrdinal("id_cat_serv_fk");

            return new Servico
            {
                IdServico = leitor.GetInt32("id_serv"),
                NomeServico = leitor.IsDBNull(leitor.GetOrdinal("nome_serv")) ? "" : leitor.GetString("nome_serv"),
                PrecoServico = leitor.IsDBNull(leitor.GetOrdinal("preco_serv")) ? 0m : leitor.GetDecimal(leitor.GetOrdinal("preco_serv")),
                DuracaoServico = leitor.IsDBNull(leitor.GetOrdinal("duracao_min")) ? 0 : leitor.GetInt32("duracao_min"),
                ComissaoServico = leitor.IsDBNull(leitor.GetOrdinal("comis_funcionario_cli")) ? 0m : leitor.GetDecimal(leitor.GetOrdinal("comis_funcionario_cli")),
                TipoComissao = leitor.GetString("tipo_comissao"),
                IdCategoria = leitor.IsDBNull(iCategoria) ? null : leitor.GetInt32(iCategoria),
                CustoEstimado = leitor.IsDBNull(iCusto) ? null : leitor.GetDecimal(iCusto),
                Ativo = leitor.GetBoolean("ativo_serv"),
                NomeCategoria = DAOHelper.IsNull(leitor, "nome_cat") ? null : leitor.GetString("nome_cat")
            };
        }

        public void Excluir(int id)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand("DELETE FROM servico WHERE id_serv = @id", conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (DAOHelper.EhRegistroEmUso(ex))
            {
                throw new Exception(
                    "Este serviço já foi usado em vendas ou agendamentos e não pode ser excluído.");
            }
        }

        public List<Servico> ListarTodos()
        {
            var lista = new List<Servico>();

            using (var comando = _conexao.CreateCommand(SelecaoPadrao))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read()) lista.Add(Ler(leitor));
            }

            return lista;
        }
    }
}