using LucasAguiar.Data;
using LucasAguiar.Models;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Planilhas das telas que mais pedem analise. Aceitam os mesmos filtros
    /// do PDF correspondente, entao o botao Excel exporta o que esta na tela.
    /// </summary>
    public static class ExcelEndpoints
    {
        private static IResult Arquivo(string nome, string aba, string[] cabecalho, IEnumerable<object?[]> linhas) =>
            Results.File(ExcelSimples.Gerar(aba, cabecalho, linhas),
                ExcelSimples.TipoXlsx, ExcelSimples.NomeArquivo(nome));

        public static void MapearExcel(this WebApplication app)
        {
            app.MapGet("/excel/vendas", (
                VendaDAO dao, DateTime? de, DateTime? ate, string? cliente, int? funcionario, string? forma) =>
            {
                var itens = dao.ListarTodas(new VendaDAO.Filtro
                {
                    De = de,
                    Ate = ate?.AddDays(1),
                    Cliente = cliente,
                    IdFuncionario = funcionario,
                    FormaPagamento = forma
                }) ?? new List<Venda>();

                return Arquivo("vendas", "Vendas",
                    new[] { "#", "Data", "Cliente", "Profissional", "Pagamento", "Parcelas", "Desconto", "Comissão", "Total", "Status" },
                    itens.Select(v => new object?[]
                    {
                        v.IdVenda, v.DataVenda, v.NomeCliente, v.NomeFuncionario,
                        FormaPagamento.Rotulo(v.FormaPagamentoVenda), v.QuantidadeParcelas,
                        v.Desconto, v.ComissaoGravada, v.ValorVenda, v.Status
                    }));
            });

            app.MapGet("/excel/despesas", (
                DespesaDAO dao, DateTime? de, DateTime? ate, string? categoria, string? forma) =>
            {
                var itens = dao.Listar(new DespesaDAO.Filtro
                {
                    De = de,
                    Ate = ate?.AddDays(1),
                    Categoria = categoria,
                    FormaPagamento = forma
                }) ?? new List<Despesa>();

                return Arquivo("despesas", "Despesas",
                    new[] { "Data", "Descrição", "Categoria", "Fornecedor", "Pagamento", "Paga", "Valor" },
                    itens.Select(d => new object?[]
                    {
                        d.DataDespesa, d.Descricao, d.RotuloCategoria, d.NomeFornecedor,
                        FormaPagamento.Rotulo(d.FormaPagamentoDespesa), d.Pago, d.Valor
                    }));
            });

            app.MapGet("/excel/comissoes", (ComissaoDAO dao, DateTime? de, DateTime? ate) =>
            {
                var inicio = de ?? DateTime.Today.AddDays(-30);
                var fim = (ate ?? DateTime.Today).AddDays(1);

                return Arquivo("comissoes", "Comissões",
                    new[] { "Profissional", "Atendimentos", "Serviços", "Produtos", "Valor produtos", "Vendido", "Comissão", "% sobre venda" },
                    dao.ResumoPorProfissional(inicio, fim).Select(c => new object?[]
                    {
                        c.NomeFuncionario, c.QuantidadeAtendimentos, c.ServicosRealizados,
                        c.ProdutosVendidos, c.ValorProdutos, c.TotalVendido, c.TotalComissao,
                        c.PercentualSobreVenda
                    }));
            });

            app.MapGet("/excel/compras", (CompraDAO dao) =>
                Arquivo("compras", "Compras",
                    new[] { "#", "Data", "Item", "Quantidade", "Produto", "Fornecedor", "Funcionário", "Valor total", "Valor unitário" },
                    (dao.ListarTodos() ?? new List<Compra>()).Select(c => new object?[]
                    {
                        c.IdCompra, c.DataCompra, c.ItemCompra, c.Quantidade, c.NomeProduto,
                        c.NomeFornecedor, c.NomeFuncionario, c.ValorCompra, c.ValorUnitario
                    })));

            app.MapGet("/excel/clientes", (ClienteDAO dao) =>
                Arquivo("clientes", "Clientes",
                    new[] { "#", "Nome", "Telefone", "CPF", "Nascimento", "Cidade", "Estado", "Plano" },
                    (dao.ListarTodos() ?? new List<Cliente>()).Select(c => new object?[]
                    {
                        c.IdCliente, c.NomeCliente, c.Telefone, c.CPF, c.DataNascimento,
                        c.Cidade, c.Estado, c.NomePlano
                    })));

            app.MapGet("/excel/produtos", (ProdutoDAO dao) =>
                Arquivo("produtos", "Produtos",
                    new[] { "#", "Nome", "Marca", "Fornecedor", "Estoque", "Valor", "Descrição" },
                    (dao.ListarTodos() ?? new List<Produto>()).Select(p => new object?[]
                    {
                        p.IdProduto, p.NomeProduto, p.Marca, p.NomeFornecedor, p.Quantidade,
                        (decimal)p.Valor, p.Descricao
                    })));

            app.MapGet("/excel/agendamentos", (AgendamentoDAO dao, DateTime? de, DateTime? ate, string? status) =>
                Arquivo("agendamentos", "Agendamentos",
                    new[] { "#", "Início", "Fim", "Cliente", "Telefone", "Serviço", "Profissional", "Preço", "Status", "Observação" },
                    dao.Listar(de, ate?.AddDays(1), status).Select(a => new object?[]
                    {
                        a.IdAgendamento, a.DataHora, a.Fim, a.NomeCliente, a.TelefoneCliente,
                        a.NomeServico, a.NomeFuncionario, a.PrecoServico,
                        StatusAgendamento.Rotulo(a.Status), a.Observacao
                    })));

            // Nunca leva senha, hash nem token -- so o que a tela mostra.
            app.MapGet("/excel/equipe", (EquipeDAO dao) =>
                Arquivo("equipe", "Equipe",
                    new[] { "#", "Nome", "Usuário", "E-mail", "Função", "Perfil", "Status", "Último acesso" },
                    (dao.Listar() ?? new List<Usuario>()).Select((u, i) => new object?[]
                    {
                        i + 1, u.NomeVisivel, u.NomeUsr, u.EmailUsr, u.NomeFuncao,
                        Perfil.Rotulo(u.PerfilUsr), u.AtivoUsr ? "Ativo" : "Inativo",
                        u.UltimoLoginUsr
                    })));

            app.MapGet("/excel/logs", (
                LogDAO dao, string? tipo, string? nivel, string? busca,
                DateTime? de, DateTime? ate) =>
            {
                var resultado = dao.Listar(new LogDAO.Filtro
                {
                    Tipo = tipo,
                    Nivel = nivel,
                    Busca = busca,
                    De = de,
                    Ate = ate?.AddDays(1),
                    Pagina = 1,
                    PorPagina = 200
                });

                return Arquivo("logs", "Logs",
                    new[] { "Quando", "Tipo", "Nível", "Ação", "Descrição", "Quem", "IP", "Alvo" },
                    resultado.Linhas.Select(l => new object?[]
                    {
                        l.Quando, TipoLog.Rotulo(l.Tipo), NivelLog.Rotulo(l.Nivel),
                        l.Acao, l.Descricao, l.NomeGravado, l.Ip, l.Alvo
                    }));
            });
        }
    }
}
