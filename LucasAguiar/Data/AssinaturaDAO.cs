using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class AssinaturaDAO
    {
        private readonly Conexao _conexao;

        /// <summary>Validade padrão dos créditos, em dias.</summary>
        public const int ValidadePadraoDias = 30;

        public AssinaturaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Todas as assinaturas não encerradas do cliente, com os saldos.
        /// Um cliente pode manter vários planos ao mesmo tempo, com serviços
        /// diferentes, então aqui vem a lista inteira.
        ///
        /// Traz também as vencidas de propósito: quem decide o que fazer com
        /// uma assinatura vencida é a tela, que oferece a renovação. Filtrar
        /// aqui fazia o sistema dizer que o cliente nunca comprou o plano.
        /// </summary>
        public List<Assinatura> ListarAtivasDoCliente(int idCliente)
        {
            var lista = new List<Assinatura>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(@"
                SELECT a.*, p.nome_plan
                FROM assinatura a
                JOIN plano p ON p.id_plan = a.id_plan_fk
                WHERE a.id_cli_fk = @idCliente
                  AND a.status = 'ATIVA'
                ORDER BY a.data_inicio DESC", conexao))
            {
                comando.Parameters.AddWithValue("@idCliente", idCliente);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read()) lista.Add(Ler(leitor));
            }

            foreach (var assinatura in lista)
                assinatura.Saldos = ListarSaldos(conexao, null, assinatura.IdAssinatura);

            return lista;
        }

        public List<Assinatura> ListarDoCliente(int idCliente)
        {
            var lista = new List<Assinatura>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(@"
                SELECT a.*, p.nome_plan
                FROM assinatura a
                JOIN plano p ON p.id_plan = a.id_plan_fk
                WHERE a.id_cli_fk = @idCliente
                ORDER BY a.data_inicio DESC", conexao))
            {
                comando.Parameters.AddWithValue("@idCliente", idCliente);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read()) lista.Add(Ler(leitor));
            }

            foreach (var a in lista)
                a.Saldos = ListarSaldos(conexao, null, a.IdAssinatura);

            return lista;
        }

        private static List<AssinaturaSaldo> ListarSaldos(
            MySqlConnection conexao, MySqlTransaction? transacao, int idAssinatura)
        {
            var saldos = new List<AssinaturaSaldo>();

            using var comando = new MySqlCommand(
                "SELECT * FROM assinatura_saldo WHERE id_assinatura_fk = @id ORDER BY id_saldo",
                conexao, transacao);
            comando.Parameters.AddWithValue("@id", idAssinatura);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                saldos.Add(new AssinaturaSaldo
                {
                    IdSaldo = leitor.GetInt32("id_saldo"),
                    IdAssinatura = leitor.GetInt32("id_assinatura_fk"),
                    TipoItem = leitor.GetString("tipo_item"),
                    IdReferencia = leitor.GetInt32("id_ref"),
                    Descricao = leitor.GetString("descricao"),
                    ValorReferencia = leitor.GetDecimal("valor_referencia"),
                    QuantidadeTotal = leitor.GetInt32("quantidade_total"),
                    QuantidadeUsada = leitor.GetInt32("quantidade_usada")
                });
            }

            return saldos;
        }

        /// <summary>
        /// Cria a assinatura e seus créditos a partir dos componentes do plano.
        /// Chamado de dentro da transação da venda que vendeu o plano.
        /// </summary>
        public static int CriarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao,
            int idCliente, int idPlano, decimal valorPago, int idVenda,
            IEnumerable<PlanoItem> componentes, int validadeDias = ValidadePadraoDias)
        {
            // Planos se acumulam: um cliente pode manter varias assinaturas
            // ativas ao mesmo tempo, com servicos diferentes. Comprar um plano
            // novo nao encerra os que ele ja tem.
            int idAssinatura;
            using (var comando = new MySqlCommand(@"
                INSERT INTO assinatura
                    (id_cli_fk, id_plan_fk, data_inicio, data_fim, valor_pago, id_vend_fk, status)
                VALUES
                    (@idCliente, @idPlano, @inicio, @fim, @valor, @idVenda, 'ATIVA');
                SELECT LAST_INSERT_ID();", conexao, transacao))
            {
                comando.Parameters.AddWithValue("@idCliente", idCliente);
                comando.Parameters.AddWithValue("@idPlano", idPlano);
                comando.Parameters.AddWithValue("@inicio", DateTime.Today);
                comando.Parameters.AddWithValue("@fim", DateTime.Today.AddDays(validadeDias));
                comando.Parameters.AddWithValue("@valor", valorPago);
                comando.Parameters.AddWithValue("@idVenda", idVenda);

                idAssinatura = Convert.ToInt32(comando.ExecuteScalar());
            }

            foreach (var item in componentes)
            {
                using var comando = new MySqlCommand(@"
                    INSERT INTO assinatura_saldo
                        (id_assinatura_fk, tipo_item, id_ref, descricao,
                         valor_referencia, quantidade_total, quantidade_usada)
                    VALUES
                        (@idAssinatura, @tipo, @idRef, @descricao, @valor, @qtd, 0)",
                    conexao, transacao);

                comando.Parameters.AddWithValue("@idAssinatura", idAssinatura);
                comando.Parameters.AddWithValue("@tipo", item.TipoItem);
                comando.Parameters.AddWithValue("@idRef", item.IdReferencia);
                comando.Parameters.AddWithValue("@descricao", item.Descricao ?? "Item do plano");
                comando.Parameters.AddWithValue("@valor", item.ValorUnitario);
                comando.Parameters.AddWithValue("@qtd", Math.Max(1, item.Quantidade));
                comando.ExecuteNonQuery();
            }

            return idAssinatura;
        }

        /// <summary>
        /// Abate créditos. A condição no UPDATE impede que duas vendas
        /// simultâneas consumam o mesmo crédito.
        /// </summary>
        public static void ConsumirNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idSaldo, int quantidade)
        {
            using var comando = new MySqlCommand(@"
                UPDATE assinatura_saldo
                SET quantidade_usada = quantidade_usada + @qtd
                WHERE id_saldo = @idSaldo
                  AND quantidade_usada + @qtd <= quantidade_total", conexao, transacao);

            comando.Parameters.AddWithValue("@idSaldo", idSaldo);
            comando.Parameters.AddWithValue("@qtd", quantidade);

            if (comando.ExecuteNonQuery() == 0)
                throw new Exception("Saldo do plano insuficiente. Recarregue a tela e refaça a venda.");
        }

        private static Assinatura Ler(MySqlDataReader leitor)
        {
            var iFim = leitor.GetOrdinal("data_fim");
            var iVenda = leitor.GetOrdinal("id_vend_fk");

            return new Assinatura
            {
                IdAssinatura = leitor.GetInt32("id_assinatura"),
                IdCliente = leitor.GetInt32("id_cli_fk"),
                IdPlano = leitor.GetInt32("id_plan_fk"),
                DataInicio = leitor.GetDateTime("data_inicio"),
                DataFim = leitor.IsDBNull(iFim) ? null : leitor.GetDateTime(iFim),
                ValorPago = leitor.GetDecimal("valor_pago"),
                IdVenda = leitor.IsDBNull(iVenda) ? null : leitor.GetInt32(iVenda),
                Status = leitor.GetString("status"),
                NomePlano = leitor.GetString("nome_plan")
            };
        }
    }
}
