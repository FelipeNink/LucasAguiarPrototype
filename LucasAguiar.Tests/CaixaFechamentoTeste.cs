using LucasAguiar.Configs;
using LucasAguiar.Data;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>Regra do fechamento do caixa com diferenca, e o fuso que o MySQL recebe.</summary>
    public class CaixaFechamentoTeste
    {
        private const string Justificativa = "Troco dado a mais para o cliente";

        [Fact]
        public void Caixa_que_confere_fecha_sem_justificativa()
        {
            Assert.Null(CaixaDAO.ErroFechamento(0m, null, podeFecharComFalta: false));
        }

        [Fact]
        public void Sobra_exige_justificativa_mas_nao_administrador()
        {
            Assert.NotNull(CaixaDAO.ErroFechamento(5m, "", podeFecharComFalta: false));
            Assert.NotNull(CaixaDAO.ErroFechamento(5m, "troco", podeFecharComFalta: false));
            Assert.Null(CaixaDAO.ErroFechamento(5m, Justificativa, podeFecharComFalta: false));
        }

        [Fact]
        public void Falta_exige_justificativa_e_administrador()
        {
            Assert.Contains("Explique a falta", CaixaDAO.ErroFechamento(-20m, null, podeFecharComFalta: true));
            Assert.Contains("administrador", CaixaDAO.ErroFechamento(-20m, Justificativa, podeFecharComFalta: false));
            Assert.Null(CaixaDAO.ErroFechamento(-20m, Justificativa, podeFecharComFalta: true));
        }

        [Fact]
        public void Justificativa_so_de_espacos_nao_vale()
        {
            Assert.NotNull(CaixaDAO.ErroFechamento(-0.01m, new string(' ', 30), podeFecharComFalta: true));
        }

        [Fact]
        public void Fuso_sai_no_formato_do_mysql()
        {
            Assert.Matches(@"^[+-]\d{2}:\d{2}$", Conexao.FusoMySql());
        }
    }
}
