using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class ProdutoDAO
    {
        private readonly Conexao _conexao;

        public ProdutoDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Cadastro mínimo feito de dentro de outra tela, sem perder o que
        /// já estava preenchido lá. Devolve o id gerado.
        /// </summary>
        public int InserirRapido(string nome, decimal valor, int quantidade = 0, int? idFornecedor = null)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do produto.");

            if (valor < 0) throw new Exception("O valor não pode ser negativo.");
            if (quantidade < 0) throw new Exception("A quantidade não pode ser negativa.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO produto (nome_prod, valor_prod, quantidade_prod, id_forn_fk)
                VALUES (@nome, @valor, @quantidade, @idFornecedor);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@valor", valor);
            comando.Parameters.AddWithValue("@quantidade", quantidade);
            comando.Parameters.AddWithValue("@idFornecedor",
                idFornecedor.HasValue && idFornecedor.Value > 0 ? idFornecedor.Value : (object)DBNull.Value);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public void Inserir(Produto produto)
        {
            Validar(produto);

            try
            {
                using (var comando = _conexao.CreateCommand(@"
                    INSERT INTO produto (nome_prod, quantidade_prod, valor_prod, descricao_prod, marca_prod, id_forn_fk,
                        id_cat_prod_fk, codigo_barras, sku, unidade, preco_custo, comissao_prod, ativo_prod)
                    VALUES (@_nome, @_quantidade, @_valor, @_descricao, @_marca, @_idForn,
                        @idCategoria, @codigoBarras, @sku, @unidade, @custo, @comissao, @ativo)
                "))
                {
                    PreencherNovos(comando, produto);
                    comando.Parameters.AddWithValue("@_nome", produto.NomeProduto ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_quantidade", produto.Quantidade);
                    comando.Parameters.AddWithValue("@_valor", produto.Valor);
                    comando.Parameters.AddWithValue("@_descricao", produto.Descricao ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_marca", produto.Marca ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_idForn",
                        produto.IdFornecedor > 0 ? produto.IdFornecedor : (object)DBNull.Value);

                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir produto: " + ex.Message);
            }
        }

        public Produto? BuscarPorId(int id)
        {
            using (var comando = _conexao.CreateCommand(SelecaoPadrao + " WHERE p.id_prod = @id;"))
            {
                comando.Parameters.AddWithValue("@id", id);

                using (var leitor = comando.ExecuteReader())
                {
                    return leitor.Read() ? Ler(leitor) : null;
                }
            }
        }

        /// <summary>
        /// Altera o cadastro do produto.
        ///
        /// A quantidade digitada aqui não é mais gravada direto: a
        /// diferença entre ela e o saldo atual vira um ajuste de
        /// inventário no extrato do produto. Antes, quem editasse o
        /// cadastro podia reescrever o saldo sem deixar rastro -- era um
        /// dos motivos de o estoque nunca bater.
        /// </summary>
        public void Atualizar(Produto produto, string? usuario = null)
        {
            Validar(produto);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            int saldoAtual;

            using (var leitura = new MySqlCommand(
                "SELECT COALESCE(quantidade_prod, 0) FROM produto WHERE id_prod = @id FOR UPDATE",
                conexao, transacao))
            {
                leitura.Parameters.AddWithValue("@id", produto.IdProduto);

                var valor = leitura.ExecuteScalar()
                    ?? throw new Exception("Produto não encontrado.");

                saldoAtual = Convert.ToInt32(valor);
            }

            // quantidade_prod fica de fora do UPDATE de propósito: quem
            // mexe nela é o EstoqueDAO, logo abaixo, para a alteração
            // deixar um ajuste no extrato em vez de reescrever o saldo
            // sem rastro.
            using (var comando = new MySqlCommand(
                "UPDATE produto SET nome_prod = @_nome, descricao_prod = @_descricao, marca_prod = @_marca," +
                "valor_prod = @_preco, id_forn_fk = @_idForn," +
                "id_cat_prod_fk = @idCategoria, codigo_barras = @codigoBarras, sku = @sku, unidade = @unidade," +
                "preco_custo = @custo, comissao_prod = @comissao, ativo_prod = @ativo WHERE id_prod = @_id;",
                conexao, transacao))
            {
                PreencherNovos(comando, produto);
                comando.Parameters.AddWithValue("@_nome", produto.NomeProduto ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@_descricao", produto.Descricao ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@_marca", produto.Marca ?? (object)DBNull.Value);
                comando.Parameters.AddWithValue("@_preco", produto.Valor);
                comando.Parameters.AddWithValue("@_idForn",
                    produto.IdFornecedor > 0 ? produto.IdFornecedor : (object)DBNull.Value);
                comando.Parameters.AddWithValue("@_id", produto.IdProduto);

                comando.ExecuteNonQuery();
            }

            var diferenca = produto.Quantidade - saldoAtual;

            if (diferenca != 0)
            {
                EstoqueDAO.RegistrarNaTransacao(
                    conexao, transacao, produto.IdProduto,
                    TipoMovimentoEstoque.Ajuste, diferenca,
                    $"Quantidade alterada na edição do produto: {saldoAtual} → {produto.Quantidade}",
                    null, usuario);
            }

            transacao.Commit();
        }

        public void Excluir(int id)
        {
            try
            {
                using (var comando = _conexao.CreateCommand("DELETE FROM produto WHERE id_prod = @id;"))
                {
                    comando.Parameters.AddWithValue("@id", id);
                    comando.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex) when (DAOHelper.EhRegistroEmUso(ex))
            {
                throw new Exception(
                    "Este produto já foi usado em vendas ou compras e não pode ser excluído.");
            }
        }

        public List<Produto> ListarTodos()
        {
            var lista = new List<Produto>();
            using (var comando = _conexao.CreateCommand(SelecaoPadrao + " ORDER BY p.nome_prod"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read()) lista.Add(Ler(leitor));
            }

            return lista;
        }

        private const string SelecaoPadrao = @"
            SELECT p.*, f.nome_forn, cp.nome_cat
            FROM produto p
            LEFT JOIN fornecedor f ON p.id_forn_fk = f.id_forn
            LEFT JOIN categoria_produto cp ON cp.id_cat_prod = p.id_cat_prod_fk";

        private static void Validar(Produto produto)
        {
            if (produto.PrecoCusto < 0) throw new Exception("O preço de custo não pode ser negativo.");
            if (produto.Comissao is < 0 or > 100) throw new Exception("A comissão deve ficar entre 0% e 100%.");
        }

        private static void PreencherNovos(MySqlCommand comando, Produto produto)
        {
            object Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

            comando.Parameters.AddWithValue("@idCategoria",
                produto.IdCategoria is > 0 ? produto.IdCategoria.Value : (object)DBNull.Value);
            comando.Parameters.AddWithValue("@codigoBarras", Texto(produto.CodigoBarras));
            comando.Parameters.AddWithValue("@sku", Texto(produto.Sku));
            comando.Parameters.AddWithValue("@unidade", string.IsNullOrWhiteSpace(produto.Unidade) ? "UN" : produto.Unidade);
            comando.Parameters.AddWithValue("@custo", produto.PrecoCusto ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@comissao", produto.Comissao ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@ativo", produto.Ativo ? 1 : 0);
        }

        private static Produto Ler(MySqlDataReader leitor)
        {
            string? TextoOuNulo(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);
            decimal? DecimalOuNulo(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetDecimal(coluna);

            return new Produto
            {
                IdProduto = leitor.GetInt32("id_prod"),
                NomeProduto = DAOHelper.GetString(leitor, "nome_prod"),
                Quantidade = DAOHelper.IsNull(leitor, "quantidade_prod") ? 0 : leitor.GetInt32("quantidade_prod"),
                Valor = DAOHelper.IsNull(leitor, "valor_prod") ? 0 : leitor.GetFloat("valor_prod"),
                Descricao = DAOHelper.GetString(leitor, "descricao_prod"),
                Marca = DAOHelper.GetString(leitor, "marca_prod"),
                IdFornecedor = DAOHelper.IsNull(leitor, "id_forn_fk") ? 0 : leitor.GetInt32("id_forn_fk"),
                NomeFornecedor = DAOHelper.GetString(leitor, "nome_forn"),
                IdCategoria = DAOHelper.IsNull(leitor, "id_cat_prod_fk") ? null : leitor.GetInt32("id_cat_prod_fk"),
                NomeCategoria = TextoOuNulo("nome_cat"),
                CodigoBarras = TextoOuNulo("codigo_barras"),
                Sku = TextoOuNulo("sku"),
                Unidade = leitor.GetString("unidade"),
                PrecoCusto = DecimalOuNulo("preco_custo"),
                Comissao = DecimalOuNulo("comissao_prod"),
                Ativo = leitor.GetBoolean("ativo_prod")
            };
        }
        }
}
