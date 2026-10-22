using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Confirmar e cancelar pelo link do lembrete, contra um MySQL de
    /// verdade: quem decide se o link ainda vale e o WHERE do banco.
    /// </summary>
    public class ConfirmacaoTeste
    {
        private sealed class Cenario : IDisposable
        {
            public readonly MySqlConnection Conexao = BancoDeTeste.Abrir();
            public readonly AgendamentoDAO Agendamentos = new(BancoDeTeste.Fabricar());
            public readonly int IdFuncionario;
            public readonly int IdServico;
            public readonly int IdCliente;
            public readonly List<int> Criados = new();

            public Cenario()
            {
                var fabrica = BancoDeTeste.Fabricar();
                IdFuncionario = new FuncionarioDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("prof"));
                IdServico = new ServicoDAO(fabrica).InserirRapido(BancoDeTeste.NomeUnico("corte"), 40m, 0m, 30);
                IdCliente = new ClienteDAO(fabrica).InserirSomenteNome(BancoDeTeste.NomeUnico("cliente"));
            }

            public int Agendar(DateTime quando)
            {
                var id = Agendamentos.Inserir(new Agendamento
                {
                    DataHora = quando,
                    DuracaoMin = 30,
                    IdCliente = IdCliente,
                    IdFuncionario = IdFuncionario,
                    IdServico = IdServico
                });
                Criados.Add(id);
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
                foreach (var id in Criados)
                {
                    Executar("DELETE FROM agendamento_servico WHERE id_agend_fk = @id", id);
                    Executar("DELETE FROM agendamento WHERE id_agend = @id", id);
                }
                Executar("DELETE FROM cliente WHERE id_cli = @id", IdCliente);
                Executar("DELETE FROM servico WHERE id_serv = @id", IdServico);
                Executar("DELETE FROM funcionario WHERE id_fun = @id", IdFuncionario);
                Conexao.Dispose();
            }
        }

        private static DateTime Amanha => DateTime.Today.AddDays(1).AddHours(10);

        [FatoComBanco]
        public void Token_e_criado_uma_vez_e_acha_o_agendamento()
        {
            using var c = new Cenario();
            var id = c.Agendar(Amanha);

            var token = c.Agendamentos.GarantirToken(id);

            Assert.Equal(32, token.Length);
            Assert.Equal(token, c.Agendamentos.GarantirToken(id));
            Assert.Equal(id, c.Agendamentos.BuscarPorToken(token)?.IdAgendamento);
            Assert.Null(c.Agendamentos.BuscarPorToken(new string('0', 32)));
        }

        [FatoComBanco]
        public void Confirmar_marca_sem_tirar_da_agenda()
        {
            using var c = new Cenario();
            var id = c.Agendar(Amanha);
            var token = c.Agendamentos.GarantirToken(id);

            Assert.True(c.Agendamentos.ConfirmarPeloCliente(token));

            var lido = c.Agendamentos.BuscarPorId(id)!;
            Assert.True(lido.Confirmado);
            Assert.Equal(StatusAgendamento.Agendado, lido.Status);

            // Continua ocupando o horario: outro cliente no mesmo horario e recusado.
            Assert.ThrowsAny<Exception>(() => c.Agendar(Amanha));
        }

        [FatoComBanco]
        public void Cancelar_libera_o_horario_e_marca_que_foi_o_cliente()
        {
            using var c = new Cenario();
            var id = c.Agendar(Amanha);
            var token = c.Agendamentos.GarantirToken(id);

            Assert.True(c.Agendamentos.CancelarPeloCliente(token));

            var lido = c.Agendamentos.BuscarPorId(id)!;
            Assert.Equal(StatusAgendamento.Cancelado, lido.Status);
            Assert.NotNull(lido.CanceladoClienteEm);
            Assert.Contains(c.Agendamentos.CanceladosPeloCliente(DateTime.Now.AddMinutes(-5)), a => a.IdAgendamento == id);

            // O horario ficou livre, e o link nao confirma mais nada.
            c.Agendar(Amanha);
            Assert.False(c.Agendamentos.ConfirmarPeloCliente(token));
        }

        [FatoComBanco]
        public void Horario_que_ja_passou_nao_aceita_confirmar_nem_cancelar()
        {
            using var c = new Cenario();
            var id = c.Agendar(DateTime.Now.AddHours(-2));
            var token = c.Agendamentos.GarantirToken(id);

            Assert.False(c.Agendamentos.ConfirmarPeloCliente(token));
            Assert.False(c.Agendamentos.CancelarPeloCliente(token));
        }

        [FatoComBanco]
        public void Remarcar_pela_equipe_desfaz_a_confirmacao()
        {
            using var c = new Cenario();
            var id = c.Agendar(Amanha);
            c.Agendamentos.ConfirmarPeloCliente(c.Agendamentos.GarantirToken(id));

            var agendamento = c.Agendamentos.BuscarPorId(id)!;
            agendamento.DataHora = Amanha.AddHours(2);
            c.Agendamentos.Atualizar(agendamento);

            Assert.False(c.Agendamentos.BuscarPorId(id)!.Confirmado);
        }

        [FatoComBanco]
        public void Editar_sem_mudar_hora_mantem_a_confirmacao()
        {
            using var c = new Cenario();
            var id = c.Agendar(Amanha);
            c.Agendamentos.ConfirmarPeloCliente(c.Agendamentos.GarantirToken(id));

            var agendamento = c.Agendamentos.BuscarPorId(id)!;
            agendamento.Observacao = "Trazer foto do corte";
            c.Agendamentos.Atualizar(agendamento);

            Assert.True(c.Agendamentos.BuscarPorId(id)!.Confirmado);
        }
    }
}
