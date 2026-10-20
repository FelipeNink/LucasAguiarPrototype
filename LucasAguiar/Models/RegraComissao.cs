namespace LucasAguiar.Models
{
    /// <summary>
    /// Comissão por unidade, em R$, do jeito que fica congelada no item da venda.
    ///
    /// Serviço: a comissão própria do profissional para o serviço vale
    /// primeiro; depois a do serviço; sem nenhuma das duas, o % padrão do
    /// profissional. Produto: o % do produto ou, sem ele, o % padrão do
    /// profissional.
    /// </summary>
    public static class RegraComissao
    {
        public static decimal Servico(Servico servico, Funcionario? profissional,
                                      FuncionarioServico? doProfissional, decimal baseUnitaria)
        {
            var tipo = servico.TipoComissao;
            decimal valor;

            if (doProfissional?.Comissao is decimal propria) valor = propria;
            else if (servico.ComissaoServico > 0) valor = (decimal)servico.ComissaoServico;
            else
            {
                tipo = TipoComissao.Percentual;
                valor = profissional?.ComissaoServicoPadrao ?? 0;
            }

            return tipo == TipoComissao.Percentual
                ? Math.Round(baseUnitaria * valor / 100m, 2)
                : Math.Round(valor, 2);
        }

        public static decimal Produto(Produto produto, Funcionario? profissional, decimal baseUnitaria)
        {
            var percentual = produto.Comissao ?? profissional?.ComissaoProdutoPadrao ?? 0;
            return Math.Round(baseUnitaria * percentual / 100m, 2);
        }
    }
}
