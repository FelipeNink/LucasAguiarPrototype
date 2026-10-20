using LucasAguiar.Models;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>As contas do fluxo de caixa e do DRE, sem banco.</summary>
    public class FluxoCaixaTeste
    {
        private static readonly DateTime Hoje = new(2026, 10, 20);

        private static Dictionary<string, TaxaPagamento> Taxas() => new()
        {
            [FormaPagamento.Dinheiro] = new() { Forma = FormaPagamento.Dinheiro },
            [FormaPagamento.Pix] = new() { Forma = FormaPagamento.Pix },
            [FormaPagamento.Debito] = new() { Forma = FormaPagamento.Debito, Taxa = 1.5m, Dias = 1 },
            [FormaPagamento.Credito] = new() { Forma = FormaPagamento.Credito, Taxa = 3m, Dias = 30 },
            [TaxaPagamento.CreditoParcelado] = new() { Forma = TaxaPagamento.CreditoParcelado, Taxa = 5m, Dias = 30 }
        };

        [Fact]
        public void Dinheiro_cai_no_dia_sem_taxa()
        {
            var pagamento = new PagamentoDeVenda { IdVenda = 1, DataVenda = Hoje.AddHours(10), Forma = FormaPagamento.Dinheiro, Valor = 50m };

            var r = Assert.Single(FluxoCaixa.Recebimentos(new[] { pagamento }, Taxas(), Hoje));

            Assert.Equal(Hoje, r.Data);
            Assert.Equal(50m, r.Valor);
            Assert.True(r.Realizado);
        }

        [Fact]
        public void Debito_cai_no_prazo_com_a_taxa_descontada()
        {
            var pagamento = new PagamentoDeVenda { IdVenda = 1, DataVenda = Hoje, Forma = FormaPagamento.Debito, Valor = 100m };

            var r = Assert.Single(FluxoCaixa.Recebimentos(new[] { pagamento }, Taxas(), Hoje));

            Assert.Equal(Hoje.AddDays(1), r.Data);
            Assert.Equal(98.50m, r.Valor);
            Assert.False(r.Realizado);
        }

        [Fact]
        public void Credito_parcelado_vira_uma_entrada_por_mes_com_o_centavo_na_ultima()
        {
            var pagamento = new PagamentoDeVenda { IdVenda = 7, DataVenda = Hoje, Forma = FormaPagamento.Credito, Valor = 100m, Parcelas = 3 };

            var lista = FluxoCaixa.Recebimentos(new[] { pagamento }, Taxas(), Hoje);

            // 100 - 5% = 95,00 em 3: 31,67 + 31,67 + 31,66
            Assert.Equal(new[] { 31.67m, 31.67m, 31.66m }, lista.Select(l => l.Valor));
            Assert.Equal(new[] { Hoje.AddDays(30), Hoje.AddDays(60), Hoje.AddDays(90) }, lista.Select(l => l.Data));
            Assert.Equal(95m, lista.Sum(l => l.Valor));
        }

        [Fact]
        public void Credito_em_1x_usa_a_taxa_a_vista()
        {
            var pagamento = new PagamentoDeVenda { DataVenda = Hoje, Forma = FormaPagamento.Credito, Valor = 100m, Parcelas = 1 };

            Assert.Equal(3m, FluxoCaixa.ValorDaTaxa(pagamento, Taxas()));
        }

        [Fact]
        public void Forma_sem_configuracao_cai_no_dia_sem_taxa()
        {
            var pagamento = new PagamentoDeVenda { DataVenda = Hoje, Forma = FormaPagamento.Pix, Valor = 40m };

            var r = Assert.Single(FluxoCaixa.Recebimentos(new[] { pagamento }, new Dictionary<string, TaxaPagamento>(), Hoje));

            Assert.Equal(Hoje, r.Data);
            Assert.Equal(40m, r.Valor);
        }

        [Fact]
        public void Salario_do_cadastro_e_proporcional_aos_dias()
        {
            var profissional = new SalarioDoProfissional { IdFuncionario = 1, SalarioFixo = 3100m, Ativo = true };

            // 1 a 15 de outubro: 15 de 31 dias.
            var total = FluxoCaixa.SalariosFixos(new[] { profissional }, new Dictionary<(int, string), decimal>(),
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 15));

            Assert.Equal(1500m, total);
        }

        [Fact]
        public void Folha_paga_vale_mais_que_o_cadastro()
        {
            var profissional = new SalarioDoProfissional { IdFuncionario = 1, SalarioFixo = 3000m, Ativo = true };
            var folha = new Dictionary<(int, string), decimal> { [(1, "2026-09")] = 2000m };

            var total = FluxoCaixa.SalariosFixos(new[] { profissional }, folha,
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

            Assert.Equal(2000m, total);
        }

        [Fact]
        public void Inativo_sem_folha_e_admitido_depois_nao_contam_inteiro()
        {
            var inativo = new SalarioDoProfissional { IdFuncionario = 1, SalarioFixo = 3000m, Ativo = false };
            var admitido = new SalarioDoProfissional { IdFuncionario = 2, SalarioFixo = 3000m, Ativo = true, Admissao = new DateTime(2026, 9, 21) };

            var total = FluxoCaixa.SalariosFixos(new[] { inativo, admitido }, new Dictionary<(int, string), decimal>(),
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

            // So o admitido, do dia 21 ao 30: 10 de 30 dias.
            Assert.Equal(1000m, total);
        }

        [Fact]
        public void Resultado_do_dre_desconta_tudo_da_receita()
        {
            var dre = new Dre
            {
                Receita = 10000m, Taxas = 200m, CustoProdutos = 800m, Comissoes = 3000m, SalariosFixos = 1500m,
                Despesas = new() { ("Aluguel", 2000m), ("Energia", 500m) }
            };

            Assert.Equal(9800m, dre.ReceitaLiquida);
            Assert.Equal(6000m, dre.Margem);
            Assert.Equal(2000m, dre.Resultado);
            Assert.Equal(20m, dre.Percentual(dre.Resultado));
        }
    }
}
