using LucasAguiar.Models;
using LucasAguiar.Data;
using static LucasAguiar.Services.RelatorioPdf;

namespace LucasAguiar.Services
{
    /// <summary>Um endpoint de download por tela de listagem.</summary>
    public static class RelatorioEndpoints
    {
        private const string TipoPdf = "application/pdf";

        public static void MapearRelatorios(this WebApplication app)
        {
            app.MapGet("/relatorio/fornecedores", (RelatorioPdf rel, FornecedorDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Fornecedor>();
                var pdf = rel.Gerar("Relatório de Fornecedores",
                    new[]
                    {
                        new Coluna("#", 0.7), new Coluna("Nome", 3), new Coluna("E-mail", 3),
                        new Coluna("Telefone", 1.8), new Coluna("Tipo de Produto", 2.2)
                    },
                    itens.Select((f, i) => new[]
                    {
                        (i + 1).ToString(), f.NomeFornecedor ?? "", f.Email ?? "",
                        f.Telefone ?? "", f.TipoProd ?? ""
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("fornecedores"));
            });

            app.MapGet("/relatorio/servicos", (RelatorioPdf rel, ServicoDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Servico>();
                var pdf = rel.Gerar("Relatório de Serviços",
                    new[]
                    {
                        new Coluna("#", 0.7), new Coluna("Nome", 4),
                        new Coluna("Preço", 1.6, true), new Coluna("Duração", 1.6, true),
                        new Coluna("Comissão", 1.6, true)
                    },
                    itens.Select((s, i) => new[]
                    {
                        (i + 1).ToString(), s.NomeServico ?? "",
                        s.PrecoServico.ToString("C"), $"{s.DuracaoServico} min",
                        s.ComissaoServico.ToString("C")
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("servicos"));
            });

            app.MapGet("/relatorio/funcionarios", (RelatorioPdf rel, FuncionarioDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Funcionario>();
                var pdf = rel.Gerar("Relatório de Funcionários",
                    new[]
                    {
                        new Coluna("#", 0.6), new Coluna("Nome", 2.8), new Coluna("Telefone", 1.6),
                        new Coluna("CPF", 1.6), new Coluna("E-mail", 2.8), new Coluna("Cidade", 1.6)
                    },
                    itens.Select((f, i) => new[]
                    {
                        (i + 1).ToString(), f.NomeFuncionario ?? "", f.Telefone ?? "",
                        f.CPF ?? "", f.Email ?? "", f.Cidade ?? ""
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("funcionarios"));
            });

            app.MapGet("/relatorio/produtos", (RelatorioPdf rel, ProdutoDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Produto>();
                var pdf = rel.Gerar("Relatório de Produtos",
                    new[]
                    {
                        new Coluna("#", 0.6), new Coluna("Nome", 2.6), new Coluna("Qtd.", 0.9, true),
                        new Coluna("Valor", 1.3, true), new Coluna("Marca", 1.6),
                        new Coluna("Fornecedor", 2.2)
                    },
                    itens.Select(p => new[]
                    {
                        p.IdProduto.ToString(), p.NomeProduto ?? "", p.Quantidade.ToString(),
                        p.Valor.ToString("C"), p.Marca ?? "", p.NomeFornecedor ?? ""
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("produtos"));
            });

            app.MapGet("/relatorio/compras", (RelatorioPdf rel, CompraDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Compra>();
                var pdf = rel.Gerar("Relatório de Compras",
                    new[]
                    {
                        new Coluna("#", 0.6), new Coluna("Data", 1.4), new Coluna("Item", 2.4),
                        new Coluna("Qtd.", 0.9, true), new Coluna("Valor", 1.3, true),
                        new Coluna("Fornecedor", 2.2), new Coluna("Funcionário", 2.2)
                    },
                    itens.Select((c, i) => new[]
                    {
                        (i + 1).ToString(), c.DataCompra.ToString("dd/MM/yyyy"), c.ItemCompra ?? "",
                        c.Quantidade.ToString(), c.ValorCompra.ToString("C"),
                        c.NomeFornecedor ?? "—", c.NomeFuncionario ?? "—"
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("compras"));
            });

            app.MapGet("/relatorio/clientes", (RelatorioPdf rel, ClienteDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Cliente>();
                var pdf = rel.Gerar("Relatório de Clientes",
                    new[]
                    {
                        new Coluna("#", 0.6), new Coluna("Nome", 3), new Coluna("Telefone", 1.7),
                        new Coluna("CPF", 1.7), new Coluna("Cidade", 1.8), new Coluna("Bairro", 1.8)
                    },
                    itens.Select((c, i) => new[]
                    {
                        (i + 1).ToString(), c.NomeCliente ?? "", c.Telefone ?? "",
                        c.CPF ?? "", c.Cidade ?? "", c.Bairro ?? ""
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("clientes"));
            });

            // Aceita os mesmos filtros da tela, para o PDF sair igual ao que
            // esta na tela em vez de sempre trazer tudo.
            app.MapGet("/relatorio/vendas", (
                RelatorioPdf rel, VendaDAO dao,
                DateTime? de, DateTime? ate, string? cliente, int? funcionario, string? forma) =>
            {
                var filtro = new VendaDAO.Filtro
                {
                    De = de,
                    Ate = ate?.AddDays(1),
                    Cliente = cliente,
                    IdFuncionario = funcionario,
                    FormaPagamento = forma
                };

                var itens = dao.ListarTodas(filtro) ?? new List<Venda>();

                var pdf = rel.Gerar($"Relatório de Vendas — {filtro.Resumo()}",
                    new[]
                    {
                        new Coluna("#", 0.6), new Coluna("Data", 1.6), new Coluna("Cliente", 2.4),
                        new Coluna("Profissional", 2), new Coluna("Pagamento", 1.6),
                        new Coluna("Comissão", 1.2, true), new Coluna("Total", 1.3, true)
                    },
                    itens.Select(v => new[]
                    {
                        v.IdVenda.ToString(),
                        v.DataVenda.ToString("dd/MM/yy HH:mm"),
                        v.NomeCliente ?? "—",
                        v.NomeFuncionario ?? "—",
                        FormaPagamento.Rotulo(v.FormaPagamentoVenda),
                        v.ComissaoGravada.ToString("C"),
                        v.ValorVenda.ToString("C")
                    }).ToList());

                return Results.File(pdf, TipoPdf, NomeArquivo("vendas"));
            });

            app.MapGet("/relatorio/comissoes", (
                RelatorioPdf rel, ComissaoDAO dao, DateTime? de, DateTime? ate) =>
            {
                var inicio = de ?? DateTime.Today.AddDays(-30);
                var fim = (ate ?? DateTime.Today).AddDays(1);

                var itens = dao.ResumoPorProfissional(inicio, fim);

                var pdf = rel.Gerar(
                    $"Comissões — {inicio:dd/MM/yyyy} a {fim.AddDays(-1):dd/MM/yyyy}",
                    new[]
                    {
                        new Coluna("Profissional", 3), new Coluna("Atend.", 1, true),
                        new Coluna("Serviços", 1.1, true), new Coluna("Produtos", 1.1, true),
                        new Coluna("Vlr. produtos", 1.6, true), new Coluna("Vendido", 1.6, true),
                        new Coluna("Comissão", 1.6, true), new Coluna("%", 0.9, true)
                    },
                    itens.Select(c => new[]
                    {
                        c.NomeFuncionario,
                        c.QuantidadeAtendimentos.ToString(),
                        c.ServicosRealizados.ToString(),
                        c.ProdutosVendidos.ToString(),
                        c.ValorProdutos.ToString("C"),
                        c.TotalVendido.ToString("C"),
                        c.TotalComissao.ToString("C"),
                        c.PercentualSobreVenda.ToString("0.#") + "%"
                    }).ToList());

                return Results.File(pdf, TipoPdf, NomeArquivo("comissoes"));
            });

            app.MapGet("/relatorio/despesas", (
                RelatorioPdf rel, DespesaDAO dao,
                DateTime? de, DateTime? ate, string? categoria, string? forma) =>
            {
                var filtro = new DespesaDAO.Filtro
                {
                    De = de,
                    Ate = ate?.AddDays(1),
                    Categoria = categoria,
                    FormaPagamento = forma
                };

                var itens = dao.Listar(filtro) ?? new List<Despesa>();

                var pdf = rel.Gerar($"Relatório de Despesas — {filtro.Resumo()}",
                    new[]
                    {
                        new Coluna("Data", 1.3), new Coluna("Descrição", 3.2),
                        new Coluna("Categoria", 2), new Coluna("Fornecedor", 2),
                        new Coluna("Pagamento", 1.6), new Coluna("Valor", 1.3, true)
                    },
                    itens.Select(d => new[]
                    {
                        d.DataDespesa.ToString("dd/MM/yyyy"),
                        d.Descricao,
                        d.RotuloCategoria,
                        d.NomeFornecedor ?? "—",
                        FormaPagamento.Rotulo(d.FormaPagamentoDespesa) + (d.Pago ? "" : " (aberto)"),
                        d.Valor.ToString("C")
                    }).ToList());

                return Results.File(pdf, TipoPdf, NomeArquivo("despesas"));
            });

            // Fechamento de uma sessao: o comprovante da conferencia do dia.
            app.MapGet("/relatorio/caixa/{id:int}", (RelatorioPdf rel, CaixaDAO dao, int id) =>
            {
                var caixa = dao.BuscarPorId(id);
                if (caixa == null) return Results.NotFound();

                var linhas = new List<string[]>
                {
                    new[] { "Abertura", caixa.DataAbertura.ToString("dd/MM/yyyy HH:mm"), caixa.ValorAbertura.ToString("C") },
                    new[] { "Vendas em dinheiro", $"{caixa.Resumo.QuantidadeVendas} venda(s)", caixa.Resumo.VendasDinheiro.ToString("C") },
                    new[] { "Suprimentos", "", caixa.Resumo.Suprimentos.ToString("C") },
                    new[] { "Sangrias", "", "- " + caixa.Resumo.Sangrias.ToString("C") },
                    new[] { "Despesas em dinheiro", "", "- " + caixa.Resumo.DespesasDinheiro.ToString("C") },
                    new[] { "SALDO ESPERADO NA GAVETA", "", caixa.Resumo.SaldoEsperadoGaveta(caixa.ValorAbertura).ToString("C") },
                    new[] { "", "", "" },
                    new[] { "Vendas em PIX", "", caixa.Resumo.VendasPix.ToString("C") },
                    new[] { "Vendas em débito", "", caixa.Resumo.VendasDebito.ToString("C") },
                    new[] { "Vendas em crédito", "", caixa.Resumo.VendasCredito.ToString("C") },
                    new[] { "FATURAMENTO TOTAL", "", caixa.Resumo.FaturamentoTotal.ToString("C") },
                    new[] { "Comissões geradas", "", caixa.Resumo.Comissoes.ToString("C") },
                    new[] { "", "", "" }
                };

                if (!caixa.EstaAberto)
                {
                    linhas.Add(new[] { "Contado no fechamento", caixa.DataFechamento?.ToString("dd/MM/yyyy HH:mm") ?? "", caixa.ValorInformado?.ToString("C") ?? "—" });
                    linhas.Add(new[] { "DIFERENÇA", caixa.RotuloDiferenca, caixa.Diferenca?.ToString("C") ?? "—" });
                }

                var titulo = caixa.EstaAberto
                    ? $"Caixa #{caixa.IdCaixa} — parcial"
                    : $"Fechamento do caixa #{caixa.IdCaixa}";

                var pdf = rel.Gerar(titulo,
                    new[] { new Coluna("Lançamento", 4), new Coluna("Detalhe", 3), new Coluna("Valor", 2, true) },
                    linhas);

                return Results.File(pdf, TipoPdf, NomeArquivo($"caixa_{caixa.IdCaixa}"));
            });

            app.MapGet("/relatorio/planos", (RelatorioPdf rel, PlanoDAO dao) =>
            {
                var itens = dao.ListarTodos() ?? new List<Plano>();
                var pdf = rel.Gerar("Relatório de Planos",
                    new[]
                    {
                        new Coluna("#", 0.7), new Coluna("Nome", 3), new Coluna("Descrição", 5),
                        new Coluna("Valor", 1.5, true)
                    },
                    itens.Select((p, i) => new[]
                    {
                        (i + 1).ToString(), p.NomePlano ?? "", p.Descricao ?? "",
                        (p.Valor ?? 0).ToString("C")
                    }).ToList());
                return Results.File(pdf, TipoPdf, NomeArquivo("planos"));
            });
        }
    }
}
