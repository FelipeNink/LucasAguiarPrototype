namespace LucasAguiar.Models
{
    /// <summary>
    /// Taxa da maquininha e prazo de recebimento de uma forma de pagamento
    /// (tabela taxa_pagamento). Crédito parcelado tem linha própria: a
    /// maquininha costuma cobrar mais e paga uma parcela por mês.
    /// </summary>
    public class TaxaPagamento
    {
        public const string CreditoParcelado = "CREDITO_PARCELADO";

        /// <summary>As linhas da tabela, na ordem da tela.</summary>
        public static readonly (string Forma, string Rotulo)[] Todas =
        {
            (FormaPagamento.Dinheiro, "Dinheiro"),
            (FormaPagamento.Pix, "PIX"),
            (FormaPagamento.Debito, "Cartão de débito"),
            (FormaPagamento.Credito, "Cartão de crédito à vista"),
            (CreditoParcelado, "Cartão de crédito parcelado")
        };

        public string Forma { get; set; } = "";

        /// <summary>% descontado do valor recebido.</summary>
        public decimal Taxa { get; set; }

        /// <summary>Dias até o dinheiro cair. No parcelado, o da 1ª parcela; as outras vêm a cada 30.</summary>
        public int Dias { get; set; }

        public string Rotulo => Todas.FirstOrDefault(t => t.Forma == Forma).Rotulo ?? Forma;
    }

    /// <summary>Um pagamento de venda, como sai de venda_pagamento.</summary>
    public class PagamentoDeVenda
    {
        public int IdVenda { get; set; }
        public DateTime DataVenda { get; set; }
        public string Forma { get; set; } = "";
        public decimal Valor { get; set; }
        public int? Parcelas { get; set; }

        public int TotalParcelas => Forma == FormaPagamento.Credito && Parcelas > 1 ? Parcelas.Value : 1;
    }

    /// <summary>Uma entrada ou saída no fluxo, na data em que o dinheiro se move.</summary>
    public class LancamentoFluxo
    {
        public DateTime Data { get; set; }
        public string Descricao { get; set; } = "";
        public string? Detalhe { get; set; }
        public bool Entrada { get; set; }
        public decimal Valor { get; set; }

        /// <summary>Já aconteceu: recebimento com data até hoje, despesa paga.</summary>
        public bool Realizado { get; set; }

        /// <summary>Venda ou despesa de origem, para o link.</summary>
        public int? IdVenda { get; set; }
        public int? IdDespesa { get; set; }

        public decimal ValorComSinal => Entrada ? Valor : -Valor;
    }

    /// <summary>Base do salário fixo de um profissional, para o DRE.</summary>
    public class SalarioDoProfissional
    {
        public int IdFuncionario { get; set; }
        public decimal SalarioFixo { get; set; }
        public bool Ativo { get; set; }
        public DateTime? Admissao { get; set; }
        public DateTime? Desligamento { get; set; }
    }

    /// <summary>
    /// Demonstrativo de resultado simples, por competência: a venda conta
    /// no dia em que foi feita, não quando o dinheiro cai.
    /// </summary>
    public class Dre
    {
        public decimal Receita { get; set; }
        public decimal Taxas { get; set; }
        public decimal CustoProdutos { get; set; }
        public decimal Comissoes { get; set; }
        public decimal SalariosFixos { get; set; }

        /// <summary>Despesas operacionais por categoria, maior primeiro.</summary>
        public List<(string Categoria, decimal Valor)> Despesas { get; set; } = new();

        public decimal ReceitaLiquida => Receita - Taxas;
        public decimal Margem => ReceitaLiquida - CustoProdutos - Comissoes;
        public decimal TotalDespesas => Despesas.Sum(d => d.Valor);
        public decimal Resultado => Margem - SalariosFixos - TotalDespesas;

        /// <summary>% sobre a receita, para cada linha do demonstrativo.</summary>
        public decimal Percentual(decimal valor) =>
            Receita == 0 ? 0 : Math.Round(valor / Receita * 100, 1);
    }

    /// <summary>
    /// As contas do fluxo de caixa e do DRE, sem banco: o DAO lê as linhas
    /// e entrega aqui. Separado para ter teste sem MySQL.
    /// </summary>
    public static class FluxoCaixa
    {
        /// <summary>Intervalo entre as parcelas do crédito parcelado.</summary>
        public const int DiasEntreParcelas = 30;

        public static TaxaPagamento TaxaDe(PagamentoDeVenda p, IReadOnlyDictionary<string, TaxaPagamento> taxas)
        {
            var chave = p.TotalParcelas > 1 ? TaxaPagamento.CreditoParcelado : p.Forma;
            return taxas.TryGetValue(chave, out var taxa) ? taxa : new TaxaPagamento { Forma = chave };
        }

        /// <summary>Quanto a maquininha fica deste pagamento.</summary>
        public static decimal ValorDaTaxa(PagamentoDeVenda p, IReadOnlyDictionary<string, TaxaPagamento> taxas) =>
            Math.Round(p.Valor * TaxaDe(p, taxas).Taxa / 100m, 2);

        /// <summary>
        /// Quando e quanto cada pagamento cai, já sem a taxa. O crédito
        /// parcelado vira uma entrada por parcela; o centavo que sobra da
        /// divisão vai na última.
        /// </summary>
        public static List<LancamentoFluxo> Recebimentos(
            IEnumerable<PagamentoDeVenda> pagamentos,
            IReadOnlyDictionary<string, TaxaPagamento> taxas,
            DateTime hoje)
        {
            var lista = new List<LancamentoFluxo>();

            foreach (var p in pagamentos.Where(p => p.Valor > 0))
            {
                var taxa = TaxaDe(p, taxas);
                var liquido = p.Valor - ValorDaTaxa(p, taxas);
                var parcelas = p.TotalParcelas;
                var parcela = Math.Round(liquido / parcelas, 2);

                for (var i = 1; i <= parcelas; i++)
                {
                    var valor = i < parcelas ? parcela : liquido - parcela * (parcelas - 1);
                    var data = p.DataVenda.Date.AddDays(taxa.Dias + DiasEntreParcelas * (i - 1));

                    lista.Add(new LancamentoFluxo
                    {
                        Data = data,
                        Descricao = $"Venda #{p.IdVenda}",
                        Detalhe = parcelas > 1
                            ? $"{FormaPagamento.Rotulo(p.Forma)} · parcela {i}/{parcelas}"
                            : FormaPagamento.Rotulo(p.Forma),
                        Entrada = true,
                        Valor = valor,
                        Realizado = data <= hoje.Date,
                        IdVenda = p.IdVenda
                    });
                }
            }

            return lista;
        }

        /// <summary>
        /// Salário fixo que cabe no período, proporcional aos dias de cada
        /// mês. Vale o da folha paga daquela competência; sem folha, o do
        /// cadastro, para quem estava admitido e não desligado. Inativo sem
        /// folha paga não conta.
        /// </summary>
        public static decimal SalariosFixos(
            IEnumerable<SalarioDoProfissional> profissionais,
            IReadOnlyDictionary<(int IdFuncionario, string Competencia), decimal> folhaPaga,
            DateTime de, DateTime ate)
        {
            var total = 0m;
            var inicio = de.Date;
            var fim = ate.Date;

            foreach (var f in profissionais)
            {
                for (var mes = new DateTime(inicio.Year, inicio.Month, 1); mes <= fim; mes = mes.AddMonths(1))
                {
                    var diasNoMes = DateTime.DaysInMonth(mes.Year, mes.Month);
                    var competencia = mes.ToString("yyyy-MM");

                    var trechoInicio = Max(inicio, mes);
                    var trechoFim = Min(fim, mes.AddDays(diasNoMes - 1));

                    decimal salario;
                    if (folhaPaga.TryGetValue((f.IdFuncionario, competencia), out var pago))
                    {
                        salario = pago;
                    }
                    else
                    {
                        if (!f.Ativo) continue;
                        if (f.Admissao.HasValue) trechoInicio = Max(trechoInicio, f.Admissao.Value.Date);
                        if (f.Desligamento.HasValue) trechoFim = Min(trechoFim, f.Desligamento.Value.Date);
                        salario = f.SalarioFixo;
                    }

                    var dias = (trechoFim - trechoInicio).Days + 1;
                    if (dias <= 0 || salario <= 0) continue;

                    total += salario * dias / diasNoMes;
                }
            }

            return Math.Round(total, 2);
        }

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
