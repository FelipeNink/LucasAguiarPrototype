using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Dados de exemplo para as telas terem o que mostrar: clientes, vendas
    /// dos ultimos dias, agendamentos (passados e da semana), despesas e um
    /// acesso de barbeiro para a tela Meu dia.
    ///
    /// Roda fora do site, pela linha de comando:
    ///   dotnet run -- --dados-exemplo      carrega
    ///   dotnet run -- --remover-exemplo    apaga tudo o que carregou
    ///
    /// Grava pelos DAOs do sistema, e nao por SQL solto: comissao, baixa de
    /// estoque e conflito de agenda saem iguais a uma venda de verdade.
    /// Cada id criado fica anotado em <c>dados_exemplo</c>, e e por ela que
    /// a remocao sabe o que apagar sem tocar no que a equipe cadastrou.
    ///
    /// Usa os profissionais, servicos e produtos que ja existem; nao cria
    /// cadastro novo deles.
    /// </summary>
    public class DadosExemplo
    {
        private readonly Conexao _conexao;
        private readonly ClienteDAO _clientes;
        private readonly FuncionarioDAO _funcionarios;
        private readonly ServicoDAO _servicos;
        private readonly ProdutoDAO _produtos;
        private readonly VendaDAO _vendas;
        private readonly AgendamentoDAO _agendamentos;
        private readonly DespesaDAO _despesas;
        private readonly HorarioDAO _horario;
        private readonly EquipeDAO _equipe;

        /// <summary>Dias para tras com vendas e atendimentos.</summary>
        private const int DiasPassados = 30;

        /// <summary>Dias para frente com horarios marcados.</summary>
        private const int DiasFuturos = 7;

        private const string Usuario = "Dados de exemplo";
        private const string Marca = "[exemplo]";

        /// <summary>Acesso do barbeiro de exemplo, para abrir a tela Meu dia.</summary>
        public const string LoginBarbeiro = "barbeiro";
        public const string SenhaBarbeiro = "barbeiro123";

        // Semente fixa: rodar de novo num banco limpo gera o mesmo movimento.
        private readonly Random _sorte = new(20261022);

        public DadosExemplo(Conexao conexao, ClienteDAO clientes, FuncionarioDAO funcionarios,
                            ServicoDAO servicos, ProdutoDAO produtos, VendaDAO vendas,
                            AgendamentoDAO agendamentos, DespesaDAO despesas, HorarioDAO horario,
                            EquipeDAO equipe)
        {
            _conexao = conexao;
            _clientes = clientes;
            _funcionarios = funcionarios;
            _servicos = servicos;
            _produtos = produtos;
            _vendas = vendas;
            _agendamentos = agendamentos;
            _despesas = despesas;
            _horario = horario;
            _equipe = equipe;
        }

        // =============================================================
        // CARREGAR
        // =============================================================

        public void Carregar(Action<string> log)
        {
            CriarTabelaDeControle();

            if (Contar() > 0)
                throw new Exception("Os dados de exemplo já foram carregados. Rode --remover-exemplo antes de carregar de novo.");

            // Venda de exemplo nao pode virar nota na prefeitura.
            if (EmissaoFiscalAtiva())
                throw new Exception("A emissão de nota fiscal está ativa: desligue em Notas fiscais › Configuração antes de carregar dados de exemplo.");

            var profissionais = _funcionarios.ListarTodos().Where(f => f.Ativo).ToList();
            var servicos = _servicos.ListarTodos().Where(s => s.Ativo && s.PrecoServico > 0).ToList();
            var produtos = _produtos.ListarTodos().Where(p => p.Ativo && p.Valor > 0).ToList();

            if (profissionais.Count == 0) throw new Exception("Cadastre ao menos um profissional ativo antes.");
            if (servicos.Count == 0) throw new Exception("Cadastre ao menos um serviço ativo antes.");

            var precos = _funcionarios.MapaServicos();
            var grade = HorarioDaSemana();

            var clientes = CriarClientes();
            log($"{clientes.Count} clientes");

            var (vendas, atendidos, faltas) = CriarHistorico(profissionais, servicos, produtos, precos, grade, clientes);
            log($"{vendas} vendas, {atendidos} agendamentos concluídos e {faltas} faltas nos últimos {DiasPassados} dias");

            var futuros = CriarAgenda(profissionais, servicos, precos, grade, clientes);
            log($"{futuros} agendamentos nos próximos {DiasFuturos} dias");

            log($"{CriarDespesas()} despesas");

            var barbeiro = CriarBarbeiro(profissionais);
            log(barbeiro);
        }

        private List<int> CriarClientes()
        {
            var nomes = new[]
            {
                "Rafael Souza", "Gustavo Lima", "Pedro Henrique Alves", "Lucas Martins", "Mateus Ribeiro",
                "João Vitor Costa", "Felipe Andrade", "Bruno Carvalho", "Diego Fernandes", "André Moreira",
                "Thiago Barbosa", "Vinícius Rocha", "Leonardo Pires", "Caio Nogueira", "Eduardo Teixeira",
                "Marcelo Duarte", "Rodrigo Farias", "Gabriel Monteiro"
            };

            var ids = new List<int>();
            var hoje = DateTime.Today;

            for (var i = 0; i < nomes.Length; i++)
            {
                // Numero ficticio, da faixa 9 0000-00xx, que nao pertence a
                // ninguem: o botao de WhatsApp nao pode chamar um estranho.
                var telefone = Documentos.FormatarTelefone($"4890000{i + 10:0000}");

                // Tres aniversariantes no mes, para o cartao do painel.
                DateTime? nascimento = i < 3
                    ? new DateTime(1990 + i, hoje.Month, Math.Min(hoje.Day + i * 3, DateTime.DaysInMonth(hoje.Year, hoje.Month)))
                    : new DateTime(1980 + _sorte.Next(25), _sorte.Next(1, 13), _sorte.Next(1, 28));

                var id = _clientes.Inserir(new Cliente
                {
                    NomeCliente = nomes[i],
                    Telefone = telefone,
                    DataNascimento = nascimento,
                    Origem = OrigemCliente.Todas[_sorte.Next(OrigemCliente.Todas.Length)].Valor,
                    Observacoes = $"{Marca} Cliente fictício para demonstração.",
                    Cidade = "Florianópolis",
                    Estado = "SC"
                });

                Anotar("cliente", id);
                ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Os ultimos dias: por profissional e dia aberto, de zero a tres
        /// atendimentos. Dois tercos chegam por agendamento (que fecha em
        /// venda, ou vira falta); o resto e cliente de balcao.
        /// </summary>
        private (int Vendas, int Atendidos, int Faltas) CriarHistorico(
            List<Funcionario> profissionais, List<Servico> servicos, List<Produto> produtos,
            Dictionary<(int, int), FuncionarioServico> precos, Dictionary<DayOfWeek, HorarioDia> grade,
            List<int> clientes)
        {
            int vendas = 0, atendidos = 0, faltas = 0;
            // No maximo um terco do saldo de cada produto: o exemplo nao
            // pode zerar o estoque de verdade nem disparar reposicao.
            var estoque = produtos.ToDictionary(p => p.IdProduto, p => Math.Max(0, p.Quantidade / 3));

            for (var dia = DateTime.Today.AddDays(-DiasPassados); dia <= DateTime.Today; dia = dia.AddDays(1))
            {
                if (!grade.TryGetValue(dia.DayOfWeek, out var expediente) || !expediente.Aberto) continue;

                foreach (var profissional in profissionais)
                {
                    foreach (var (inicio, servico) in Horarios(dia, expediente, servicos, _sorte.Next(0, 4)))
                    {
                        // Hoje so o que ja passou: o resto do dia e agenda.
                        if (inicio.AddMinutes(servico.DuracaoServico) > DateTime.Now) continue;

                        var idCliente = _sorte.NextDouble() < 0.85 ? clientes[_sorte.Next(clientes.Count)] : (int?)null;
                        int? idAgendamento = null;

                        if (idCliente.HasValue && _sorte.NextDouble() < 0.66)
                        {
                            idAgendamento = Agendar(inicio, servico, profissional, idCliente.Value);
                            if (idAgendamento == null) continue;

                            if (_sorte.NextDouble() < 0.08)
                            {
                                _agendamentos.MarcarFalta(idAgendamento.Value);
                                faltas++;
                                continue;
                            }
                        }

                        var idVenda = Vender(inicio.AddMinutes(servico.DuracaoServico), servico, profissional,
                                             idCliente, produtos, estoque, precos);
                        vendas++;

                        if (idAgendamento.HasValue)
                        {
                            _agendamentos.Concluir(idAgendamento.Value, idVenda);
                            atendidos++;
                        }
                    }
                }
            }

            return (vendas, atendidos, faltas);
        }

        /// <summary>Horarios ainda em aberto: de agora ate o fim da semana que vem.</summary>
        private int CriarAgenda(
            List<Funcionario> profissionais, List<Servico> servicos,
            Dictionary<(int, int), FuncionarioServico> precos, Dictionary<DayOfWeek, HorarioDia> grade,
            List<int> clientes)
        {
            var total = 0;

            for (var dia = DateTime.Today; dia <= DateTime.Today.AddDays(DiasFuturos); dia = dia.AddDays(1))
            {
                if (!grade.TryGetValue(dia.DayOfWeek, out var expediente) || !expediente.Aberto) continue;

                foreach (var profissional in profissionais)
                {
                    foreach (var (inicio, servico) in Horarios(dia, expediente, servicos, _sorte.Next(1, 5)))
                    {
                        if (inicio <= DateTime.Now) continue;

                        if (Agendar(inicio, servico, profissional, clientes[_sorte.Next(clientes.Count)]) != null)
                            total++;
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Ate <paramref name="quantos"/> horarios dentro do expediente do
        /// dia, em ordem, sem se cruzar e sem cair no almoco (12h as 13h).
        /// </summary>
        private IEnumerable<(DateTime Inicio, Servico Servico)> Horarios(
            DateTime dia, HorarioDia expediente, List<Servico> servicos, int quantos)
        {
            var hora = dia.Date + expediente.Abre;
            var fecha = dia.Date + expediente.Fecha;
            var almoco = dia.Date.AddHours(12);

            for (var i = 0; i < quantos; i++)
            {
                // Pula de meia em meia hora, para a agenda nao ficar colada.
                hora = hora.AddMinutes(30 * _sorte.Next(0, 6));

                var servico = servicos[_sorte.Next(servicos.Count)];
                var duracao = Math.Max(15, servico.DuracaoServico);

                if (hora < almoco.AddHours(1) && hora.AddMinutes(duracao) > almoco) hora = almoco.AddHours(1);
                if (hora.AddMinutes(duracao) > fecha) yield break;

                yield return (hora, servico);
                hora = hora.AddMinutes(duracao);
            }
        }

        private int? Agendar(DateTime inicio, Servico servico, Funcionario profissional, int idCliente)
        {
            try
            {
                var id = _agendamentos.Inserir(new Agendamento
                {
                    DataHora = inicio,
                    DuracaoMin = Math.Max(15, servico.DuracaoServico),
                    IdCliente = idCliente,
                    IdFuncionario = profissional.IdFuncionario,
                    IdServico = servico.IdServico,
                    Observacao = Marca
                });

                Anotar("agendamento", id);
                return id;
            }
            catch
            {
                // Horario ocupado por um agendamento de verdade: so pula.
                return null;
            }
        }

        private int Vender(DateTime quando, Servico servico, Funcionario profissional, int? idCliente,
                           List<Produto> produtos, Dictionary<int, int> estoque,
                           Dictionary<(int, int), FuncionarioServico> precos)
        {
            precos.TryGetValue((profissional.IdFuncionario, servico.IdServico), out var doProfissional);
            var preco = doProfissional?.Preco ?? (decimal)servico.PrecoServico;

            var venda = new Venda
            {
                DataVenda = quando,
                IdCliente = idCliente,
                IdFuncionario = profissional.IdFuncionario,
                Usuario = Usuario,
                FormaPagamentoVenda = SortearForma(),
                Itens =
                {
                    new VendaItem
                    {
                        TipoItem = TipoItem.Servico,
                        IdReferencia = servico.IdServico,
                        Descricao = servico.NomeServico ?? "Serviço",
                        Quantidade = 1,
                        ValorUnitario = preco,
                        ComissaoUnitaria = RegraComissao.Servico(servico, profissional, doProfissional, preco)
                    }
                }
            };

            // Um em cada cinco leva um produto, so dos que tem saldo.
            var comSaldo = produtos.Where(p => estoque[p.IdProduto] > 0).ToList();
            if (comSaldo.Count > 0 && _sorte.NextDouble() < 0.2)
            {
                var produto = comSaldo[_sorte.Next(comSaldo.Count)];
                var valor = (decimal)produto.Valor;
                estoque[produto.IdProduto]--;

                venda.Itens.Add(new VendaItem
                {
                    TipoItem = TipoItem.Produto,
                    IdReferencia = produto.IdProduto,
                    Descricao = produto.NomeProduto ?? "Produto",
                    Quantidade = 1,
                    ValorUnitario = valor,
                    ComissaoUnitaria = RegraComissao.Produto(produto, profissional, valor)
                });
            }

            var id = _vendas.Inserir(venda);
            Anotar("venda", id);
            return id;
        }

        /// <summary>Pix na frente, como e no balcao hoje; dinheiro e cartao depois.</summary>
        private string SortearForma()
        {
            var x = _sorte.NextDouble();
            return x < 0.45 ? FormaPagamento.Pix
                 : x < 0.65 ? FormaPagamento.Debito
                 : x < 0.85 ? FormaPagamento.Credito
                 : FormaPagamento.Dinheiro;
        }

        /// <summary>Contas pagas do mes, por Pix: nenhuma sai da gaveta do caixa.</summary>
        private int CriarDespesas()
        {
            var hoje = DateTime.Today;
            var contas = new (int DiasAtras, string Categoria, string Descricao, decimal Valor)[]
            {
                (25, "ENERGIA", "Conta de luz", 312.40m),
                (22, "PRODUTOS", "Lâminas, toalhas descartáveis e álcool", 186.90m),
                (15, "MARKETING", "Impulsionamento no Instagram", 150.00m),
                (9, "MANUTENCAO", "Afiação das tesouras", 120.00m),
                (4, "PRODUTOS", "Reposição de pomadas e shampoo", 248.00m)
            };

            foreach (var c in contas)
            {
                var data = hoje.AddDays(-c.DiasAtras);
                var id = _despesas.Inserir(new Despesa
                {
                    DataDespesa = data,
                    DataVencimento = data,
                    DataPagamento = data,
                    Categoria = c.Categoria,
                    Descricao = c.Descricao,
                    Valor = c.Valor,
                    FormaPagamentoDespesa = FormaPagamento.Pix,
                    Pago = true,
                    Observacao = Marca
                });
                Anotar("despesa", id);
            }

            return contas.Length;
        }

        /// <summary>
        /// Acesso comum ligado a um profissional, so com o que o barbeiro
        /// usa: Meu dia, a agenda e lancar venda. Sem o painel, ele entra
        /// direto em Meu dia.
        /// </summary>
        private string CriarBarbeiro(List<Funcionario> profissionais)
        {
            var jaLigados = _equipe.Listar().Where(u => u.IdFuncionario.HasValue).Select(u => u.IdFuncionario!.Value).ToHashSet();
            var profissional = profissionais.FirstOrDefault(f => !jaLigados.Contains(f.IdFuncionario));

            if (profissional == null)
                return "Todos os profissionais já têm acesso: nenhum acesso de barbeiro criado.";

            if (_equipe.Listar(LoginBarbeiro).Any(u => string.Equals(u.NomeUsr, LoginBarbeiro, StringComparison.OrdinalIgnoreCase)))
                return $"O usuário \"{LoginBarbeiro}\" já existe: nenhum acesso de barbeiro criado.";

            var matriz = new Dictionary<string, Acao>
            {
                ["meudia"] = Acao.Ver,
                ["agendamentos"] = Acao.Ver,
                ["vendas"] = Acao.Ver | Acao.Criar,
                ["clientes"] = Acao.Ver
            };

            var id = _equipe.Criar(new Usuario
            {
                NomeUsr = LoginBarbeiro,
                NomeExibicao = profissional.NomeFuncionario,
                PerfilUsr = Perfil.Comum,
                IdFuncionario = profissional.IdFuncionario
            }, SenhaBarbeiro, matriz, exigirTrocaDeSenha: false, Autor.Sistema);

            Anotar("usuario", id);
            return $"Acesso de barbeiro: usuário \"{LoginBarbeiro}\", senha \"{SenhaBarbeiro}\", ligado a {profissional.NomeFuncionario}";
        }

        // =============================================================
        // REMOVER
        // =============================================================

        /// <summary>
        /// Apaga so o que esta anotado em <c>dados_exemplo</c>, do dependente
        /// para o cadastro: agendamentos, vendas (devolvendo o estoque),
        /// despesas, o acesso do barbeiro e os clientes.
        /// </summary>
        public void Remover(Action<string> log)
        {
            CriarTabelaDeControle();

            var agendamentos = Anotados("agendamento");
            ApagarPorId("DELETE FROM agendamento_servico WHERE id_agend_fk = @id", agendamentos);
            ApagarPorId("DELETE FROM agendamento WHERE id_agend = @id", agendamentos);
            Esquecer("agendamento", agendamentos);
            log($"{agendamentos.Count} agendamentos apagados");

            var vendas = Anotados("venda");
            foreach (var id in vendas)
            {
                // Cancelar devolve ao estoque o produto vendido; ja cancelada
                // por alguem, a devolucao ja aconteceu.
                try { _vendas.Cancelar(id, "Remoção dos dados de exemplo", Usuario); } catch { }
            }
            ApagarPorId("DELETE FROM movimento_estoque WHERE id_ref = @id AND motivo LIKE 'Venda%'", vendas);
            ApagarPorId("DELETE FROM nota_fiscal WHERE id_vend_fk = @id", vendas);
            ApagarPorId("DELETE FROM venda_pagamento WHERE id_vend_fk = @id", vendas);
            ApagarPorId("DELETE FROM venda_item WHERE id_vend_fk = @id", vendas);
            ApagarPorId("DELETE FROM venda WHERE id_vend = @id", vendas);
            Esquecer("venda", vendas);
            log($"{vendas.Count} vendas apagadas, com o estoque devolvido");

            var despesas = Anotados("despesa");
            foreach (var id in despesas) _despesas.Excluir(id);
            Esquecer("despesa", despesas);
            log($"{despesas.Count} despesas apagadas");

            foreach (var id in Anotados("usuario"))
            {
                try
                {
                    _equipe.Excluir(id, Autor.Sistema);
                    Esquecer("usuario", new List<int> { id });
                    log("Acesso do barbeiro apagado");
                }
                catch (Exception ex) { log("Acesso do barbeiro mantido: " + ex.Message); }
            }

            var clientes = Anotados("cliente");
            var apagados = new List<int>();
            foreach (var id in clientes)
            {
                // Cliente de exemplo que a equipe usou numa venda de verdade fica.
                try { _clientes.Excluir(id); apagados.Add(id); } catch { }
            }
            Esquecer("cliente", apagados);
            log($"{apagados.Count} clientes apagados"
                + (apagados.Count < clientes.Count ? $", {clientes.Count - apagados.Count} mantidos por terem uso real" : ""));

            // Sobrou alguma coisa mantida: a tabela fica, com o que restou.
            if (Contar() > 0) return;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("DROP TABLE dados_exemplo", conexao);
            comando.ExecuteNonQuery();
        }

        // =============================================================
        // APOIO
        // =============================================================

        private Dictionary<DayOfWeek, HorarioDia> HorarioDaSemana()
        {
            var grade = _horario.Listar().ToDictionary(h => h.Dia);
            if (grade.Count > 0) return grade;

            // Sem grade cadastrada: segunda a sabado, das 9h as 19h.
            return Enum.GetValues<DayOfWeek>().ToDictionary(d => d,
                d => new HorarioDia { Dia = d, Aberto = d != DayOfWeek.Sunday });
        }

        private bool EmissaoFiscalAtiva()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT COALESCE(MAX(ativo), 0) FROM config_fiscal", conexao);
            return Convert.ToInt32(comando.ExecuteScalar()) == 1;
        }

        private void CriarTabelaDeControle()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                CREATE TABLE IF NOT EXISTS `dados_exemplo` (
                  `tabela` varchar(30) NOT NULL,
                  `id` int NOT NULL,
                  PRIMARY KEY (`tabela`, `id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci", conexao);
            comando.ExecuteNonQuery();
        }

        private int Contar()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT COUNT(*) FROM dados_exemplo", conexao);
            return Convert.ToInt32(comando.ExecuteScalar());
        }

        private void Anotar(string tabela, int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "INSERT INTO dados_exemplo (tabela, id) VALUES (@tabela, @id)", conexao);
            comando.Parameters.AddWithValue("@tabela", tabela);
            comando.Parameters.AddWithValue("@id", id);
            comando.ExecuteNonQuery();
        }

        private List<int> Anotados(string tabela)
        {
            var ids = new List<int>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT id FROM dados_exemplo WHERE tabela = @tabela ORDER BY id", conexao);
            comando.Parameters.AddWithValue("@tabela", tabela);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) ids.Add(leitor.GetInt32(0));
            return ids;
        }

        private void Esquecer(string tabela, List<int> ids) =>
            ApagarPorId("DELETE FROM dados_exemplo WHERE tabela = @tabela AND id = @id", ids, tabela);

        /// <summary>Um DELETE por id, todos numa transacao.</summary>
        private void ApagarPorId(string sql, List<int> ids, string? tabela = null)
        {
            if (ids.Count == 0) return;

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            foreach (var id in ids)
            {
                using var comando = new MySqlCommand(sql, conexao, transacao);
                comando.Parameters.AddWithValue("@id", id);
                if (tabela != null) comando.Parameters.AddWithValue("@tabela", tabela);
                comando.ExecuteNonQuery();
            }

            transacao.Commit();
        }
    }
}
