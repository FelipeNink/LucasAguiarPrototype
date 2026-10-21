using LucasAguiar.Models;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Aberta/fechada do painel e o aviso da agenda. Grade de teste: segunda
    /// a sabado das 9h as 19h, domingo fechado. 19/10/2026 e uma segunda.
    /// </summary>
    public class ExpedienteTeste
    {
        private static List<HorarioDia> Grade() =>
            Enum.GetValues<DayOfWeek>()
                .Select(d => new HorarioDia
                {
                    Dia = d,
                    Aberto = d != DayOfWeek.Sunday,
                    Abre = new(9, 0, 0),
                    Fecha = new(19, 0, 0)
                })
                .ToList();

        [Fact]
        public void Dentro_do_horario_esta_aberta()
        {
            var s = Expediente.Situacao(Grade(), null, new DateTime(2026, 10, 19, 10, 30, 0));

            Assert.True(s.Aberta);
            Assert.Equal("Aberta · fecha às 19:00", s.Texto);
        }

        [Fact]
        public void Antes_de_abrir_diz_a_hora_de_hoje()
        {
            var s = Expediente.Situacao(Grade(), null, new DateTime(2026, 10, 19, 8, 0, 0));

            Assert.False(s.Aberta);
            Assert.Equal("Fechada · abre às 09:00", s.Texto);
        }

        [Fact]
        public void No_horario_de_fechar_ja_esta_fechada()
        {
            var s = Expediente.Situacao(Grade(), null, new DateTime(2026, 10, 19, 19, 0, 0));

            Assert.False(s.Aberta);
            Assert.Equal("Fechada · abre amanhã às 09:00", s.Texto);
        }

        [Fact]
        public void Sabado_a_noite_pula_o_domingo()
        {
            var s = Expediente.Situacao(Grade(), null, new DateTime(2026, 10, 24, 20, 0, 0));

            Assert.Equal("Fechada · abre seg. às 09:00", s.Texto);
        }

        [Fact]
        public void Ajuste_manual_vence_a_grade()
        {
            var domingo = new DateTime(2026, 10, 25, 11, 0, 0);
            var segunda = new DateTime(2026, 10, 19, 11, 0, 0);

            Assert.True(Expediente.Situacao(Grade(), true, domingo).Aberta);
            Assert.False(Expediente.Situacao(Grade(), false, segunda).Aberta);
            Assert.True(Expediente.Situacao(Grade(), false, segunda).Manual);
        }

        [Fact]
        public void Semana_toda_fechada_nao_trava()
        {
            var grade = Grade();
            grade.ForEach(d => d.Aberto = false);

            Assert.Equal("Fechada", Expediente.Situacao(grade, null, new DateTime(2026, 10, 19, 10, 0, 0)).Texto);
        }

        [Fact]
        public void Agendamento_dentro_do_expediente_nao_avisa()
        {
            var inicio = new DateTime(2026, 10, 19, 18, 30, 0);
            Assert.Null(Expediente.AvisoAgendamento(Grade(), null, inicio, inicio.AddMinutes(30)));
        }

        [Fact]
        public void Agendamento_que_passa_do_fechamento_avisa()
        {
            var inicio = new DateTime(2026, 10, 19, 18, 45, 0);
            var aviso = Expediente.AvisoAgendamento(Grade(), null, inicio, inicio.AddMinutes(30));

            Assert.Equal("Fora do expediente: nesse dia a barbearia funciona das 09:00 às 19:00.", aviso);
        }

        [Fact]
        public void Agendamento_em_dia_fechado_avisa_salvo_se_abriram_na_mao()
        {
            var inicio = new DateTime(2026, 10, 25, 10, 0, 0);

            Assert.Equal("Fora do expediente: a barbearia não abre aos domingos.",
                Expediente.AvisoAgendamento(Grade(), null, inicio, inicio.AddMinutes(30)));
            Assert.Null(Expediente.AvisoAgendamento(Grade(), true, inicio, inicio.AddMinutes(30)));
        }
    }
}
