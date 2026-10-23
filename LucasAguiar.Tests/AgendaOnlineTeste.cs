using LucasAguiar.Models;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Horarios livres do link publico, sem banco. Barbearia de segunda a
    /// sabado das 9h as 19h, domingo fechada. 19/10/2026 e uma segunda.
    /// </summary>
    public class AgendaOnlineTeste
    {
        private static readonly DateTime Segunda = new(2026, 10, 19);
        private static readonly DateTime Domingo = new(2026, 10, 25);
        private static readonly DateTime Antes = new(2026, 1, 1);
        private const int Luis = 1, Beto = 2;

        private static List<HorarioDia> Grade() =>
            Enum.GetValues<DayOfWeek>()
                .Select(d => new HorarioDia { Dia = d, Aberto = d != DayOfWeek.Sunday, Abre = new(9, 0, 0), Fecha = new(19, 0, 0) })
                .ToList();

        /// <summary>Jornada de segunda a sabado, almoco 12h-13h, folga no domingo.</summary>
        private static List<JornadaDia> Jornada(int entrada = 9, int saida = 18) =>
            Enum.GetValues<DayOfWeek>()
                .Select(d => new JornadaDia
                {
                    Dia = d,
                    Trabalha = d != DayOfWeek.Sunday,
                    Entrada = new(entrada, 0, 0),
                    Saida = new(saida, 0, 0),
                    AlmocoInicio = new(12, 0, 0),
                    AlmocoFim = new(13, 0, 0)
                })
                .ToList();

        private static QuadroAgenda Quadro(
            List<JornadaDia>? jornada = null,
            List<Periodo>? ocupados = null,
            Dictionary<DateTime, bool>? excecoes = null) => new()
        {
            Grade = Grade(),
            Excecoes = excecoes ?? new Dictionary<DateTime, bool>(),
            Jornadas = new Dictionary<int, List<JornadaDia>?> { [Luis] = jornada },
            Ocupados = new Dictionary<int, List<Periodo>> { [Luis] = ocupados ?? new List<Periodo>() }
        };

        private static DateTime Em(int hora, int minuto = 0) => Segunda.AddHours(hora).AddMinutes(minuto);

        private static List<string> Horas(IEnumerable<DateTime> lista) => lista.Select(h => h.ToString("HH:mm")).ToList();

        [Fact]
        public void Sem_jornada_segue_o_expediente_da_barbearia()
        {
            var livres = Quadro().Livres(Luis, Segunda, 30, 30, Antes);

            Assert.Equal("09:00", livres.First().ToString("HH:mm"));
            Assert.Equal("18:30", livres.Last().ToString("HH:mm"));
            Assert.Equal(20, livres.Count);
        }

        [Fact]
        public void Servico_que_nao_cabe_antes_de_fechar_nao_aparece()
        {
            var livres = Quadro().Livres(Luis, Segunda, 60, 30, Antes);

            Assert.Equal("18:00", livres.Last().ToString("HH:mm"));
        }

        [Fact]
        public void Almoco_impede_e_encostar_nele_nao()
        {
            var horas = Horas(Quadro(Jornada()).Livres(Luis, Segunda, 30, 30, Antes));

            Assert.Contains("11:30", horas);
            Assert.DoesNotContain("12:00", horas);
            Assert.DoesNotContain("12:30", horas);
            Assert.Contains("13:00", horas);
            Assert.Equal("17:30", horas.Last());
        }

        [Fact]
        public void Bloqueio_impede_o_horario_que_cruza_com_ele()
        {
            var bloqueio = new Periodo(Em(15), Em(16));
            var horas = Horas(Quadro(ocupados: new() { bloqueio }).Livres(Luis, Segunda, 30, 15, Antes));

            Assert.Contains("14:30", horas);
            Assert.DoesNotContain("14:45", horas);
            Assert.DoesNotContain("15:30", horas);
            Assert.Contains("16:00", horas);
        }

        [Fact]
        public void Logo_ao_fim_de_um_atendimento_entra_um_horario_fora_da_grade()
        {
            var corte = new Periodo(Em(10), Em(10, 40));
            var horas = Horas(Quadro(ocupados: new() { corte }).Livres(Luis, Segunda, 30, 30, Antes));

            Assert.Contains("09:30", horas);
            Assert.DoesNotContain("10:00", horas);
            Assert.Contains("10:40", horas);
            Assert.DoesNotContain("10:30", horas);
            Assert.Contains("11:00", horas);
        }

        [Fact]
        public void Antecedencia_corta_o_comeco_do_dia()
        {
            var horas = Horas(Quadro().Livres(Luis, Segunda, 30, 15, Em(14, 10)));

            Assert.Equal("14:15", horas.First());
        }

        [Fact]
        public void Jornada_maior_que_o_expediente_fica_dentro_dele()
        {
            var horas = Horas(Quadro(Jornada(entrada: 7, saida: 21)).Livres(Luis, Segunda, 30, 30, Antes));

            Assert.Equal("09:00", horas.First());
            Assert.Equal("18:30", horas.Last());
        }

        [Fact]
        public void Barbearia_fechada_nao_tem_horario()
        {
            Assert.Empty(Quadro().Livres(Luis, Domingo, 30, 30, Antes));
        }

        [Fact]
        public void Fechada_na_mao_tira_o_dia_de_todos()
        {
            var quadro = Quadro(excecoes: new() { [Segunda] = false });

            Assert.Empty(quadro.Livres(Luis, Segunda, 30, 30, Antes));
        }

        [Fact]
        public void Aberta_na_mao_vale_a_jornada_de_cada_um()
        {
            var excecao = new Dictionary<DateTime, bool> { [Domingo] = true };

            // Sem jornada: o horario guardado da grade, mesmo com o domingo fechado nela.
            Assert.NotEmpty(Quadro(excecoes: excecao).Livres(Luis, Domingo, 30, 30, Antes));

            // Com jornada de folga no domingo: continua sem horario.
            Assert.Empty(Quadro(Jornada(), excecoes: excecao).Livres(Luis, Domingo, 30, 30, Antes));
        }

        [Fact]
        public void Livre_confere_o_horario_exato_sem_depender_da_grade()
        {
            var quadro = Quadro(Jornada(), new() { new Periodo(Em(10), Em(10, 40)) });

            Assert.True(quadro.Livre(Luis, Em(10, 40), 30, Antes));
            Assert.True(quadro.Livre(Luis, Em(9, 7), 30, Antes));
            Assert.False(quadro.Livre(Luis, Em(10, 20), 30, Antes));
            Assert.False(quadro.Livre(Luis, Em(11, 45), 30, Antes));
            Assert.False(quadro.Livre(Luis, Em(17, 45), 30, Antes));
            Assert.False(quadro.Livre(Luis, Em(14), 30, Em(15)));
        }

        [Fact]
        public void Qualquer_profissional_junta_os_horarios_com_quem_pode()
        {
            var quadro = new QuadroAgenda
            {
                Grade = Grade(),
                Jornadas = new Dictionary<int, List<JornadaDia>?> { [Luis] = null, [Beto] = null },
                Ocupados = new Dictionary<int, List<Periodo>>
                {
                    [Luis] = new() { new Periodo(Em(9), Em(10)) },
                    [Beto] = new()
                }
            };

            var mapa = quadro.LivresDeTodos(new[] { Luis, Beto }, Segunda, 30, 30, Antes);

            Assert.Equal(new[] { Beto }, mapa[Em(9)]);
            Assert.Equal(new[] { Luis, Beto }, mapa[Em(10)]);
        }

        [Fact]
        public void Profissional_sem_servicos_marcados_faz_todos()
        {
            var todos = new ProfissionalOnline();
            var so = new ProfissionalOnline { Servicos = new() { 1, 2 } };

            Assert.True(todos.Faz(new[] { 7 }));
            Assert.True(so.Faz(new[] { 1, 2 }));
            Assert.False(so.Faz(new[] { 1, 3 }));
        }

        [Theory]
        [InlineData(-1, 30, 15, 2, null)]
        [InlineData(60, 0, 15, 2, null)]
        [InlineData(60, 30, 25, 2, null)]
        [InlineData(60, 30, 15, 0, null)]
        [InlineData(60, 30, 15, 2, "1234")]
        public void Configuracao_invalida_tem_mensagem(int antecedencia, int dias, int intervalo, int maximo, string? whatsapp)
        {
            var config = new ConfigAgendaOnline
            {
                AntecedenciaMin = antecedencia, DiasAFrente = dias, IntervaloMin = intervalo,
                MaximoAbertos = maximo, WhatsApp = whatsapp
            };

            Assert.NotNull(config.Erro());
        }

        [Fact]
        public void Configuracao_padrao_e_valida()
        {
            Assert.Null(new ConfigAgendaOnline { WhatsApp = "(48) 99123-4567" }.Erro());
        }
    }
}
