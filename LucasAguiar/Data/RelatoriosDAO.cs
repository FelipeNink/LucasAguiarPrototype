using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Os números que o dono abre para entender o mês: quanto entrou e por
    /// quem, quanto vale um atendimento, o que mais sai, quantos faltam,
    /// quem sumiu e quanto da agenda está de fato ocupado.
    ///
    /// Tudo apurado direto no banco e sempre ignorando venda cancelada,
    /// que é a mesma regra do painel, do caixa e das comissões.
    /// </summary>
    public class RelatoriosDAO
    {
        private readonly Conexao _conexao;

        public RelatoriosDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>Venda cancelada nunca conta em lugar nenhum.</summary>
        private const string Validas = "v.status_vend <> 'CANCELADA'";

        // =============================================================
        // 1 e 2 — FATURAMENTO E TICKET MEDIO
        // =============================================================

        public class LinhaProfissional
        {
            public int IdFuncionario { get; set; }
            public string Nome { get; set; } = "";
            public int Atendimentos { get; set; }
            public decimal Faturamento { get; set; }
            public decimal Servicos { get; set; }
            public decimal Produtos { get; set; }

            public decimal TicketMedio =>
                Atendimentos == 0 ? 0 : Math.Round(Faturamento / Atendimentos, 2);

            /// <summary>Quanto este profissional representa do total do período.</summary>
            public decimal Participacao { get; set; }
        }

        /// <summary>
        /// Faturamento por profissional no período, com a divisão entre
        /// serviço e produto -- que é o que mostra quem vende o que, e não
        /// só quem fatura mais.
        ///
        /// Atendimento é venda distinta, não item: uma venda com corte e
        /// pomada é um atendimento só, senão o ticket médio sairia menor
        /// do que a realidade.
        /// </summary>
        public List<LinhaProfissional> FaturamentoPorProfissional(DateTime de, DateTime ate)
        {
            var lista = new List<LinhaProfissional>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand($@"
                SELECT f.id_fun,
                       COALESCE(f.nome_fun, 'Sem profissional') AS nome,
                       COUNT(DISTINCT v.id_vend) AS atendimentos,
                       COALESCE(SUM(v.valor_vend), 0) AS faturamento,
                       COALESCE((
                           SELECT SUM(i.valor_total)
                             FROM venda_item i
                             JOIN venda vv ON vv.id_vend = i.id_vend_fk
                            WHERE vv.id_fun_fk <=> v.id_fun_fk
                              AND vv.data_vend >= @de AND vv.data_vend < @ate
                              AND vv.status_vend <> 'CANCELADA'
                              AND i.tipo_item = 'SERVICO'), 0) AS servicos,
                       COALESCE((
                           SELECT SUM(i.valor_total)
                             FROM venda_item i
                             JOIN venda vv ON vv.id_vend = i.id_vend_fk
                            WHERE vv.id_fun_fk <=> v.id_fun_fk
                              AND vv.data_vend >= @de AND vv.data_vend < @ate
                              AND vv.status_vend <> 'CANCELADA'
                              AND i.tipo_item = 'PRODUTO'), 0) AS produtos
                  FROM venda v
                  LEFT JOIN funcionario f ON f.id_fun = v.id_fun_fk
                 WHERE v.data_vend >= @de AND v.data_vend < @ate AND {Validas}
                 GROUP BY v.id_fun_fk, f.id_fun, f.nome_fun
                 ORDER BY faturamento DESC", conexao))
            {
                comando.Parameters.AddWithValue("@de", de);
                comando.Parameters.AddWithValue("@ate", ate);

                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                {
                    lista.Add(new LinhaProfissional
                    {
                        IdFuncionario = DAOHelper.IsNull(leitor, "id_fun")
                            ? 0 : leitor.GetInt32("id_fun"),
                        Nome = leitor.GetString("nome"),
                        Atendimentos = leitor.GetInt32("atendimentos"),
                        Faturamento = leitor.GetDecimal("faturamento"),
                        Servicos = leitor.GetDecimal("servicos"),
                        Produtos = leitor.GetDecimal("produtos")
                    });
                }
            }

            var total = lista.Sum(l => l.Faturamento);

            foreach (var linha in lista)
            {
                linha.Participacao = total == 0
                    ? 0
                    : Math.Round(linha.Faturamento / total * 100, 1);
            }

            return lista;
        }

        // =============================================================
        // 3 — MAIS VENDIDOS
        // =============================================================

        public class LinhaItem
        {
            public string Tipo { get; set; } = "";
            public string Descricao { get; set; } = "";
            public int Quantidade { get; set; }
            public decimal Total { get; set; }

            /// <summary>Quantas vezes saiu coberto por plano, sem entrar no caixa.</summary>
            public int PeloPlano { get; set; }

            public bool EhServico =>
                string.Equals(Tipo, "SERVICO", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Serviços e produtos mais vendidos no período.
        ///
        /// Agrupa por id_ref quando existe, e pela descrição quando não --
        /// item avulso, digitado na hora da venda, não tem cadastro para
        /// apontar. O que saiu pelo plano aparece na contagem mas não
        /// infla o valor: ali não entrou dinheiro.
        /// </summary>
        public List<LinhaItem> MaisVendidos(DateTime de, DateTime ate, string? tipo = null, int limite = 10)
        {
            var lista = new List<LinhaItem>();
            var filtroTipo = string.IsNullOrWhiteSpace(tipo) ? "" : " AND i.tipo_item = @tipo";

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT i.tipo_item,
                       MIN(i.descricao) AS descricao,
                       SUM(i.quantidade) AS quantidade,
                       SUM(CASE WHEN i.coberto_plano = 1 THEN i.quantidade ELSE 0 END) AS pelo_plano,
                       SUM(i.valor_total) AS total
                  FROM venda_item i
                  JOIN venda v ON v.id_vend = i.id_vend_fk
                 WHERE v.data_vend >= @de AND v.data_vend < @ate AND {Validas}{filtroTipo}
                 -- A chave de agrupamento e o cadastro quando ele existe, e
                 -- a descricao quando nao -- item avulso nao tem id_ref. O
                 -- id_ref nao pode aparecer no SELECT por nao estar no
                 -- GROUP BY: com only_full_group_by, que e o padrao do
                 -- MySQL 8, a consulta e recusada.
                 GROUP BY i.tipo_item,
                          CASE WHEN i.id_ref IS NULL OR i.id_ref = 0
                               THEN CONCAT('t:', i.descricao)
                               ELSE CONCAT('r:', i.id_ref) END
                 ORDER BY quantidade DESC, total DESC
                 LIMIT @limite", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);
            comando.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, 100));

            if (!string.IsNullOrWhiteSpace(tipo))
                comando.Parameters.AddWithValue("@tipo", tipo);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new LinhaItem
                {
                    Tipo = leitor.GetString("tipo_item"),
                    Descricao = DAOHelper.GetString(leitor, "descricao"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    PeloPlano = leitor.GetInt32("pelo_plano"),
                    Total = leitor.GetDecimal("total")
                });
            }

            return lista;
        }

        // =============================================================
        // 4 — FALTAS
        // =============================================================

        public class ResumoFaltas
        {
            public int Concluidos { get; set; }
            public int Faltas { get; set; }
            public int Cancelados { get; set; }

            /// <summary>Marcados cujo horário ainda não chegou: não contam.</summary>
            public int Pendentes { get; set; }

            /// <summary>
            /// Base da taxa: o que já deveria ter acontecido. Cancelamento
            /// fica de fora -- quem avisa que não vem não é falta.
            /// </summary>
            public int Encerrados => Concluidos + Faltas;

            public decimal Taxa =>
                Encerrados == 0 ? 0 : Math.Round((decimal)Faltas / Encerrados * 100, 1);

            /// <summary>Encerrados e faltas so de quem confirmou pelo link do lembrete.</summary>
            public int EncerradosConfirmados { get; set; }
            public int FaltasConfirmados { get; set; }

            public int EncerradosSemConfirmar => Encerrados - EncerradosConfirmados;
            public int FaltasSemConfirmar => Faltas - FaltasConfirmados;

            /// <summary>Taxa de falta de quem confirmou x de quem nao confirmou: mede o lembrete.</summary>
            public decimal TaxaConfirmados =>
                EncerradosConfirmados == 0 ? 0 : Math.Round((decimal)FaltasConfirmados / EncerradosConfirmados * 100, 1);

            public decimal TaxaSemConfirmar =>
                EncerradosSemConfirmar == 0 ? 0 : Math.Round((decimal)FaltasSemConfirmar / EncerradosSemConfirmar * 100, 1);
        }

        /// <summary>
        /// Faltas do período.
        ///
        /// Falta é o agendamento marcado como FALTOU e também o que ficou
        /// AGENDADO com o horário já passado, que ninguém concluiu nem
        /// cancelou -- é o rastro que um cliente que não apareceu deixa
        /// quando a falta não foi marcada.
        ///
        /// Cancelado é contado à parte, de propósito: desmarcar com aviso
        /// é outra coisa, e somar os dois esconderia justamente a
        /// diferença que interessa ao dono.
        /// </summary>
        public ResumoFaltas Faltas(DateTime de, DateTime ate)
        {
            var resumo = new ResumoFaltas();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT
                    SUM(status = 'CONCLUIDO') AS concluidos,
                    SUM(status = 'CANCELADO') AS cancelados,
                    SUM(status = 'FALTOU' OR (status = 'AGENDADO' AND data_hora < NOW())) AS faltas,
                    SUM(status = 'AGENDADO' AND data_hora >= NOW()) AS pendentes,
                    SUM(confirmado_em IS NOT NULL AND (status IN ('CONCLUIDO', 'FALTOU')
                        OR (status = 'AGENDADO' AND data_hora < NOW()))) AS encerrados_confirmados,
                    SUM(confirmado_em IS NOT NULL AND (status = 'FALTOU'
                        OR (status = 'AGENDADO' AND data_hora < NOW()))) AS faltas_confirmados
                  FROM agendamento
                 WHERE data_hora >= @de AND data_hora < @ate", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);

            using var leitor = comando.ExecuteReader();
            if (leitor.Read())
            {
                resumo.Concluidos = Inteiro(leitor, "concluidos");
                resumo.Cancelados = Inteiro(leitor, "cancelados");
                resumo.Faltas = Inteiro(leitor, "faltas");
                resumo.Pendentes = Inteiro(leitor, "pendentes");
                resumo.EncerradosConfirmados = Inteiro(leitor, "encerrados_confirmados");
                resumo.FaltasConfirmados = Inteiro(leitor, "faltas_confirmados");
            }

            return resumo;
        }

        /// <summary>Quem faltou, para o dono saber de quem se trata.</summary>
        public List<(string Cliente, string Telefone, string Profissional, DateTime Quando)>
            QuemFaltou(DateTime de, DateTime ate, int limite = 30)
        {
            var lista = new List<(string, string, string, DateTime)>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT a.data_hora,
                       COALESCE(c.nome_cli, '—') AS cliente,
                       COALESCE(c.telefone_cli, '') AS telefone,
                       COALESCE(f.nome_fun, '—') AS profissional
                  FROM agendamento a
                  LEFT JOIN cliente c ON c.id_cli = a.id_cli_fk
                  LEFT JOIN funcionario f ON f.id_fun = a.id_fun_fk
                 WHERE a.data_hora >= @de AND a.data_hora < @ate
                   AND (a.status = 'FALTOU' OR (a.status = 'AGENDADO' AND a.data_hora < NOW()))
                 ORDER BY a.data_hora DESC
                 LIMIT @limite", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);
            comando.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, 200));

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add((
                    leitor.GetString("cliente"),
                    leitor.GetString("telefone"),
                    leitor.GetString("profissional"),
                    leitor.GetDateTime("data_hora")));
            }

            return lista;
        }

        // =============================================================
        // 5 — CLIENTES INATIVOS
        // =============================================================

        public class LinhaInativo
        {
            public int IdCliente { get; set; }
            public string Nome { get; set; } = "";
            public string? Telefone { get; set; }
            public DateTime? UltimaVisita { get; set; }
            public decimal TotalGasto { get; set; }
            public int Visitas { get; set; }

            /// <summary>Null quando a pessoa nunca veio.</summary>
            public int? DiasSemVir => UltimaVisita == null
                ? null
                : (int)(DateTime.Today - UltimaVisita.Value.Date).TotalDays;
        }

        /// <summary>
        /// Clientes sem vir há mais de <paramref name="dias"/>.
        ///
        /// Quem nunca comprou entra no fim da lista, e não de fora: é
        /// cadastro que nunca virou cliente, e isso também é informação.
        /// Ordenado da visita mais antiga para a mais recente, que é a
        /// ordem de quem está mais perto de ser perdido.
        /// </summary>
        public List<LinhaInativo> ClientesInativos(int dias, int limite = 100)
        {
            var lista = new List<LinhaInativo>();
            var limite_ = DateTime.Today.AddDays(-Math.Max(1, dias));

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand($@"
                SELECT c.id_cli, c.nome_cli, c.telefone_cli,
                       MAX(v.data_vend) AS ultima,
                       COUNT(v.id_vend) AS visitas,
                       COALESCE(SUM(v.valor_vend), 0) AS gasto
                  FROM cliente c
                  LEFT JOIN venda v
                         ON v.id_cli_fk = c.id_cli AND {Validas}
                 GROUP BY c.id_cli, c.nome_cli, c.telefone_cli
                HAVING ultima IS NULL OR ultima < @limite
                 ORDER BY ultima IS NULL, ultima
                 LIMIT @quantos", conexao);

            comando.Parameters.AddWithValue("@limite", limite_);
            comando.Parameters.AddWithValue("@quantos", Math.Clamp(limite, 1, 500));

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new LinhaInativo
                {
                    IdCliente = leitor.GetInt32("id_cli"),
                    Nome = DAOHelper.GetString(leitor, "nome_cli"),
                    Telefone = DAOHelper.GetString(leitor, "telefone_cli"),
                    UltimaVisita = DAOHelper.GetDateTime(leitor, "ultima"),
                    Visitas = leitor.GetInt32("visitas"),
                    TotalGasto = leitor.GetDecimal("gasto")
                });
            }

            return lista;
        }

        // =============================================================
        // 6 — OCUPACAO DA AGENDA
        // =============================================================

        /// <summary>
        /// Jornada de referência da barbearia: 9h às 19h, de segunda a
        /// sábado. Fica aqui, e não no banco, porque o sistema ainda não
        /// tem cadastro de horário de funcionamento -- quando tiver, é
        /// este bloco que sai.
        /// </summary>
        public static class Jornada
        {
            public const int Abre = 9;
            public const int Fecha = 19;

            public static int MinutosPorDia => (Fecha - Abre) * 60;

            public static bool EhDiaUtil(DateTime dia) =>
                dia.DayOfWeek != DayOfWeek.Sunday;

            public static string Descricao =>
                $"{Abre}h às {Fecha}h, de segunda a sábado";

            /// <summary>Dias de funcionamento no intervalo, domingo fora.</summary>
            public static int DiasNoIntervalo(DateTime de, DateTime ate)
            {
                var dias = 0;

                for (var dia = de.Date; dia < ate.Date; dia = dia.AddDays(1))
                {
                    if (EhDiaUtil(dia)) dias++;
                }

                return dias;
            }
        }

        public class LinhaOcupacao
        {
            public string Nome { get; set; } = "";
            public int Agendamentos { get; set; }
            public int MinutosAgendados { get; set; }
            public int MinutosDisponiveis { get; set; }

            public decimal Ocupacao => MinutosDisponiveis == 0
                ? 0
                : Math.Round((decimal)MinutosAgendados / MinutosDisponiveis * 100, 1);

            public string HorasAgendadas =>
                $"{MinutosAgendados / 60}h{(MinutosAgendados % 60):00}";
        }

        /// <summary>
        /// Quanto da agenda de cada profissional está de fato ocupado.
        ///
        /// Numerador: minutos de agendamento que não foram cancelados --
        /// o que foi concluído e o que está marcado. Denominador: dias de
        /// funcionamento no período vezes a jornada.
        ///
        /// Cancelado fica de fora dos dois lados: horário desmarcado
        /// voltou a ficar livre.
        /// </summary>
        public List<LinhaOcupacao> OcupacaoDaAgenda(DateTime de, DateTime ate)
        {
            var lista = new List<LinhaOcupacao>();
            var disponiveis = Jornada.DiasNoIntervalo(de, ate) * Jornada.MinutosPorDia;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT COALESCE(f.nome_fun, 'Sem profissional') AS nome,
                       COUNT(*) AS quantos,
                       COALESCE(SUM(a.duracao_min), 0) AS minutos
                  FROM agendamento a
                  LEFT JOIN funcionario f ON f.id_fun = a.id_fun_fk
                 WHERE a.data_hora >= @de AND a.data_hora < @ate
                   AND a.status <> 'CANCELADO'
                 GROUP BY a.id_fun_fk, f.nome_fun
                 ORDER BY minutos DESC", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new LinhaOcupacao
                {
                    Nome = leitor.GetString("nome"),
                    Agendamentos = leitor.GetInt32("quantos"),
                    MinutosAgendados = leitor.GetInt32("minutos"),
                    MinutosDisponiveis = disponiveis
                });
            }

            return lista;
        }

        // ---------- apoio ----------

        /// <summary>
        /// SUM(condicao) no MySQL volta como decimal, e null quando não há
        /// nenhuma linha no período.
        /// </summary>
        private static int Inteiro(MySqlDataReader leitor, string coluna)
        {
            var posicao = leitor.GetOrdinal(coluna);

            return leitor.IsDBNull(posicao) ? 0 : Convert.ToInt32(leitor.GetValue(posicao));
        }
    }
}
