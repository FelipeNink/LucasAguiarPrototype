namespace LucasAguiar.Configs
{
    /// <summary>
    /// Acoes que uma tela pode exigir. Flags para uma tela declarar de uma
    /// vez so o que faz sentido nela: a matriz de permissoes mostra "—" nas
    /// outras, em vez de um interruptor que nao liga nada.
    ///
    /// Os quatro primeiros valores sao o basico de qualquer listagem. Os
    /// quatro ultimos sao as acoes sensiveis deste sistema, e nascem
    /// sempre desligadas para um membro novo.
    /// </summary>
    [Flags]
    public enum Acao
    {
        Nenhuma = 0,

        /// <summary>Abrir a tela. Sem isto, nenhuma das outras vale.</summary>
        Ver = 1,
        Criar = 2,
        Editar = 4,
        Excluir = 8,

        /// <summary>Baixar o PDF ou a planilha da tela.</summary>
        Exportar = 16,

        /// <summary>Cancelar uma venda ja lancada.</summary>
        Estornar = 32,

        /// <summary>
        /// Mexer em dinheiro de verdade: abrir e fechar o caixa, sangria,
        /// suprimento e pagamento da folha.
        /// </summary>
        Liquidar = 64,

        /// <summary>Ver faturamento, lucro e comissao no painel.</summary>
        VerValores = 128
    }

    /// <summary>
    /// Uma tela do sistema. <paramref name="Rotas"/> sao os caminhos de
    /// pagina que ela atende e <paramref name="Exportacoes"/> os endpoints
    /// de download que pertencem a ela.
    /// </summary>
    public record Tela(
        string Chave,
        string Nome,
        string Secao,
        Acao Acoes,
        string[] Rotas,
        string[]? Exportacoes = null)
    {
        public bool Tem(Acao acao) => (Acoes & acao) == acao;

        public string[] Download => Exportacoes ?? Array.Empty<string>();
    }

    /// <summary>
    /// Fonte unica de verdade das telas e acoes do sistema.
    ///
    /// Quem le daqui: a matriz de permissoes, o menu lateral, o middleware
    /// de autorizacao, as guardas de pagina, os endpoints de relatorio e os
    /// testes. Uma tela nova entra aqui uma vez e aparece em todos eles --
    /// e, pela sincronizacao do BancoInicializador, nasce negada para quem
    /// nao e administrador.
    /// </summary>
    public static class Catalogo
    {
        public const string Operacao = "Operação";
        public const string Financeiro = "Financeiro";
        public const string Cadastros = "Cadastros";
        public const string Sistema = "Sistema";

        /// <summary>O basico de uma listagem de cadastro.</summary>
        private const Acao Cadastro =
            Acao.Ver | Acao.Criar | Acao.Editar | Acao.Excluir | Acao.Exportar;

        public static readonly IReadOnlyList<Tela> Telas = new List<Tela>
        {
            // ---------- Operacao ----------
            new("painel", "Painel", Operacao,
                Acao.Ver | Acao.VerValores,
                new[] { "/" }),

            // Editar venda, hoje, e trocar o profissional do atendimento.
            new("vendas", "Vendas", Operacao,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Estornar | Acao.Exportar,
                new[] { "/vendas", "/venda/nova", "/venda" },
                new[] { "/relatorio/vendas", "/excel/vendas" }),

            new("agendamentos", "Agendamentos", Operacao,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Excluir | Acao.Exportar,
                new[] { "/agendamentos", "/agendamento/novo", "/agendamento" },
                new[] { "/excel/agendamentos" }),

            // Liquidar = abrir, fechar, sangria e suprimento.
            new("caixa", "Caixa", Operacao,
                Acao.Ver | Acao.Liquidar | Acao.Exportar,
                new[] { "/caixa" },
                new[] { "/relatorio/caixa" }),

            // ---------- Financeiro ----------
            // Nao tem Criar nem Editar: so le e soma o que as outras telas
            // ja gravaram.
            new("relatorios", "Relatórios", Financeiro,
                Acao.Ver | Acao.Exportar,
                new[] { "/relatorios" },
                new[] { "/relatorio/gerencial", "/excel/gerencial" }),

            // Editar = mudar as taxas e prazos por forma de pagamento.
            new("fluxo", "Fluxo de caixa", Financeiro,
                Acao.Ver | Acao.Editar,
                new[] { "/fluxo-caixa" }),

            new("despesas", "Despesas", Financeiro,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Excluir | Acao.Exportar,
                new[] { "/despesas", "/despesa" },
                new[] { "/relatorio/despesas", "/excel/despesas" }),

            new("comissoes", "Comissões", Financeiro,
                Acao.Ver | Acao.Exportar,
                new[] { "/comissoes" },
                new[] { "/relatorio/comissoes", "/excel/comissoes" }),

            // Liquidar = pagar o profissional, o que gera despesa e
            // movimento de caixa.
            new("folha", "Folha de pagamento", Financeiro,
                Acao.Ver | Acao.Liquidar | Acao.Exportar,
                new[] { "/folha" }),

            new("compras", "Compras", Financeiro,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Excluir | Acao.Exportar,
                new[] { "/compra", "/compra/novo" },
                new[] { "/relatorio/compras", "/excel/compras" }),

            // Editar = configurar o prestador e reenviar nota recusada.
            // Estornar = cancelar nota autorizada na prefeitura.
            new("notas", "Notas fiscais", Financeiro,
                Acao.Ver | Acao.Editar | Acao.Estornar,
                new[] { "/notas-fiscais" }),

            // ---------- Cadastros ----------
            new("clientes", "Clientes", Cadastros, Cadastro,
                new[] { "/cliente", "/cliente/novo" },
                new[] { "/relatorio/clientes", "/excel/clientes" }),

            new("servicos", "Serviços", Cadastros, Cadastro,
                new[] { "/servico", "/servico/novo" },
                new[] { "/relatorio/servicos" }),

            new("produtos", "Produtos", Cadastros, Cadastro,
                new[] { "/produto", "/produto/novo" },
                new[] { "/relatorio/produtos", "/excel/produtos" }),

            // Criar = lancar consumo e perda. Editar = inventariar, que e
            // corrigir o saldo e por isso pede permissao propria. Nao tem
            // Excluir: movimento registrado nao se apaga, senao o extrato
            // nao serve para conferir nada.
            new("estoque", "Estoque", Cadastros,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Exportar,
                new[] { "/estoque" },
                new[] { "/relatorio/estoque", "/excel/estoque" }),

            new("planos", "Planos", Cadastros, Cadastro,
                new[] { "/plano", "/plano/novo" },
                new[] { "/relatorio/planos" }),

            // So consulta: a assinatura nasce na venda do plano.
            new("assinaturas", "Assinaturas", Cadastros,
                Acao.Ver,
                new[] { "/assinaturas" }),

            new("funcionarios", "Funcionários", Cadastros, Cadastro,
                new[] { "/funcionario", "/funcionario/novo" },
                new[] { "/relatorio/funcionarios" }),

            new("fornecedores", "Fornecedores", Cadastros, Cadastro,
                new[] { "/fornecedores", "/fornecedores/novo" },
                new[] { "/relatorio/fornecedores" }),

            // ---------- Sistema ----------
            new("equipe", "Equipe", Sistema, Cadastro,
                new[] { "/equipe", "/equipe/novo" },
                new[] { "/relatorio/equipe", "/excel/equipe" }),

            new("funcoes", "Funções", Sistema,
                Acao.Ver | Acao.Criar | Acao.Editar | Acao.Excluir,
                new[] { "/funcoes" }),

            new("logs", "Logs", Sistema,
                Acao.Ver | Acao.Exportar,
                new[] { "/logs" },
                new[] { "/excel/logs" }),

            // Editar tambem cobre o "abrir/fechar agora" do painel.
            new("horario", "Horário de funcionamento", Sistema,
                Acao.Ver | Acao.Editar,
                new[] { "/horario" })
        };

        /// <summary>Todas as acoes, na ordem em que aparecem na matriz.</summary>
        public static readonly IReadOnlyList<Acao> Acoes = new[]
        {
            Acao.Ver, Acao.Criar, Acao.Editar, Acao.Excluir,
            Acao.Exportar, Acao.Estornar, Acao.Liquidar, Acao.VerValores
        };

        /// <summary>Secoes na ordem da lateral.</summary>
        public static readonly IReadOnlyList<string> Secoes = new[]
        {
            Operacao, Financeiro, Cadastros, Sistema
        };

        public static Tela? Por(string? chave) =>
            string.IsNullOrWhiteSpace(chave)
                ? null
                : Telas.FirstOrDefault(t =>
                    string.Equals(t.Chave, chave, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Qual tela responde por um caminho de requisicao.
        ///
        /// Casa o prefixo mais longo, para "/plano/3/componentes" cair em
        /// "planos" por conta de "/plano" e nao em outra tela qualquer. A
        /// raiz e tratada a parte: como prefixo, "/" casaria com tudo.
        /// </summary>
        public static Tela? PorRota(string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return null;

            var limpo = "/" + caminho.Trim().Trim('/').ToLowerInvariant();
            if (limpo == "/") return Por("painel");

            Tela? melhor = null;
            var tamanhoMelhor = 0;

            foreach (var tela in Telas)
            {
                foreach (var rota in tela.Rotas.Concat(tela.Download))
                {
                    if (rota == "/") continue;

                    if (!limpo.Equals(rota, StringComparison.OrdinalIgnoreCase)
                        && !limpo.StartsWith(rota + "/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (rota.Length <= tamanhoMelhor) continue;

                    melhor = tela;
                    tamanhoMelhor = rota.Length;
                }
            }

            return melhor;
        }

        /// <summary>
        /// Qual acao um caminho exige. Os endpoints de download pedem
        /// Exportar; qualquer outro caminho pede Ver, e a acao de escrita e
        /// conferida depois, no DAO.
        /// </summary>
        public static Acao AcaoDaRota(string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return Acao.Ver;

            var limpo = "/" + caminho.Trim().Trim('/').ToLowerInvariant();

            return limpo.StartsWith("/relatorio/") || limpo.StartsWith("/excel/")
                ? Acao.Exportar
                : Acao.Ver;
        }

        /// <summary>Nome da coluna de <c>permissao</c> que guarda a acao.</summary>
        public static string Coluna(Acao acao) => acao switch
        {
            Acao.Ver => "ver",
            Acao.Criar => "criar",
            Acao.Editar => "editar",
            Acao.Excluir => "excluir",
            Acao.Exportar => "exportar",
            Acao.Estornar => "estornar",
            Acao.Liquidar => "liquidar",
            Acao.VerValores => "ver_valores",
            _ => throw new ArgumentOutOfRangeException(nameof(acao), acao, "Ação sem coluna.")
        };

        /// <summary>Cabecalho da coluna na matriz.</summary>
        public static string Rotulo(Acao acao) => acao switch
        {
            Acao.Ver => "Acessar",
            Acao.Criar => "Criar",
            Acao.Editar => "Editar",
            Acao.Excluir => "Excluir",
            Acao.Exportar => "Exportar",
            Acao.Estornar => "Estornar",
            Acao.Liquidar => "Liquidar",
            Acao.VerValores => "Ver valores",
            _ => acao.ToString()
        };

        /// <summary>
        /// O que a acao significa, para a dica na matriz. As sensiveis
        /// precisam explicar o que concedem antes de alguem marcar.
        /// </summary>
        public static string Explicacao(Acao acao) => acao switch
        {
            Acao.Ver => "Abrir a tela. Sem isto, nenhuma das outras ações vale.",
            Acao.Criar => "Cadastrar registros novos.",
            Acao.Editar => "Alterar registros existentes.",
            Acao.Excluir => "Apagar registros.",
            Acao.Exportar => "Baixar o relatório em PDF e a planilha.",
            Acao.Estornar => "Cancelar uma venda já lançada.",
            Acao.Liquidar => "Movimentar dinheiro: abrir e fechar o caixa, sangria, suprimento e pagamento da folha.",
            Acao.VerValores => "Ver faturamento, lucro e comissões no painel.",
            _ => ""
        };

        /// <summary>
        /// Acoes sensiveis: ficam desligadas no "Acesso total" e no modelo
        /// de membro novo. Quem precisa delas recebe uma por uma.
        /// </summary>
        public static bool EhSensivel(Acao acao) =>
            acao is Acao.Excluir or Acao.Estornar or Acao.Liquidar or Acao.VerValores;
    }
}
