using LucasAguiar.Models;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// Aviso da agenda pela jornada e bloqueios do profissional. Jornada de
    /// teste: segunda a sabado das 9h as 18h, almoco 12h-13h, domingo de
    /// folga. 19/10/2026 e uma segunda.
    /// </summary>
    public class DisponibilidadeTeste
    {
        private static List<JornadaDia> Jornada() =>
            Enum.GetValues<DayOfWeek>()
                .Select(d => new JornadaDia
                {
                    Dia = d,
                    Trabalha = d != DayOfWeek.Sunday,
                    Entrada = new(9, 0, 0),
                    Saida = new(18, 0, 0),
                    AlmocoInicio = new(12, 0, 0),
                    AlmocoFim = new(13, 0, 0)
                })
                .ToList();

        private static readonly List<BloqueioFuncionario> SemBloqueio = new();

        private static string? Aviso(List<JornadaDia>? jornada, List<BloqueioFuncionario> bloqueios, int hora, int minuto, int duracao) =>
            Disponibilidade.AvisoAgendamento(jornada, bloqueios,
                new DateTime(2026, 10, 19, hora, minuto, 0),
                new DateTime(2026, 10, 19, hora, minuto, 0).AddMinutes(duracao), "Luis");

        [Fact]
        public void Dentro_da_jornada_nao_avisa()
        {
            Assert.Null(Aviso(Jornada(), SemBloqueio, 10, 0, 40));
        }

        [Fact]
        public void Encostar_no_almoco_nao_avisa()
        {
            Assert.Null(Aviso(Jornada(), SemBloqueio, 11, 20, 40));
            Assert.Null(Aviso(Jornada(), SemBloqueio, 13, 0, 30));
        }

        [Fact]
        public void Cruzar_o_almoco_avisa()
        {
            Assert.Equal("O horário cai no almoço de Luis (12:00–13:00).", Aviso(Jornada(), SemBloqueio, 11, 40, 40));
        }

        [Fact]
        public void Passar_da_saida_avisa()
        {
            Assert.Equal("Luis trabalha das 09:00 às 18:00 nesse dia.", Aviso(Jornada(), SemBloqueio, 17, 40, 30));
        }

        [Fact]
        public void Dia_de_folga_avisa()
        {
            var aviso = Disponibilidade.AvisoAgendamento(Jornada(), SemBloqueio,
                new DateTime(2026, 10, 25, 10, 0, 0), new DateTime(2026, 10, 25, 10, 30, 0), "Luis");

            Assert.Equal("Luis não trabalha aos domingos.", aviso);
        }

        [Fact]
        public void Bloqueio_avisa_com_motivo_e_vale_mais_que_a_jornada()
        {
            var bloqueio = new BloqueioFuncionario
            {
                Inicio = new DateTime(2026, 10, 19, 15, 0, 0),
                Fim = new DateTime(2026, 10, 19, 18, 0, 0),
                Motivo = "Médico"
            };

            Assert.Equal("Luis está indisponível em 19/10 15:00–18:00 (Médico).",
                Aviso(Jornada(), new() { bloqueio }, 17, 40, 30));
            Assert.Null(Aviso(Jornada(), new() { bloqueio }, 14, 0, 60));
        }

        [Fact]
        public void Sem_jornada_so_valem_os_bloqueios()
        {
            Assert.Null(Aviso(null, SemBloqueio, 22, 0, 30));
        }

        [Fact]
        public void Jornada_nasce_da_barbearia_sem_almoco()
        {
            var grade = Enum.GetValues<DayOfWeek>()
                .Select(d => new HorarioDia { Dia = d, Aberto = d != DayOfWeek.Sunday, Abre = new(8, 0, 0), Fecha = new(20, 0, 0) })
                .ToList();

            var jornada = Disponibilidade.CopiarDaBarbearia(grade);

            Assert.False(jornada[(int)DayOfWeek.Sunday].Trabalha);
            Assert.True(jornada[(int)DayOfWeek.Monday].Trabalha);
            Assert.Equal(new TimeSpan(8, 0, 0), jornada[(int)DayOfWeek.Monday].Entrada);
            Assert.False(jornada[(int)DayOfWeek.Monday].TemAlmoco);
        }

        [Fact]
        public void Jornada_invalida_e_recusada()
        {
            var saidaAntes = Jornada();
            saidaAntes[1].Saida = new(8, 0, 0);
            Assert.Equal("Segunda: a saída deve ser depois da entrada.", Disponibilidade.ErroJornada(saidaAntes));

            var almocoPelaMetade = Jornada();
            almocoPelaMetade[2].AlmocoFim = null;
            Assert.Equal("Terça: informe o início e o fim do almoço, ou nenhum dos dois.", Disponibilidade.ErroJornada(almocoPelaMetade));

            var almocoFora = Jornada();
            almocoFora[3].AlmocoInicio = new(8, 0, 0);
            Assert.Equal("Quarta: o almoço deve ficar dentro da jornada.", Disponibilidade.ErroJornada(almocoFora));

            Assert.Null(Disponibilidade.ErroJornada(Jornada()));
        }
    }
}
