using LucasAguiar.Configs;
using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>Avaliacao pos-atendimento e consentimento de WhatsApp.</summary>
    public class AvaliacaoTeste
    {
        // =========================================================
        // Sem banco
        // =========================================================

        private static Avaliacao Nota(string profissional, int nota) =>
            new() { NomeFuncionario = profissional, Nota = nota };

        [Fact]
        public void Media_e_distribuicao_por_profissional()
        {
            var linhas = AvaliacaoDAO.PorProfissional(new[]
            {
                Nota("Diego", 5), Nota("Diego", 4), Nota("Diego", 5),
                Nota("Luis", 2), Nota("Luis", 4)
            });

            Assert.Equal("Diego", linhas[0].Nome);
            Assert.Equal(4.7m, linhas[0].Media);
            Assert.Equal(2, linhas[0].PorNota[5]);
            Assert.Equal(3.0m, linhas[1].Media);
            Assert.Equal(2, linhas[1].Quantidade);
        }

        [Fact]
        public void Nao_respondida_fica_fora_da_media()
        {
            var linhas = AvaliacaoDAO.PorProfissional(new[] { Nota("Diego", 5), new Avaliacao { NomeFuncionario = "Diego" } });

            Assert.Equal(1, linhas.Single().Quantidade);
        }

        [Fact]
        public void Estrelas_e_validade_da_nota()
        {
            Assert.Equal("★★★☆☆", NotaAvaliacao.Estrelas(3));
            Assert.False(NotaAvaliacao.Valida(0));
            Assert.False(NotaAvaliacao.Valida(6));
            Assert.True(NotaAvaliacao.Valida(5));
        }

        [Fact]
        public void Mensagem_leva_o_link_e_o_profissional()
        {
            var texto = WhatsApp.PedidoAvaliacao("joão silva", "Diego Martins", "https://x/avaliar/abc");

            Assert.StartsWith("Olá, João!", texto);
            Assert.Contains("com Diego", texto);
            Assert.EndsWith("https://x/avaliar/abc", texto);
        }

        [Fact]
        public void Link_vence_trinta_dias_depois_da_venda()
        {
            var a = new Avaliacao { DataVenda = new DateTime(2026, 10, 1, 10, 0, 0) };

            Assert.False(a.Vencida(new DateTime(2026, 10, 31, 9, 0, 0)));
            Assert.True(a.Vencida(new DateTime(2026, 10, 31, 11, 0, 0)));
        }

        // =========================================================
        // Com banco
        // =========================================================

        private sealed class Cenario : IDisposable
        {
            public readonly MySqlConnection Conexao = BancoDeTeste.Abrir();
            public readonly AvaliacaoDAO Avaliacoes = new(BancoDeTeste.Fabricar());
            public readonly ClienteDAO Clientes = new(BancoDeTeste.Fabricar());
            public readonly int IdFuncionario;
            public readonly int IdOutro;
            public readonly int IdCliente;
            private readonly List<int> _vendas = new();

            public Cenario()
            {
                var fabrica = BancoDeTeste.Fabricar();
                IdFuncionario = new FuncionarioDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("prof"));
                IdOutro = new FuncionarioDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("caixa"));
                IdCliente = Clientes.InserirSomenteNome(BancoDeTeste.NomeUnico("cliente"));
            }

            /// <summary>Venda com um servico feito por IdFuncionario, lancada por IdOutro.</summary>
            public int Vender(DateTime quando, string status = "CONFIRMADA")
            {
                int id;
                using (var venda = new MySqlCommand(@"
                    INSERT INTO venda (valor_vend, data_vend, status_vend, id_fun_fk, id_cli_fk)
                    VALUES (40, @quando, @status, @outro, @cliente); SELECT LAST_INSERT_ID();", Conexao))
                {
                    venda.Parameters.AddWithValue("@quando", quando);
                    venda.Parameters.AddWithValue("@status", status);
                    venda.Parameters.AddWithValue("@outro", IdOutro);
                    venda.Parameters.AddWithValue("@cliente", IdCliente);
                    id = Convert.ToInt32(venda.ExecuteScalar());
                }

                using (var item = new MySqlCommand(@"
                    INSERT INTO venda_item (id_vend_fk, tipo_item, descricao, valor_unit, valor_total, id_fun_fk)
                    VALUES (@id, 'SERVICO', 'Corte', 40, 40, @fun)", Conexao))
                {
                    item.Parameters.AddWithValue("@id", id);
                    item.Parameters.AddWithValue("@fun", IdFuncionario);
                    item.ExecuteNonQuery();
                }

                _vendas.Add(id);
                return id;
            }

            public void Executar(string sql, int id)
            {
                using var comando = new MySqlCommand(sql, Conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }

            public void Dispose()
            {
                foreach (var id in _vendas) Executar("DELETE FROM venda WHERE id_vend = @id", id);
                Executar("DELETE FROM cliente WHERE id_cli = @id", IdCliente);
                Executar("DELETE FROM funcionario WHERE id_fun = @id", IdFuncionario);
                Executar("DELETE FROM funcionario WHERE id_fun = @id", IdOutro);
                Conexao.Dispose();
            }
        }

        [FatoComBanco]
        public void Um_link_por_venda_com_o_profissional_do_servico()
        {
            using var c = new Cenario();
            var venda = c.Vender(DateTime.Now.AddHours(-1));

            var token = c.Avaliacoes.GarantirToken(venda)!;

            Assert.Equal(32, token.Length);
            Assert.Equal(token, c.Avaliacoes.GarantirToken(venda));

            var avaliacao = c.Avaliacoes.BuscarPorToken(token)!;
            Assert.Equal("Corte", avaliacao.Servicos);
            using var quem = new MySqlCommand("SELECT id_fun_fk FROM avaliacao WHERE token = @t", c.Conexao);
            quem.Parameters.AddWithValue("@t", token);
            Assert.Equal(c.IdFuncionario, Convert.ToInt32(quem.ExecuteScalar()));
        }

        [FatoComBanco]
        public void Responde_uma_vez_so()
        {
            using var c = new Cenario();
            var token = c.Avaliacoes.GarantirToken(c.Vender(DateTime.Now.AddHours(-1)))!;

            Assert.True(c.Avaliacoes.Responder(token, 2, "  Demorou muito  "));
            Assert.False(c.Avaliacoes.Responder(token, 5, null));

            var a = c.Avaliacoes.BuscarPorToken(token)!;
            Assert.Equal(2, a.Nota);
            Assert.Equal("Demorou muito", a.Comentario);
            Assert.Contains(c.Avaliacoes.Respondidas(DateTime.Today, DateTime.Now.AddMinutes(1), c.IdFuncionario, notaAte: 3),
                r => r.Token == token);
        }

        [FatoComBanco]
        public void Venda_cancelada_ou_antiga_nao_aceita_resposta()
        {
            using var c = new Cenario();

            Assert.Null(c.Avaliacoes.GarantirToken(c.Vender(DateTime.Now, "CANCELADA")));

            var antiga = c.Avaliacoes.GarantirToken(c.Vender(DateTime.Now.AddDays(-NotaAvaliacao.PrazoDias - 1)))!;
            Assert.False(c.Avaliacoes.Responder(antiga, 5, null));
        }

        [FatoComBanco]
        public void Consentimento_guarda_quando_mudou_e_nao_mexe_se_nao_mudou()
        {
            using var c = new Cenario();

            var cliente = c.Clientes.BuscarPorId(c.IdCliente)!;
            Assert.Null(cliente.AceitaWhatsApp);
            Assert.Null(cliente.AceitaWhatsAppEm);

            cliente.AceitaWhatsApp = false;
            c.Clientes.Atualizar(cliente);
            c.Executar("UPDATE cliente SET whatsapp_ok_em = '2026-01-01' WHERE id_cli = @id", c.IdCliente);

            // Salvar de novo sem mudar a resposta mantem a data da resposta.
            cliente = c.Clientes.BuscarPorId(c.IdCliente)!;
            cliente.Observacoes = "editou outra coisa";
            c.Clientes.Atualizar(cliente);
            cliente = c.Clientes.BuscarPorId(c.IdCliente)!;
            Assert.True(cliente.RecusouWhatsApp);
            Assert.Equal(new DateTime(2026, 1, 1), cliente.AceitaWhatsAppEm);
            Assert.Equal(OrigemConsentimento.Cadastro, cliente.AceitaWhatsAppOrigem);

            // Mudar a resposta grava a data nova.
            cliente.AceitaWhatsApp = true;
            c.Clientes.Atualizar(cliente);
            Assert.Equal(DateTime.Today, c.Clientes.BuscarPorId(c.IdCliente)!.AceitaWhatsAppEm?.Date);
        }
    }
}
