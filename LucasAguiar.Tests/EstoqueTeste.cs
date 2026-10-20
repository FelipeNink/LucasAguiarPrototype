using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// O estoque só serve se o extrato fechar com o saldo. Estes testes
    /// cobrem justamente isso: cada tipo de movimento, o saldo que fica
    /// depois, e a soma do extrato batendo com o número do produto.
    /// </summary>
    public class EstoqueTeste
    {
        private static EstoqueDAO Dao() => new(BancoDeTeste.Fabricar());

        /// <summary>Produto de teste com o saldo inicial pedido.</summary>
        private static int CriarProduto(MySqlConnection conexao, int saldo, int minimo = 0)
        {
            using var comando = new MySqlCommand(@"
                INSERT INTO produto (nome_prod, quantidade_prod, valor_prod,
                                     marca_prod, estoque_min_prod)
                VALUES (@nome, @saldo, 10.00, 'Teste', @minimo);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", BancoDeTeste.NomeUnico("Produto"));
            comando.Parameters.AddWithValue("@saldo", saldo);
            comando.Parameters.AddWithValue("@minimo", minimo);

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        private static void Apagar(MySqlConnection conexao, int idProduto)
        {
            using var comando = new MySqlCommand(
                "DELETE FROM produto WHERE id_prod = @id", conexao);

            comando.Parameters.AddWithValue("@id", idProduto);
            comando.ExecuteNonQuery();
        }

        private static int SaldoNoBanco(MySqlConnection conexao, int idProduto)
        {
            using var comando = new MySqlCommand(
                "SELECT COALESCE(quantidade_prod, 0) FROM produto WHERE id_prod = @id", conexao);

            comando.Parameters.AddWithValue("@id", idProduto);
            return Convert.ToInt32(comando.ExecuteScalar());
        }

        // =========================================================
        // CONSUMO E PERDA
        // =========================================================

        /// <summary>
        /// O buraco que fazia o saldo nunca bater: pomada usada no
        /// atendimento saía da prateleira e não do sistema.
        /// </summary>
        [FatoComBanco]
        public void Consumo_interno_baixa_o_saldo_e_fica_no_extrato()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 10);

            try
            {
                var dao = Dao();
                var saldo = dao.Consumir(id, 3, "Usado nos cortes do dia", "Teste");

                Assert.Equal(7, saldo);
                Assert.Equal(7, SaldoNoBanco(conexao, id));

                var extrato = dao.Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id });
                var movimento = extrato.First();

                Assert.Equal(TipoMovimentoEstoque.Consumo, movimento.Tipo);
                Assert.Equal(-3, movimento.Quantidade);
                Assert.Equal(7, movimento.SaldoApos);
                Assert.Equal("Usado nos cortes do dia", movimento.Motivo);
                Assert.Equal("Teste", movimento.Usuario);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        [FatoComBanco]
        public void Perda_exige_motivo_e_quantidade_positiva()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 5);

            try
            {
                var dao = Dao();

                Assert.Throws<Exception>(() => dao.RegistrarPerda(id, 1, "", "Teste"));
                Assert.Throws<Exception>(() => dao.RegistrarPerda(id, 0, "Quebrou", "Teste"));
                Assert.Throws<Exception>(() => dao.Consumir(id, -2, "Negativo", "Teste"));

                // Nenhuma das recusas pode ter mexido no saldo.
                Assert.Equal(5, SaldoNoBanco(conexao, id));

                Assert.Equal(3, dao.RegistrarPerda(id, 2, "Frasco quebrou", "Teste"));
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        // =========================================================
        // INVENTARIO
        // =========================================================

        [FatoComBanco]
        public void Inventario_grava_a_diferenca_e_nao_o_numero_contado()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 10);

            try
            {
                var dao = Dao();

                // Contou 7 onde o sistema dizia 10: faltam 3.
                Assert.Equal(7, dao.Inventariar(id, 7, "Conferência mensal", "Teste"));

                var movimento = dao.Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id }).First();

                Assert.Equal(TipoMovimentoEstoque.Ajuste, movimento.Tipo);
                Assert.Equal(-3, movimento.Quantidade);
                Assert.Equal(7, movimento.SaldoApos);
                Assert.Contains("Contagem: 7", movimento.Motivo);
                Assert.Contains("tinha 10", movimento.Motivo);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        /// <summary>Conferir e estar certo não é um evento: não gera linha.</summary>
        [FatoComBanco]
        public void Inventario_que_bate_nao_gera_movimento()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 8);

            try
            {
                var dao = Dao();
                var antes = dao.Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id }).Count;

                Assert.Equal(8, dao.Inventariar(id, 8, null, "Teste"));

                var depois = dao.Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id }).Count;

                Assert.Equal(antes, depois);
                Assert.Equal(8, SaldoNoBanco(conexao, id));
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        [FatoComBanco]
        public void Inventario_recusa_contagem_negativa()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 4);

            try
            {
                Assert.Throws<Exception>(() => Dao().Inventariar(id, -1, null, "Teste"));
                Assert.Equal(4, SaldoNoBanco(conexao, id));
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        // =========================================================
        // SALDO NEGATIVO
        // =========================================================

        /// <summary>
        /// Antes, a baixa da venda usava GREATEST(0, saldo - qtd): vender
        /// cinco tendo dois deixava zero e a diferença sumia. Agora o
        /// saldo vai a negativo, que é o que torna o problema visível.
        /// </summary>
        [FatoComBanco]
        public void Saldo_pode_ficar_negativo_em_vez_de_parar_no_zero()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 2);

            try
            {
                var dao = Dao();
                var saldo = dao.Registrar(id, TipoMovimentoEstoque.Saida, -5, "Venda", null, "Teste");

                Assert.Equal(-3, saldo);
                Assert.Equal(-3, SaldoNoBanco(conexao, id));

                var situacao = dao.Saldos().First(s => s.IdProduto == id);

                Assert.Equal("negativo", situacao.Situacao);
                Assert.True(situacao.PedeAtencao);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        // =========================================================
        // O EXTRATO FECHA COM O SALDO
        // =========================================================

        /// <summary>
        /// O teste que dá sentido a todos os outros: depois de uma
        /// sequência de movimentos, a soma do extrato tem de ser
        /// exatamente o saldo do produto.
        /// </summary>
        [FatoComBanco]
        public void Soma_do_extrato_bate_com_o_saldo_do_produto()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 0);

            try
            {
                var dao = Dao();

                dao.Registrar(id, TipoMovimentoEstoque.Entrada, 24, "Compra", null, "Teste");
                dao.Registrar(id, TipoMovimentoEstoque.Saida, -5, "Venda", null, "Teste");
                dao.Consumir(id, 3, "Atendimento", "Teste");
                dao.RegistrarPerda(id, 1, "Venceu", "Teste");
                dao.Registrar(id, TipoMovimentoEstoque.Devolucao, 2, "Venda cancelada", null, "Teste");
                dao.Inventariar(id, 16, "Contagem", "Teste");

                var extrato = dao.Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id, Limite = 500 });
                var somaDosMovimentos = extrato.Sum(m => m.Quantidade);

                Assert.Equal(16, SaldoNoBanco(conexao, id));
                Assert.Equal(16, somaDosMovimentos);

                // E cada linha registrou o saldo correto no momento dela:
                // a mais recente tem de ser o saldo de agora.
                Assert.Equal(16, extrato.First().SaldoApos);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        // =========================================================
        // MINIMO E ALERTA
        // =========================================================

        [FatoComBanco]
        public void Produto_no_minimo_ou_abaixo_pede_atencao()
        {
            using var conexao = BancoDeTeste.Abrir();
            var noMinimo = CriarProduto(conexao, 3, minimo: 3);
            var acima = CriarProduto(conexao, 10, minimo: 3);
            var zerado = CriarProduto(conexao, 0);

            try
            {
                var dao = Dao();
                var todos = dao.Saldos();

                Assert.Equal("baixo", todos.First(s => s.IdProduto == noMinimo).Situacao);
                Assert.Equal("ok", todos.First(s => s.IdProduto == acima).Situacao);
                Assert.Equal("zerado", todos.First(s => s.IdProduto == zerado).Situacao);

                var atencao = dao.PedindoAtencao(100).Select(s => s.IdProduto).ToList();

                Assert.Contains(noMinimo, atencao);
                Assert.Contains(zerado, atencao);
                Assert.DoesNotContain(acima, atencao);
            }
            finally
            {
                Apagar(conexao, noMinimo);
                Apagar(conexao, acima);
                Apagar(conexao, zerado);
            }
        }

        [FatoComBanco]
        public void Minimo_zero_desliga_o_aviso()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 1, minimo: 5);

            try
            {
                var dao = Dao();
                Assert.Equal("baixo", dao.Saldos().First(s => s.IdProduto == id).Situacao);

                dao.DefinirMinimo(id, 0);

                Assert.Equal("ok", dao.Saldos().First(s => s.IdProduto == id).Situacao);
                Assert.Throws<Exception>(() => dao.DefinirMinimo(id, -1));
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        // =========================================================
        // A LIGACAO COM A VENDA
        // =========================================================

        /// <summary>
        /// A venda tem de dar baixa pelo mesmo caminho do resto, deixando
        /// a linha no extrato. Antes ela fazia um UPDATE proprio e o
        /// estoque mudava sem ninguem saber por quê.
        ///
        /// Cobre também o fim do GREATEST(0, …): a venda é de cinco com
        /// dois em estoque, e o saldo tem de ficar em -3.
        /// </summary>
        [FatoComBanco]
        public void Venda_de_produto_baixa_o_estoque_e_deixa_rastro()
        {
            using var conexao = BancoDeTeste.Abrir();
            var idProduto = CriarProduto(conexao, 2);
            var idFuncionario = CriarFuncionario(conexao);

            try
            {
                var vendas = new VendaDAO(BancoDeTeste.Fabricar());
                var estoque = Dao();

                var idVenda = vendas.Inserir(new Venda
                {
                    DataVenda = DateTime.Now,
                    FormaPagamentoVenda = FormaPagamento.Dinheiro,
                    ValorVenda = 50m,
                    IdFuncionario = idFuncionario,
                    Usuario = "Teste",
                    Itens =
                    {
                        new VendaItem
                        {
                            TipoItem = TipoItem.Produto,
                            IdReferencia = idProduto,
                            Descricao = "Produto de teste",
                            Quantidade = 5,
                            ValorUnitario = 10m
                        }
                    }
                });

                Assert.Equal(-3, SaldoNoBanco(conexao, idProduto));

                var movimento = estoque
                    .Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = idProduto })
                    .First();

                Assert.Equal(TipoMovimentoEstoque.Saida, movimento.Tipo);
                Assert.Equal(-5, movimento.Quantidade);
                Assert.Equal(-3, movimento.SaldoApos);
                Assert.Equal(idVenda, movimento.IdReferencia);
                Assert.Equal("Teste", movimento.Usuario);

                // E cancelar devolve, com a sua propria linha.
                vendas.Cancelar(idVenda, "Teste de cancelamento", "Teste");

                Assert.Equal(2, SaldoNoBanco(conexao, idProduto));

                var devolucao = estoque
                    .Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = idProduto })
                    .First();

                Assert.Equal(TipoMovimentoEstoque.Devolucao, devolucao.Tipo);
                Assert.Equal(5, devolucao.Quantidade);
                Assert.Equal(2, devolucao.SaldoApos);
            }
            finally
            {
                LimparVendasDoProduto(conexao, idProduto);
                Apagar(conexao, idProduto);
                ApagarFuncionario(conexao, idFuncionario);
            }
        }

        private static int CriarFuncionario(MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(@"
                INSERT INTO funcionario (nome_fun, salario_fixo)
                VALUES (@nome, 0);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", BancoDeTeste.NomeUnico("Profissional"));
            return Convert.ToInt32(comando.ExecuteScalar());
        }

        private static void ApagarFuncionario(MySqlConnection conexao, int id)
        {
            using var comando = new MySqlCommand(
                "DELETE FROM funcionario WHERE id_fun = @id", conexao);

            comando.Parameters.AddWithValue("@id", id);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Editar o cadastro do produto e mudar a quantidade vira um
        /// ajuste no extrato. Era o quarto caminho que mexia no saldo sem
        /// deixar rastro.
        /// </summary>
        [FatoComBanco]
        public void Editar_o_produto_mudando_a_quantidade_grava_ajuste()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 10);

            try
            {
                var produtos = new ProdutoDAO(BancoDeTeste.Fabricar());
                var produto = produtos.BuscarPorId(id)!;

                produto.Quantidade = 25;
                produtos.Atualizar(produto, "Teste");

                Assert.Equal(25, SaldoNoBanco(conexao, id));

                var movimento = Dao()
                    .Extrato(new EstoqueDAO.FiltroExtrato { IdProduto = id })
                    .First();

                Assert.Equal(TipoMovimentoEstoque.Ajuste, movimento.Tipo);
                Assert.Equal(15, movimento.Quantidade);
                Assert.Equal(25, movimento.SaldoApos);
                Assert.Contains("10 → 25", movimento.Motivo);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }

        /// <summary>Apaga as vendas de teste para a chave estrangeira liberar o produto.</summary>
        private static void LimparVendasDoProduto(MySqlConnection conexao, int idProduto)
        {
            using var comando = new MySqlCommand(@"
                DELETE v FROM venda v
                 WHERE EXISTS (SELECT 1 FROM venda_item i
                                WHERE i.id_vend_fk = v.id_vend
                                  AND i.id_ref = @produto
                                  AND i.tipo_item = 'PRODUTO')", conexao);

            comando.Parameters.AddWithValue("@produto", idProduto);
            comando.ExecuteNonQuery();
        }

        // =========================================================
        // CONSUMO POR PRODUTO
        // =========================================================

        [FatoComBanco]
        public void Consumo_por_produto_soma_consumo_e_perda_no_periodo()
        {
            using var conexao = BancoDeTeste.Abrir();
            var id = CriarProduto(conexao, 20);

            try
            {
                var dao = Dao();

                dao.Consumir(id, 4, "Atendimento", "Teste");
                dao.RegistrarPerda(id, 2, "Quebrou", "Teste");
                dao.Registrar(id, TipoMovimentoEstoque.Saida, -3, "Venda", null, "Teste");

                var consumo = dao.ConsumoPorProduto(
                    DateTime.Today, DateTime.Today.AddDays(1));

                var linha = consumo.First(c => c.Quantidade == 6);

                // Venda nao entra: ela virou receita, nao consumo.
                Assert.Equal(6, linha.Quantidade);
                Assert.Equal(60m, linha.Custo);
            }
            finally
            {
                Apagar(conexao, id);
            }
        }
    }
}
