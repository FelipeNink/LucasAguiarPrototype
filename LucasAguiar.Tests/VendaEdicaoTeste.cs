using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Edicao e cancelamento de venda contra um MySQL de verdade: o que se
    /// confere e o estoque que volta e sai, e isso e o banco quem faz.
    /// </summary>
    public class VendaEdicaoTeste
    {
        private sealed class Cenario : IDisposable
        {
            public readonly MySqlConnection Conexao = BancoDeTeste.Abrir();
            public readonly VendaDAO Vendas = new(BancoDeTeste.Fabricar());
            public readonly int IdFuncionario;
            public readonly int IdProduto;
            public readonly List<int> VendasCriadas = new();
            public int? IdCaixa;

            public Cenario(int estoqueInicial)
            {
                var fabrica = BancoDeTeste.Fabricar();
                IdFuncionario = new FuncionarioDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("prof"));
                IdProduto = new ProdutoDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("pomada"), 30m, estoqueInicial);
            }

            public VendaItem Produto(int quantidade, decimal unitario = 30m) => new()
            {
                TipoItem = TipoItem.Produto,
                IdReferencia = IdProduto,
                Descricao = "Pomada",
                Quantidade = quantidade,
                ValorUnitario = unitario
            };

            public int Registrar(params VendaItem[] itens)
            {
                var id = Vendas.Inserir(new Venda
                {
                    IdFuncionario = IdFuncionario,
                    IdCaixa = IdCaixa,
                    FormaPagamentoVenda = FormaPagamento.Dinheiro,
                    Itens = itens.ToList()
                });
                VendasCriadas.Add(id);
                return id;
            }

            public int Estoque()
            {
                using var comando = new MySqlCommand(
                    "SELECT quantidade_prod FROM produto WHERE id_prod = @id", Conexao);
                comando.Parameters.AddWithValue("@id", IdProduto);
                return Convert.ToInt32(comando.ExecuteScalar());
            }

            public void Executar(string sql, int id)
            {
                using var comando = new MySqlCommand(sql, Conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }

            public void Dispose()
            {
                foreach (var id in VendasCriadas) Executar("DELETE FROM venda WHERE id_vend = @id", id);
                if (IdCaixa.HasValue) Executar("DELETE FROM caixa WHERE id_caixa = @id", IdCaixa.Value);
                Executar("DELETE FROM produto WHERE id_prod = @id", IdProduto);
                Executar("DELETE FROM funcionario WHERE id_fun = @id", IdFuncionario);
                Conexao.Dispose();
            }
        }

        [FatoComBanco]
        public void Editar_a_quantidade_acerta_o_estoque_pela_diferenca()
        {
            using var c = new Cenario(estoqueInicial: 10);
            var id = c.Registrar(c.Produto(2));
            Assert.Equal(8, c.Estoque());

            c.Vendas.Atualizar(new Venda
            {
                IdVenda = id,
                IdFuncionario = c.IdFuncionario,
                FormaPagamentoVenda = FormaPagamento.Pix,
                Itens = new List<VendaItem> { c.Produto(5) }
            });

            Assert.Equal(5, c.Estoque());

            var gravada = c.Vendas.BuscarPorId(id)!;
            Assert.Equal(150m, gravada.ValorVenda);
            Assert.Equal(FormaPagamento.Pix, gravada.FormaPagamentoVenda);
            Assert.Single(gravada.Itens);
        }

        [FatoComBanco]
        public void Cancelar_devolve_todas_as_linhas_do_mesmo_produto()
        {
            using var c = new Cenario(estoqueInicial: 10);

            // Duas linhas do mesmo produto, a precos diferentes, nao se somam
            // na comanda. O UPDATE com JOIN devolvia so uma delas.
            var id = c.Registrar(c.Produto(2, 30m), c.Produto(3, 25m));
            Assert.Equal(5, c.Estoque());

            c.Vendas.Cancelar(id, "teste", "teste");

            Assert.Equal(10, c.Estoque());
        }

        [FatoComBanco]
        public void Venda_de_caixa_fechado_so_permite_cliente_e_profissional()
        {
            using var c = new Cenario(estoqueInicial: 10);

            using (var comando = new MySqlCommand(@"
                INSERT INTO caixa (data_abertura, data_fechamento, valor_abertura, status)
                VALUES (NOW(), NOW(), 0, 'FECHADO');
                SELECT LAST_INSERT_ID();", c.Conexao))
            {
                c.IdCaixa = Convert.ToInt32(comando.ExecuteScalar());
            }

            var id = c.Registrar(c.Produto(1));

            var situacao = c.Vendas.SituacaoParaEditar(id);
            Assert.Equal(VendaDAO.ModoEdicao.Restrita, situacao.Modo);
            Assert.True(situacao.PodeTrocarProfissional);

            var erro = Assert.Throws<Exception>(() => c.Vendas.Atualizar(new Venda
            {
                IdVenda = id,
                IdFuncionario = c.IdFuncionario,
                FormaPagamentoVenda = FormaPagamento.Dinheiro,
                Itens = new List<VendaItem> { c.Produto(4) }
            }));

            Assert.Contains("caixa", erro.Message);
            Assert.Equal(9, c.Estoque());
        }
    }
}
