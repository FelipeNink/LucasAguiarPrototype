using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Gravacao do link publico contra um MySQL de verdade: a disputa pelo
    /// horario e decidida pela trava da transacao, e isso so o banco prova.
    /// Liga a configuracao do banco de teste e devolve como estava.
    /// </summary>
    public class AgendaOnlineBancoTeste
    {
        private sealed class Cenario : IDisposable
        {
            public readonly MySqlConnection Conexao = BancoDeTeste.Abrir();
            public readonly AgendaOnlineDAO Online = new(BancoDeTeste.Fabricar());
            public readonly int IdFuncionario;
            public readonly int IdServico;
            public readonly DateTime Horario;
            private readonly Dictionary<string, object?> _config = new();

            public Cenario(int maximoAbertos = 2)
            {
                var fabrica = BancoDeTeste.Fabricar();
                IdFuncionario = new FuncionarioDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("prof"));
                IdServico = new ServicoDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("corte"), 40m, 0m, 30);
                Executar("UPDATE funcionario SET online_fun = 1 WHERE id_fun = @id", IdFuncionario);
                Executar("UPDATE servico SET online_serv = 1 WHERE id_serv = @id", IdServico);

                using (var ler = new MySqlCommand("SELECT * FROM agenda_online WHERE id = 1", Conexao))
                using (var leitor = ler.ExecuteReader())
                {
                    leitor.Read();
                    for (var i = 0; i < leitor.FieldCount; i++) _config[leitor.GetName(i)] = leitor.GetValue(i);
                }

                using (var ligar = new MySqlCommand(@"
                    UPDATE agenda_online SET ativo = 1, antecedencia_min = 0, dias_a_frente = 30,
                           intervalo_min = 30, maximo_abertos = @maximo WHERE id = 1", Conexao))
                {
                    ligar.Parameters.AddWithValue("@maximo", maximoAbertos);
                    ligar.ExecuteNonQuery();
                }

                // Primeiro dia aberto da grade a partir de depois de amanha, na abertura.
                var grade = new HorarioDAO(fabrica).Listar();
                var dia = Enumerable.Range(2, 7).Select(i => DateTime.Today.AddDays(i))
                    .First(d => grade[(int)d.DayOfWeek].Aberto);
                Horario = dia + grade[(int)dia.DayOfWeek].Abre;
            }

            public PedidoOnline Pedido(string telefone, DateTime? quando = null) => new()
            {
                Servicos = new() { IdServico },
                IdFuncionario = IdFuncionario,
                Inicio = quando ?? Horario,
                Nome = "Cliente Teste",
                Telefone = telefone
            };

            public void Executar(string sql, int id)
            {
                using var comando = new MySqlCommand(sql, Conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }

            public List<int> ClientesDosAgendamentos()
            {
                var lista = new List<int>();
                using var comando = new MySqlCommand(
                    "SELECT DISTINCT id_cli_fk FROM agendamento WHERE id_fun_fk = @id", Conexao);
                comando.Parameters.AddWithValue("@id", IdFuncionario);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read()) lista.Add(leitor.GetInt32(0));
                return lista;
            }

            public void Dispose()
            {
                var clientes = ClientesDosAgendamentos();
                Executar("DELETE ags FROM agendamento_servico ags JOIN agendamento a ON a.id_agend = ags.id_agend_fk WHERE a.id_fun_fk = @id", IdFuncionario);
                Executar("DELETE FROM agendamento WHERE id_fun_fk = @id", IdFuncionario);
                foreach (var id in clientes)
                    Executar("DELETE FROM cliente WHERE id_cli = @id AND nome_cli = 'Cliente Teste'", id);
                Executar("DELETE FROM servico WHERE id_serv = @id", IdServico);
                Executar("DELETE FROM funcionario WHERE id_fun = @id", IdFuncionario);

                using (var voltar = new MySqlCommand(@"
                    UPDATE agenda_online SET ativo = @ativo, antecedencia_min = @antecedencia, dias_a_frente = @dias,
                           intervalo_min = @intervalo, maximo_abertos = @maximo WHERE id = 1", Conexao))
                {
                    voltar.Parameters.AddWithValue("@ativo", _config["ativo"]);
                    voltar.Parameters.AddWithValue("@antecedencia", _config["antecedencia_min"]);
                    voltar.Parameters.AddWithValue("@dias", _config["dias_a_frente"]);
                    voltar.Parameters.AddWithValue("@intervalo", _config["intervalo_min"]);
                    voltar.Parameters.AddWithValue("@maximo", _config["maximo_abertos"]);
                    voltar.ExecuteNonQuery();
                }

                Conexao.Dispose();
            }
        }

        private static string Telefone() => "4899" + Random.Shared.Next(1000000, 9999999);

        [FatoComBanco]
        public void Dois_clientes_no_mesmo_horario_so_um_leva()
        {
            using var c = new Cenario();

            var pedidos = new[] { c.Pedido(Telefone()), c.Pedido(Telefone()), c.Pedido(Telefone()) };
            var resultados = pedidos.AsParallel().Select(p =>
            {
                try
                {
                    return (Ok: true, Ocupado: false, Id: c.Online.Agendar(p, DateTime.Now).IdAgendamento);
                }
                catch (AgendaOnlineException ex)
                {
                    return (Ok: false, Ocupado: ex.HorarioOcupado, Id: 0);
                }
            }).ToList();

            Assert.Equal(1, resultados.Count(r => r.Ok));
            Assert.All(resultados.Where(r => !r.Ok), r => Assert.True(r.Ocupado));
        }

        [FatoComBanco]
        public void Grava_como_online_com_token_e_acha_o_cliente_pelo_telefone()
        {
            using var c = new Cenario();
            var telefone = Telefone();

            var primeiro = c.Online.Agendar(c.Pedido(telefone), DateTime.Now);
            var segundo = c.Online.Agendar(
                c.Pedido("(" + telefone[..2] + ") " + telefone[2..7] + "-" + telefone[7..], c.Horario.AddHours(2)), DateTime.Now);

            var agendamentos = new AgendamentoDAO(BancoDeTeste.Fabricar());
            var a = agendamentos.BuscarPorToken(primeiro.Token)!;
            var b = agendamentos.BuscarPorId(segundo.IdAgendamento)!;

            Assert.True(a.Online);
            Assert.Equal(StatusAgendamento.Agendado, a.Status);
            Assert.Equal(30, a.DuracaoMin);
            Assert.Equal(a.IdCliente, b.IdCliente);
        }

        [FatoComBanco]
        public void Limite_por_telefone_e_horario_que_cruza_sao_recusados()
        {
            using var c = new Cenario(maximoAbertos: 1);
            var telefone = Telefone();

            c.Online.Agendar(c.Pedido(telefone), DateTime.Now);

            var limite = Assert.Throws<AgendaOnlineException>(() =>
                c.Online.Agendar(c.Pedido(telefone, c.Horario.AddHours(3)), DateTime.Now));
            Assert.False(limite.HorarioOcupado);
        }

        [FatoComBanco]
        public void Bloqueio_do_profissional_impede_o_horario()
        {
            using var c = new Cenario();
            new JornadaDAO(BancoDeTeste.Fabricar()).SalvarBloqueios(c.IdFuncionario,
                new[] { new BloqueioFuncionario { Inicio = c.Horario, Fim = c.Horario.AddHours(1), Motivo = "teste" } },
                Array.Empty<int>());

            var erro = Assert.Throws<AgendaOnlineException>(() => c.Online.Agendar(c.Pedido(Telefone()), DateTime.Now));
            Assert.True(erro.HorarioOcupado);
        }

        [FatoComBanco]
        public void Desligado_recusa()
        {
            using var c = new Cenario();
            c.Executar("UPDATE agenda_online SET ativo = 0 WHERE id = @id", 1);

            Assert.Throws<AgendaOnlineException>(() => c.Online.Agendar(c.Pedido(Telefone()), DateTime.Now));
        }
    }
}
