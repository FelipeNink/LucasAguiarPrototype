using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Movimentação de estoque.
    ///
    /// O saldo continua em <c>produto.quantidade_prod</c>, que é o que as
    /// telas de venda e de produto leem. O que muda é que ninguém mais o
    /// altera sozinho: toda mudança passa por
    /// <see cref="RegistrarNaTransacao"/>, que aplica a diferença e grava
    /// a linha correspondente com o saldo que ficou.
    ///
    /// Por isso o extrato de um produto sempre fecha com o saldo dele --
    /// e, quando não fecha, a linha onde a conta se perdeu está visível.
    /// </summary>
    public class EstoqueDAO
    {
        private readonly Conexao _conexao;

        public EstoqueDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        // =============================================================
        // O REGISTRO
        // =============================================================

        /// <summary>
        /// Aplica <paramref name="quantidade"/> ao saldo do produto e grava
        /// o movimento. Positivo entra, negativo sai.
        ///
        /// Dentro da transação de quem chamou, de propósito: dar baixa no
        /// estoque e registrar a venda têm de ser a mesma operação, senão
        /// uma falha no meio deixa o estoque dizendo uma coisa e a venda
        /// outra.
        ///
        /// Devolve o saldo depois do movimento.
        /// </summary>
        public static int RegistrarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao,
            int idProduto, string tipo, int quantidade,
            string? motivo = null, int? idReferencia = null, string? usuario = null)
        {
            if (idProduto <= 0) return 0;

            // FOR UPDATE: duas vendas do mesmo produto ao mesmo tempo não
            // podem ler o mesmo saldo e as duas gravarem o mesmo resultado.
            int saldoAntes;

            using (var leitura = new MySqlCommand(
                "SELECT COALESCE(quantidade_prod, 0) FROM produto WHERE id_prod = @id FOR UPDATE",
                conexao, transacao))
            {
                leitura.Parameters.AddWithValue("@id", idProduto);

                var valor = leitura.ExecuteScalar();
                if (valor == null) return 0;

                saldoAntes = Convert.ToInt32(valor);
            }

            var saldoDepois = saldoAntes + quantidade;

            using (var atualizar = new MySqlCommand(
                "UPDATE produto SET quantidade_prod = @saldo WHERE id_prod = @id",
                conexao, transacao))
            {
                atualizar.Parameters.AddWithValue("@saldo", saldoDepois);
                atualizar.Parameters.AddWithValue("@id", idProduto);
                atualizar.ExecuteNonQuery();
            }

            using (var registrar = new MySqlCommand(@"
                INSERT INTO movimento_estoque
                    (id_prod_fk, data_hora, tipo, quantidade, saldo_apos,
                     motivo, id_ref, usuario)
                VALUES (@produto, NOW(), @tipo, @quantidade, @saldo,
                        @motivo, @referencia, @usuario)", conexao, transacao))
            {
                registrar.Parameters.AddWithValue("@produto", idProduto);
                registrar.Parameters.AddWithValue("@tipo", tipo);
                registrar.Parameters.AddWithValue("@quantidade", quantidade);
                registrar.Parameters.AddWithValue("@saldo", saldoDepois);
                registrar.Parameters.AddWithValue("@motivo", Texto(motivo, 200));
                registrar.Parameters.AddWithValue("@referencia",
                    idReferencia.HasValue ? idReferencia.Value : DBNull.Value);
                registrar.Parameters.AddWithValue("@usuario", Texto(usuario, 100));

                registrar.ExecuteNonQuery();
            }

            return saldoDepois;
        }

        /// <summary>Mesma coisa, abrindo a transação -- para quem chama sozinho.</summary>
        public int Registrar(
            int idProduto, string tipo, int quantidade,
            string? motivo = null, int? idReferencia = null, string? usuario = null)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var saldo = RegistrarNaTransacao(
                conexao, transacao, idProduto, tipo, quantidade, motivo, idReferencia, usuario);

            transacao.Commit();
            return saldo;
        }

        // =============================================================
        // LANCAMENTOS DA TELA
        // =============================================================

        /// <summary>
        /// Consumo interno: a pomada usada no corte, a lâmina trocada, a
        /// química do serviço. Sai do estoque e não é venda -- era
        /// justamente o buraco que fazia o saldo nunca bater.
        /// </summary>
        public int Consumir(int idProduto, int quantidade, string? motivo, string? usuario)
        {
            if (quantidade <= 0)
                throw new Exception("Informe uma quantidade maior que zero.");

            return Registrar(idProduto, TipoMovimentoEstoque.Consumo, -quantidade, motivo, null, usuario);
        }

        /// <summary>Quebrou, venceu ou sumiu. Separado do consumo porque é outra história.</summary>
        public int RegistrarPerda(int idProduto, int quantidade, string? motivo, string? usuario)
        {
            if (quantidade <= 0)
                throw new Exception("Informe uma quantidade maior que zero.");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new Exception("Diga o que aconteceu com o produto.");

            return Registrar(idProduto, TipoMovimentoEstoque.Perda, -quantidade, motivo, null, usuario);
        }

        /// <summary>
        /// Inventário: a pessoa contou a prateleira e informa o que achou.
        /// O sistema grava a diferença, não o número contado -- é a
        /// diferença que conta a história.
        ///
        /// Contagem igual ao saldo não gera movimento nenhum: conferir e
        /// estar certo não é um evento.
        /// </summary>
        public int Inventariar(int idProduto, int contado, string? motivo, string? usuario)
        {
            if (contado < 0)
                throw new Exception("A contagem não pode ser negativa.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            int saldoAtual;

            using (var leitura = new MySqlCommand(
                "SELECT COALESCE(quantidade_prod, 0) FROM produto WHERE id_prod = @id FOR UPDATE",
                conexao, transacao))
            {
                leitura.Parameters.AddWithValue("@id", idProduto);

                var valor = leitura.ExecuteScalar()
                    ?? throw new Exception("Produto não encontrado.");

                saldoAtual = Convert.ToInt32(valor);
            }

            var diferenca = contado - saldoAtual;

            if (diferenca == 0)
            {
                transacao.Rollback();
                return saldoAtual;
            }

            var descricao = string.IsNullOrWhiteSpace(motivo)
                ? $"Contagem: {contado}. O sistema tinha {saldoAtual}."
                : $"Contagem: {contado}. O sistema tinha {saldoAtual}. {motivo.Trim()}";

            var saldo = RegistrarNaTransacao(
                conexao, transacao, idProduto, TipoMovimentoEstoque.Ajuste, diferenca,
                descricao, null, usuario);

            transacao.Commit();
            return saldo;
        }

        /// <summary>Estoque mínimo do produto: abaixo dele a tela avisa.</summary>
        public void DefinirMinimo(int idProduto, int minimo)
        {
            if (minimo < 0)
                throw new Exception("O estoque mínimo não pode ser negativo.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "UPDATE produto SET estoque_min_prod = @minimo WHERE id_prod = @id", conexao);

            comando.Parameters.AddWithValue("@minimo", minimo);
            comando.Parameters.AddWithValue("@id", idProduto);
            comando.ExecuteNonQuery();
        }

        // =============================================================
        // LEITURA
        // =============================================================

        public List<SaldoEstoque> Saldos(string? busca = null, bool somenteAtencao = false)
        {
            var lista = new List<SaldoEstoque>();
            var temBusca = !string.IsNullOrWhiteSpace(busca);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT p.id_prod, p.nome_prod, p.marca_prod,
                       COALESCE(p.quantidade_prod, 0) AS saldo,
                       COALESCE(p.estoque_min_prod, 0) AS minimo,
                       COALESCE(p.valor_prod, 0) AS valor,
                       (SELECT MAX(m.data_hora) FROM movimento_estoque m
                         WHERE m.id_prod_fk = p.id_prod) AS ultimo
                  FROM produto p"
                + (temBusca ? " WHERE p.nome_prod LIKE @busca OR p.marca_prod LIKE @busca" : "")
                + " ORDER BY p.nome_prod", conexao);

            if (temBusca)
                comando.Parameters.AddWithValue("@busca", "%" + busca!.Trim() + "%");

            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    lista.Add(new SaldoEstoque
                    {
                        IdProduto = leitor.GetInt32("id_prod"),
                        Nome = DAOHelper.GetString(leitor, "nome_prod"),
                        Marca = DAOHelper.GetString(leitor, "marca_prod"),
                        Saldo = leitor.GetInt32("saldo"),
                        Minimo = leitor.GetInt32("minimo"),
                        Valor = Convert.ToDecimal(leitor.GetValue(leitor.GetOrdinal("valor"))),
                        UltimoMovimento = DAOHelper.GetDateTime(leitor, "ultimo")
                    });
                }
            }

            return somenteAtencao
                ? lista.Where(s => s.PedeAtencao).ToList()
                : lista;
        }

        /// <summary>
        /// Os produtos que precisam de reposição, para o aviso do painel e
        /// da lista de produtos. Ordenados pelo mais grave primeiro.
        /// </summary>
        public List<SaldoEstoque> PedindoAtencao(int limite = 20)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT p.id_prod, p.nome_prod, p.marca_prod,
                       COALESCE(p.quantidade_prod, 0) AS saldo,
                       COALESCE(p.estoque_min_prod, 0) AS minimo,
                       COALESCE(p.valor_prod, 0) AS valor,
                       NULL AS ultimo
                  FROM produto p
                 WHERE COALESCE(p.quantidade_prod, 0) <= 0
                    OR (COALESCE(p.estoque_min_prod, 0) > 0
                        AND COALESCE(p.quantidade_prod, 0) <= COALESCE(p.estoque_min_prod, 0))
                 ORDER BY COALESCE(p.quantidade_prod, 0), p.nome_prod
                 LIMIT @limite", conexao);

            comando.Parameters.AddWithValue("@limite", Math.Clamp(limite, 1, 100));

            var lista = new List<SaldoEstoque>();

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new SaldoEstoque
                {
                    IdProduto = leitor.GetInt32("id_prod"),
                    Nome = DAOHelper.GetString(leitor, "nome_prod"),
                    Marca = DAOHelper.GetString(leitor, "marca_prod"),
                    Saldo = leitor.GetInt32("saldo"),
                    Minimo = leitor.GetInt32("minimo"),
                    Valor = Convert.ToDecimal(leitor.GetValue(leitor.GetOrdinal("valor")))
                });
            }

            return lista;
        }

        public class FiltroExtrato
        {
            public int? IdProduto { get; set; }
            public string? Tipo { get; set; }
            public DateTime? De { get; set; }
            public DateTime? Ate { get; set; }
            public int Limite { get; set; } = 80;
        }

        public List<MovimentoEstoque> Extrato(FiltroExtrato filtro)
        {
            var onde = new List<string>();

            if (filtro.IdProduto is > 0) onde.Add("m.id_prod_fk = @produto");
            if (!string.IsNullOrWhiteSpace(filtro.Tipo)) onde.Add("m.tipo = @tipo");
            if (filtro.De.HasValue) onde.Add("m.data_hora >= @de");
            if (filtro.Ate.HasValue) onde.Add("m.data_hora < @ate");

            var filtragem = onde.Count == 0 ? "" : " WHERE " + string.Join(" AND ", onde);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT m.id_mov_est, m.id_prod_fk, m.data_hora, m.tipo,
                       m.quantidade, m.saldo_apos, m.motivo, m.id_ref, m.usuario,
                       COALESCE(p.nome_prod, '—') AS nome_prod
                  FROM movimento_estoque m
                  LEFT JOIN produto p ON p.id_prod = m.id_prod_fk"
                + filtragem
                + " ORDER BY m.data_hora DESC, m.id_mov_est DESC LIMIT @limite", conexao);

            if (filtro.IdProduto is > 0)
                comando.Parameters.AddWithValue("@produto", filtro.IdProduto.Value);

            if (!string.IsNullOrWhiteSpace(filtro.Tipo))
                comando.Parameters.AddWithValue("@tipo", filtro.Tipo);

            if (filtro.De.HasValue) comando.Parameters.AddWithValue("@de", filtro.De.Value);
            if (filtro.Ate.HasValue) comando.Parameters.AddWithValue("@ate", filtro.Ate.Value);

            comando.Parameters.AddWithValue("@limite", Math.Clamp(filtro.Limite, 1, 500));

            var lista = new List<MovimentoEstoque>();

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new MovimentoEstoque
                {
                    IdMovimento = leitor.GetInt32("id_mov_est"),
                    IdProduto = leitor.GetInt32("id_prod_fk"),
                    NomeProduto = leitor.GetString("nome_prod"),
                    DataHora = leitor.GetDateTime("data_hora"),
                    Tipo = leitor.GetString("tipo"),
                    Quantidade = leitor.GetInt32("quantidade"),
                    SaldoApos = leitor.GetInt32("saldo_apos"),
                    Motivo = DAOHelper.GetString(leitor, "motivo"),
                    IdReferencia = DAOHelper.IsNull(leitor, "id_ref")
                        ? null : leitor.GetInt32("id_ref"),
                    Usuario = DAOHelper.GetString(leitor, "usuario")
                });
            }

            return lista;
        }

        /// <summary>Consumo interno por produto no período: onde o estoque some sem virar receita.</summary>
        public List<(string Produto, int Quantidade, decimal Custo)> ConsumoPorProduto(
            DateTime de, DateTime ate)
        {
            var lista = new List<(string, int, decimal)>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT COALESCE(p.nome_prod, '—') AS nome,
                       SUM(-m.quantidade) AS quantidade,
                       SUM(-m.quantidade * COALESCE(p.valor_prod, 0)) AS custo
                  FROM movimento_estoque m
                  LEFT JOIN produto p ON p.id_prod = m.id_prod_fk
                 WHERE m.tipo IN ('CONSUMO', 'PERDA')
                   AND m.data_hora >= @de AND m.data_hora < @ate
                 GROUP BY m.id_prod_fk, p.nome_prod
                 ORDER BY quantidade DESC", conexao);

            comando.Parameters.AddWithValue("@de", de);
            comando.Parameters.AddWithValue("@ate", ate);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add((
                    leitor.GetString("nome"),
                    Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("quantidade"))),
                    Convert.ToDecimal(leitor.GetValue(leitor.GetOrdinal("custo")))));
            }

            return lista;
        }

        private static object Texto(string? valor, int tamanho)
        {
            if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;

            var limpo = valor.Trim();
            return limpo.Length <= tamanho ? limpo : limpo[..tamanho];
        }
    }
}
