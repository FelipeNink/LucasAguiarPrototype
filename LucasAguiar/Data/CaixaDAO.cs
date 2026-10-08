using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class CaixaDAO
    {
        private readonly Conexao _conexao;

        public CaixaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>Sessão aberta no momento, ou null se não houver.</summary>
        public Caixa? BuscarAberto()
        {
            using var conexao = _conexao.GetConnection();

            Caixa? caixa = null;
            using (var comando = new MySqlCommand(
                "SELECT * FROM caixa WHERE status = 'ABERTO' ORDER BY data_abertura DESC LIMIT 1", conexao))
            using (var leitor = comando.ExecuteReader())
            {
                if (leitor.Read()) caixa = Ler(leitor);
            }

            if (caixa != null)
            {
                caixa.Movimentos = ListarMovimentos(conexao, caixa.IdCaixa);
                caixa.Resumo = Apurar(conexao, caixa.IdCaixa);
            }

            return caixa;
        }

        public Caixa? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();

            Caixa? caixa = null;
            using (var comando = new MySqlCommand("SELECT * FROM caixa WHERE id_caixa = @id", conexao))
            {
                comando.Parameters.AddWithValue("@id", id);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read()) caixa = Ler(leitor);
            }

            if (caixa != null)
            {
                caixa.Movimentos = ListarMovimentos(conexao, caixa.IdCaixa);
                caixa.Resumo = Apurar(conexao, caixa.IdCaixa);
            }

            return caixa;
        }

        public List<Caixa> Listar(int limite = 60)
        {
            var lista = new List<Caixa>();

            using var conexao = _conexao.GetConnection();
            using (var comando = new MySqlCommand(
                "SELECT * FROM caixa ORDER BY data_abertura DESC LIMIT @limite", conexao))
            {
                comando.Parameters.AddWithValue("@limite", limite);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read()) lista.Add(Ler(leitor));
            }

            foreach (var caixa in lista)
                caixa.Resumo = Apurar(conexao, caixa.IdCaixa);

            return lista;
        }

        public int Abrir(decimal valorAbertura, string? usuario, string? observacao = null)
        {
            if (valorAbertura < 0)
                throw new Exception("O valor de abertura não pode ser negativo.");

            if (BuscarAberto() != null)
                throw new Exception("Já existe um caixa aberto. Feche-o antes de abrir outro.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO caixa (data_abertura, valor_abertura, status, usuario_abertura, observacao)
                VALUES (@data, @valor, 'ABERTO', @usuario, @observacao);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@data", DateTime.Now);
            comando.Parameters.AddWithValue("@valor", valorAbertura);
            comando.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
            comando.Parameters.AddWithValue("@observacao",
                string.IsNullOrWhiteSpace(observacao) ? (object)DBNull.Value : observacao);

            return Convert.ToInt32(comando.ExecuteScalar());
        }


        /// <summary>
        /// Recusa uma saida em dinheiro maior do que ha na gaveta.
        ///
        /// A sangria ja fazia esta conferencia, mas despesa em dinheiro e
        /// folha de pagamento gravavam direto na tabela despesa, sem passar
        /// por aqui -- e por isso o caixa aceitava ficar negativo.
        /// </summary>
        /// <param name="oQue">Como a saida e chamada na mensagem de erro.</param>
        public void GarantirSaldoParaSaida(int idCaixa, decimal valor, string oQue)
        {
            if (idCaixa <= 0) return;

            var caixa = BuscarPorId(idCaixa)
                ?? throw new Exception("Caixa não encontrado.");

            if (!caixa.EstaAberto)
                throw new Exception("Este caixa já está fechado.");

            var disponivel = caixa.Resumo.SaldoEsperadoGaveta(caixa.ValorAbertura);
            if (valor > disponivel)
                throw new Exception(
                    $"{oQue} de {valor:C} é maior que o dinheiro em caixa ({disponivel:C}).");
        }

        public void RegistrarMovimento(int idCaixa, string tipo, decimal valor, string? descricao, string? usuario)
        {
            if (valor <= 0)
                throw new Exception("O valor precisa ser maior que zero.");

            var caixa = BuscarPorId(idCaixa)
                ?? throw new Exception("Caixa não encontrado.");

            if (!caixa.EstaAberto)
                throw new Exception("Este caixa já está fechado.");

            if (tipo == TipoMovimento.Sangria)
                GarantirSaldoParaSaida(idCaixa, valor, "A sangria");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO movimento_caixa (id_caixa_fk, tipo, valor, descricao, data_hora, usuario)
                VALUES (@idCaixa, @tipo, @valor, @descricao, @dataHora, @usuario)", conexao);

            comando.Parameters.AddWithValue("@idCaixa", idCaixa);
            comando.Parameters.AddWithValue("@tipo", tipo);
            comando.Parameters.AddWithValue("@valor", valor);
            comando.Parameters.AddWithValue("@descricao",
                string.IsNullOrWhiteSpace(descricao) ? (object)DBNull.Value : descricao.Trim());
            comando.Parameters.AddWithValue("@dataHora", DateTime.Now);
            comando.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);

            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Fecha a sessão. O operador informa o que contou na gaveta e o
        /// sistema apura a diferença — conferência às cegas, sem mostrar o
        /// esperado antes.
        /// </summary>
        public Caixa Fechar(int idCaixa, decimal valorContado, string? usuario, string? observacao = null)
        {
            if (valorContado < 0)
                throw new Exception("O valor contado não pode ser negativo.");

            var caixa = BuscarPorId(idCaixa)
                ?? throw new Exception("Caixa não encontrado.");

            if (!caixa.EstaAberto)
                throw new Exception("Este caixa já está fechado.");

            var esperado = caixa.Resumo.SaldoEsperadoGaveta(caixa.ValorAbertura);
            var diferenca = decimal.Round(valorContado - esperado, 2);

            using (var conexao = _conexao.GetConnection())
            using (var comando = new MySqlCommand(@"
                UPDATE caixa
                SET data_fechamento = @data, valor_informado = @informado,
                    valor_calculado = @calculado, diferenca = @diferenca,
                    status = 'FECHADO', usuario_fechamento = @usuario,
                    observacao = COALESCE(@observacao, observacao)
                WHERE id_caixa = @idCaixa AND status = 'ABERTO'", conexao))
            {
                comando.Parameters.AddWithValue("@data", DateTime.Now);
                comando.Parameters.AddWithValue("@informado", valorContado);
                comando.Parameters.AddWithValue("@calculado", esperado);
                comando.Parameters.AddWithValue("@diferenca", diferenca);
                comando.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
                comando.Parameters.AddWithValue("@observacao",
                    string.IsNullOrWhiteSpace(observacao) ? (object)DBNull.Value : observacao);
                comando.Parameters.AddWithValue("@idCaixa", idCaixa);

                if (comando.ExecuteNonQuery() == 0)
                    throw new Exception("O caixa foi fechado por outra sessão. Recarregue a tela.");
            }

            return BuscarPorId(idCaixa)!;
        }

        private static List<MovimentoCaixa> ListarMovimentos(MySqlConnection conexao, int idCaixa)
        {
            var lista = new List<MovimentoCaixa>();

            using var comando = new MySqlCommand(
                "SELECT * FROM movimento_caixa WHERE id_caixa_fk = @id ORDER BY data_hora", conexao);
            comando.Parameters.AddWithValue("@id", idCaixa);

            using var leitor = comando.ExecuteReader();
            var iDesc = leitor.GetOrdinal("descricao");
            var iUsr = leitor.GetOrdinal("usuario");

            while (leitor.Read())
            {
                lista.Add(new MovimentoCaixa
                {
                    IdMovimento = leitor.GetInt32("id_mov"),
                    IdCaixa = leitor.GetInt32("id_caixa_fk"),
                    Tipo = leitor.GetString("tipo"),
                    Valor = leitor.GetDecimal("valor"),
                    Descricao = leitor.IsDBNull(iDesc) ? null : leitor.GetString(iDesc),
                    DataHora = leitor.GetDateTime("data_hora"),
                    Usuario = leitor.IsDBNull(iUsr) ? null : leitor.GetString(iUsr)
                });
            }

            return lista;
        }

        /// <summary>
        /// Soma tudo que aconteceu na sessão, separando por forma de pagamento.
        /// A separação importa: só o dinheiro passa pela gaveta.
        /// </summary>
        private static ResumoCaixa Apurar(MySqlConnection conexao, int idCaixa)
        {
            var resumo = new ResumoCaixa();

            using (var comando = new MySqlCommand(@"
                SELECT forma_pagamento_vend forma, COUNT(*) qtd, SUM(valor_vend) total
                FROM venda
                WHERE id_caixa_fk = @id AND status_vend <> 'CANCELADA'
                GROUP BY forma_pagamento_vend", conexao))
            {
                comando.Parameters.AddWithValue("@id", idCaixa);
                using var leitor = comando.ExecuteReader();
                while (leitor.Read())
                {
                    var forma = leitor.IsDBNull(leitor.GetOrdinal("forma"))
                        ? "" : leitor.GetString("forma");
                    var total = leitor.IsDBNull(leitor.GetOrdinal("total"))
                        ? 0m : leitor.GetDecimal("total");

                    resumo.QuantidadeVendas += leitor.GetInt32("qtd");

                    switch (forma)
                    {
                        case FormaPagamento.Dinheiro: resumo.VendasDinheiro = total; break;
                        case FormaPagamento.Pix: resumo.VendasPix = total; break;
                        case FormaPagamento.Debito: resumo.VendasDebito = total; break;
                        case FormaPagamento.Credito: resumo.VendasCredito = total; break;
                    }
                }
            }

            using (var comando = new MySqlCommand(@"
                SELECT COALESCE(SUM(vi.comissao * vi.quantidade), 0) total
                FROM venda_item vi
                JOIN venda v ON v.id_vend = vi.id_vend_fk
                WHERE v.id_caixa_fk = @id AND v.status_vend <> 'CANCELADA'", conexao))
            {
                comando.Parameters.AddWithValue("@id", idCaixa);
                resumo.Comissoes = Convert.ToDecimal(comando.ExecuteScalar());
            }

            using (var comando = new MySqlCommand(@"
                SELECT
                  COALESCE(SUM(CASE WHEN forma_pagamento = 'DINHEIRO' AND pago = 1 THEN valor ELSE 0 END), 0) dinheiro,
                  COALESCE(SUM(CASE WHEN forma_pagamento <> 'DINHEIRO' OR pago = 0 THEN valor ELSE 0 END), 0) outras
                FROM despesa WHERE id_caixa_fk = @id", conexao))
            {
                comando.Parameters.AddWithValue("@id", idCaixa);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read())
                {
                    resumo.DespesasDinheiro = leitor.GetDecimal("dinheiro");
                    resumo.DespesasOutras = leitor.GetDecimal("outras");
                }
            }

            using (var comando = new MySqlCommand(@"
                SELECT
                  COALESCE(SUM(CASE WHEN tipo = 'SANGRIA' THEN valor ELSE 0 END), 0) sangrias,
                  COALESCE(SUM(CASE WHEN tipo = 'SUPRIMENTO' THEN valor ELSE 0 END), 0) suprimentos
                FROM movimento_caixa WHERE id_caixa_fk = @id", conexao))
            {
                comando.Parameters.AddWithValue("@id", idCaixa);
                using var leitor = comando.ExecuteReader();
                if (leitor.Read())
                {
                    resumo.Sangrias = leitor.GetDecimal("sangrias");
                    resumo.Suprimentos = leitor.GetDecimal("suprimentos");
                }
            }

            return resumo;
        }

        private static Caixa Ler(MySqlDataReader leitor)
        {
            decimal? DecimalOuNulo(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetDecimal(i);
            }

            string? TextoOuNulo(string coluna)
            {
                var i = leitor.GetOrdinal(coluna);
                return leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            var iFech = leitor.GetOrdinal("data_fechamento");

            return new Caixa
            {
                IdCaixa = leitor.GetInt32("id_caixa"),
                DataAbertura = leitor.GetDateTime("data_abertura"),
                DataFechamento = leitor.IsDBNull(iFech) ? null : leitor.GetDateTime(iFech),
                ValorAbertura = leitor.GetDecimal("valor_abertura"),
                ValorInformado = DecimalOuNulo("valor_informado"),
                ValorCalculado = DecimalOuNulo("valor_calculado"),
                Diferenca = DecimalOuNulo("diferenca"),
                Status = leitor.GetString("status"),
                UsuarioAbertura = TextoOuNulo("usuario_abertura"),
                UsuarioFechamento = TextoOuNulo("usuario_fechamento"),
                Observacao = TextoOuNulo("observacao")
            };
        }
    }
}
