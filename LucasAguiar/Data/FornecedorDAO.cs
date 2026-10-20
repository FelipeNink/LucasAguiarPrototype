using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class FornecedorDAO
    {
        private readonly Conexao _conexao;

        public FornecedorDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Cadastro mínimo feito de dentro de outra tela, sem perder o que
        /// já estava preenchido lá. Devolve o id gerado.
        /// </summary>
        public int InserirRapido(string nome, string? telefone = null)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do fornecedor.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO fornecedor (nome_forn, telefone_forn)
                VALUES (@nome, @telefone);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@telefone",
                string.IsNullOrWhiteSpace(telefone) ? (object)DBNull.Value : telefone.Trim());

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Inserir(Fornecedor fornecedor)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(@"INSERT INTO fornecedor (nome_forn, documento_forn, email_forn, telefone_forn, tipo_prod_forn,
                        tipo_pessoa, nome_fantasia, contato_forn, cep_forn, estado_forn, cidade_forn, bairro_forn, rua_forn, numero_forn,
                        observacoes_forn, ativo_forn)
                    VALUES (@_nome, @_documento, @_email, @_telefone, @_tipo,
                        @tipoPessoa, @fantasia, @contato, @cep, @estado, @cidade, @bairro, @rua, @numero,
                        @observacoes, @ativo)
                "))
                {
                    PreencherNovos(comando, fornecedor);
                    comando.Parameters.AddWithValue("@_nome", fornecedor.NomeFornecedor ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_documento", fornecedor.Documento ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_email", fornecedor.Email ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_telefone", fornecedor.Telefone ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_tipo", fornecedor.TipoProd ?? (object)DBNull.Value);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir fornecedor: " + ex.Message);
            }
        }

        public Fornecedor? BuscarPorId(int id)
        {
            return ListarTodos().FirstOrDefault(f => f.IdFornecedor == id);
        }

        public void Atualizar(Fornecedor fornecedor)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE fornecedor SET
                    nome_forn = @nome, documento_forn = @documento, email_forn = @email,
                    telefone_forn = @telefone, tipo_prod_forn = @tipo,
                    tipo_pessoa = @tipoPessoa, nome_fantasia = @fantasia, contato_forn = @contato,
                    cep_forn = @cep, estado_forn = @estado, cidade_forn = @cidade, bairro_forn = @bairro,
                    rua_forn = @rua, numero_forn = @numero, observacoes_forn = @observacoes, ativo_forn = @ativo
                WHERE id_forn = @id", conexao);

            PreencherNovos(comando, fornecedor);
            comando.Parameters.AddWithValue("@nome", fornecedor.NomeFornecedor ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@documento", fornecedor.Documento ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@email", fornecedor.Email ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@telefone", fornecedor.Telefone ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@tipo", fornecedor.TipoProd ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@id", fornecedor.IdFornecedor);

            comando.ExecuteNonQuery();
        }

        public void Excluir(int id)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand("DELETE FROM fornecedor WHERE id_forn = @id", conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (DAOHelper.EhRegistroEmUso(ex))
            {
                throw new Exception(
                    "Este fornecedor está ligado a produtos, compras ou despesas e não pode ser excluído.");
            }
        }

        public List<Fornecedor> ListarTodos()
        {
            var lista = new List<Fornecedor>();

            using (var comando = _conexao.CreateCommand("SELECT * FROM fornecedor"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    var fornecedor = new Fornecedor
                    {
                        IdFornecedor = leitor.GetInt32("id_forn"),
                        NomeFornecedor = leitor.IsDBNull(leitor.GetOrdinal("nome_forn")) ? "" : leitor.GetString("nome_forn"),
                        Documento = leitor.IsDBNull(leitor.GetOrdinal("documento_forn")) ? null : leitor.GetString("documento_forn"),
                        Email = leitor.IsDBNull(leitor.GetOrdinal("email_forn")) ? "" : leitor.GetString("email_forn"),
                        Telefone = leitor.IsDBNull(leitor.GetOrdinal("telefone_forn")) ? "" : leitor.GetString("telefone_forn"),
                        TipoProd = leitor.IsDBNull(leitor.GetOrdinal("tipo_prod_forn")) ? "" : leitor.GetString("tipo_prod_forn")
                    };

                    LerNovos(leitor, fornecedor);
                    lista.Add(fornecedor);
                }
            }

            return lista;
        }

        /// <summary>Listagem com a data da compra mais recente de cada fornecedor.</summary>
        public List<Fornecedor> ListarComUltimaCompra()
        {
            var lista = new List<Fornecedor>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT f.*,
                       (SELECT MAX(c.data_comp) FROM compras c WHERE c.id_forn_fk = f.id_forn) AS ultima_compra
                FROM fornecedor f
                ORDER BY f.nome_forn", conexao);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var iUltima = leitor.GetOrdinal("ultima_compra");

                var fornecedor = new Fornecedor
                {
                    IdFornecedor = leitor.GetInt32("id_forn"),
                    NomeFornecedor = DAOHelper.GetString(leitor, "nome_forn"),
                    Documento = DAOHelper.GetString(leitor, "documento_forn"),
                    Email = DAOHelper.GetString(leitor, "email_forn"),
                    Telefone = DAOHelper.GetString(leitor, "telefone_forn"),
                    TipoProd = DAOHelper.GetString(leitor, "tipo_prod_forn"),
                    UltimaCompra = leitor.IsDBNull(iUltima) ? null : leitor.GetDateTime(iUltima)
                };

                LerNovos(leitor, fornecedor);
                lista.Add(fornecedor);
            }

            return lista;
        }

        private static void PreencherNovos(MySqlCommand comando, Fornecedor f)
        {
            object Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

            comando.Parameters.AddWithValue("@tipoPessoa", f.TipoPessoa == "PF" ? "PF" : "PJ");
            comando.Parameters.AddWithValue("@fantasia", Texto(f.NomeFantasia));
            comando.Parameters.AddWithValue("@contato", Texto(f.Contato));
            comando.Parameters.AddWithValue("@cep", Texto(f.Cep));
            comando.Parameters.AddWithValue("@estado", Texto(f.Estado));
            comando.Parameters.AddWithValue("@cidade", Texto(f.Cidade));
            comando.Parameters.AddWithValue("@bairro", Texto(f.Bairro));
            comando.Parameters.AddWithValue("@rua", Texto(f.Rua));
            comando.Parameters.AddWithValue("@numero", Texto(f.Numero));
            comando.Parameters.AddWithValue("@observacoes", Texto(f.Observacoes));
            comando.Parameters.AddWithValue("@ativo", f.Ativo ? 1 : 0);
        }

        private static void LerNovos(MySqlDataReader leitor, Fornecedor f)
        {
            string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

            f.TipoPessoa = leitor.GetString("tipo_pessoa");
            f.NomeFantasia = Texto("nome_fantasia");
            f.Contato = Texto("contato_forn");
            f.Cep = Texto("cep_forn");
            f.Estado = Texto("estado_forn");
            f.Cidade = Texto("cidade_forn");
            f.Bairro = Texto("bairro_forn");
            f.Rua = Texto("rua_forn");
            f.Numero = Texto("numero_forn");
            f.Observacoes = Texto("observacoes_forn");
            f.Ativo = leitor.GetBoolean("ativo_forn");
        }
    }
}
