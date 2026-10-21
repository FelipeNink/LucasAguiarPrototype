using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Fila e historico das NFS-e, e a configuracao fiscal.
    ///
    /// A venda so poe a nota na fila, dentro da propria transacao. Quem
    /// conversa com a Focus e o EmissorNotasFiscais, fora dela: uma
    /// prefeitura fora do ar nao pode impedir de fechar uma venda.
    /// </summary>
    public class NotaFiscalDAO
    {
        private readonly Conexao _conexao;

        public NotaFiscalDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        // ---------- configuracao ----------

        public ConfigFiscal BuscarConfig()
        {
            using var conexao = _conexao.GetConnection();
            return BuscarConfig(conexao, null);
        }

        private static ConfigFiscal BuscarConfig(MySqlConnection conexao, MySqlTransaction? transacao)
        {
            using var comando = new MySqlCommand("SELECT * FROM config_fiscal WHERE id_config = 1", conexao, transacao);
            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return new ConfigFiscal();

            string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

            return new ConfigFiscal
            {
                Ativo = leitor.GetBoolean("ativo"),
                Ambiente = leitor.GetString("ambiente"),
                Cnpj = Texto("cnpj"),
                InscricaoMunicipal = Texto("inscricao_municipal"),
                RazaoSocial = Texto("razao_social"),
                CodigoMunicipio = Texto("codigo_municipio"),
                OpcaoSimples = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("opcao_simples"))),
                RegimeSimples = DAOHelper.IsNull(leitor, "regime_simples")
                    ? null : Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("regime_simples"))),
                RegimeEspecial = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("regime_especial"))),
                CodigoTributacaoNacional = leitor.GetString("codigo_tributacao_nacional"),
                CodigoTributacaoMunicipal = Texto("codigo_tributacao_municipal"),
                CodigoNbs = Texto("codigo_nbs"),
                AliquotaIss = DAOHelper.IsNull(leitor, "aliquota_iss") ? null : leitor.GetDecimal("aliquota_iss"),
                IbsCbsCst = Texto("ibs_cbs_cst"),
                IbsCbsClassificacao = Texto("ibs_cbs_classificacao"),
                SerieDps = leitor.GetInt32("serie_dps"),
                ProximoNumeroDps = leitor.GetInt64("proximo_numero_dps")
            };
        }

        public void SalvarConfig(ConfigFiscal config)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE config_fiscal SET
                    ativo = @ativo, ambiente = @ambiente, cnpj = @cnpj,
                    inscricao_municipal = @im, razao_social = @razao, codigo_municipio = @municipio,
                    opcao_simples = @opcao, regime_simples = @regime, regime_especial = @especial,
                    codigo_tributacao_nacional = @tribNac, codigo_tributacao_municipal = @tribMun,
                    codigo_nbs = @nbs, aliquota_iss = @aliquota,
                    ibs_cbs_cst = @cst, ibs_cbs_classificacao = @classTrib,
                    serie_dps = @serie, proximo_numero_dps = @proximo
                WHERE id_config = 1", conexao);

            comando.Parameters.AddWithValue("@ativo", config.Ativo ? 1 : 0);
            comando.Parameters.AddWithValue("@ambiente", config.Ambiente);
            comando.Parameters.AddWithValue("@cnpj", config.Cnpj ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@im", config.InscricaoMunicipal ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@razao", config.RazaoSocial ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@municipio", config.CodigoMunicipio ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@opcao", config.OpcaoSimples);
            comando.Parameters.AddWithValue("@regime", config.RegimeSimples ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@especial", config.RegimeEspecial);
            comando.Parameters.AddWithValue("@tribNac", config.CodigoTributacaoNacional);
            comando.Parameters.AddWithValue("@tribMun", config.CodigoTributacaoMunicipal ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@nbs", config.CodigoNbs ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@aliquota", config.AliquotaIss ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cst", config.IbsCbsCst ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@classTrib", config.IbsCbsClassificacao ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@serie", config.SerieDps);
            comando.Parameters.AddWithValue("@proximo", config.ProximoNumeroDps);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Numero da DPS, um por tentativa de envio. O LAST_INSERT_ID(expr)
        /// faz ler e incrementar num comando so, sem dois envios pegarem o
        /// mesmo numero. Lacuna na sequencia da DPS e permitida.
        /// </summary>
        public (int Serie, long Numero) ReservarNumeroDps()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE config_fiscal
                   SET proximo_numero_dps = LAST_INSERT_ID(proximo_numero_dps) + 1
                 WHERE id_config = 1;
                SELECT serie_dps, LAST_INSERT_ID() AS numero FROM config_fiscal WHERE id_config = 1;", conexao);

            using var leitor = comando.ExecuteReader();
            leitor.Read();
            return (leitor.GetInt32("serie_dps"), Convert.ToInt64(leitor.GetValue(leitor.GetOrdinal("numero"))));
        }

        // ---------- ligacao com a venda ----------

        /// <summary>
        /// Poe a NFS-e da venda na fila. Chamado ao registrar e ao editar a
        /// venda, na transacao dela. Idempotente:
        ///   - venda sem valor tributavel descarta o que estiver na fila;
        ///   - nota em ERRO volta para a fila, com os dados novos da venda;
        ///   - nota ja enviada ou autorizada nao e tocada.
        /// </summary>
        public static void EnfileirarNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idVenda, Venda venda)
        {
            if (!BuscarConfig(conexao, transacao).Ativo) return;

            var valor = RegrasNfse.Calcular(venda).Liquido;

            if (valor <= 0)
            {
                using var descartar = new MySqlCommand(@"
                    UPDATE nota_fiscal SET status = 'DESCARTADA', mensagem = 'Venda sem serviço a tributar.'
                    WHERE id_vend_fk = @id AND status IN ('PENDENTE', 'ERRO')", conexao, transacao);
                descartar.Parameters.AddWithValue("@id", idVenda);
                descartar.ExecuteNonQuery();
                return;
            }

            using (var vigente = new MySqlCommand(@"
                SELECT COUNT(*) FROM nota_fiscal
                WHERE id_vend_fk = @id AND status IN ('PENDENTE', 'PROCESSANDO', 'AUTORIZADA')", conexao, transacao))
            {
                vigente.Parameters.AddWithValue("@id", idVenda);
                if (Convert.ToInt32(vigente.ExecuteScalar()) > 0) return;
            }

            using (var reabrir = new MySqlCommand(@"
                UPDATE nota_fiscal
                   SET status = 'PENDENTE', valor = @valor, mensagem = NULL, proxima_tentativa = NULL
                 WHERE id_vend_fk = @id AND status = 'ERRO'", conexao, transacao))
            {
                reabrir.Parameters.AddWithValue("@id", idVenda);
                reabrir.Parameters.AddWithValue("@valor", valor);
                if (reabrir.ExecuteNonQuery() > 0) return;
            }

            using var inserir = new MySqlCommand(@"
                INSERT INTO nota_fiscal (id_vend_fk, tipo, status, valor)
                VALUES (@id, 'NFSE', 'PENDENTE', @valor)", conexao, transacao);
            inserir.Parameters.AddWithValue("@id", idVenda);
            inserir.Parameters.AddWithValue("@valor", valor);
            inserir.ExecuteNonQuery();
        }

        /// <summary>
        /// Venda cancelada: o que nao saiu e descartado; o que saiu ou esta
        /// saindo fica marcado para o emissor cancelar na prefeitura.
        /// </summary>
        public static void CancelarDaVendaNaTransacao(
            MySqlConnection conexao, MySqlTransaction transacao, int idVenda, string justificativa)
        {
            using var comando = new MySqlCommand(@"
                UPDATE nota_fiscal SET status = 'DESCARTADA'
                 WHERE id_vend_fk = @id AND status IN ('PENDENTE', 'ERRO');
                UPDATE nota_fiscal SET cancelar = 1, justificativa_cancelamento = @justificativa
                 WHERE id_vend_fk = @id AND status IN ('PROCESSANDO', 'AUTORIZADA');", conexao, transacao);
            comando.Parameters.AddWithValue("@id", idVenda);
            comando.Parameters.AddWithValue("@justificativa", Justificativa(justificativa));
            comando.ExecuteNonQuery();
        }

        /// <summary>O padrao nacional pede de 15 a 255 caracteres.</summary>
        public static string Justificativa(string texto)
        {
            var limpo = (texto ?? "").Trim();
            if (limpo.Length < 15) limpo = "Cancelamento: " + limpo;
            if (limpo.Length < 15) limpo = limpo.PadRight(15, '.');
            return limpo.Length <= 255 ? limpo : limpo[..255];
        }

        // ---------- tela ----------

        public List<NotaFiscal> Listar(DateTime? de, DateTime? ate)
        {
            var lista = new List<NotaFiscal>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT n.*, v.data_vend, v.status_vend, c.nome_cli
                FROM nota_fiscal n
                JOIN venda v ON v.id_vend = n.id_vend_fk
                LEFT JOIN cliente c ON c.id_cli = v.id_cli_fk
                WHERE (@de IS NULL OR v.data_vend >= @de)
                  AND (@ate IS NULL OR v.data_vend < @ate)
                ORDER BY n.id_nf DESC
                LIMIT 500", conexao);
            comando.Parameters.AddWithValue("@de", de?.Date ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@ate", ate?.Date.AddDays(1) ?? (object)DBNull.Value);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var nota = Ler(leitor);
                nota.DataVenda = DAOHelper.GetDateTime(leitor, "data_vend");
                nota.StatusVenda = DAOHelper.GetString(leitor, "status_vend");
                nota.NomeCliente = DAOHelper.IsNull(leitor, "nome_cli") ? null : leitor.GetString("nome_cli");
                lista.Add(nota);
            }

            return lista;
        }

        /// <summary>A nota mais recente da venda, ou nulo se ela nunca entrou na fila.</summary>
        public NotaFiscal? BuscarUltimaDaVenda(int idVenda)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT * FROM nota_fiscal WHERE id_vend_fk = @id ORDER BY id_nf DESC LIMIT 1", conexao);
            comando.Parameters.AddWithValue("@id", idVenda);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? Ler(leitor) : null;
        }

        /// <summary>
        /// Botao "Nota fiscal" da venda: poe na fila a venda que ficou sem
        /// nota -- feita antes de ligar a emissao, ou com a nota cancelada.
        /// </summary>
        public void EmitirManual(Venda venda)
        {
            if (venda.Status == StatusVenda.Cancelada)
                throw new Exception("Venda cancelada não emite nota.");
            if (RegrasNfse.Calcular(venda).Liquido <= 0)
                throw new Exception("Esta venda não tem serviço a tributar: produto e serviço coberto por plano ficam fora da NFS-e.");

            using var conexao = _conexao.GetConnection();
            if (!BuscarConfig(conexao, null).Ativo)
                throw new Exception("A emissão está desligada. Ligue em Notas fiscais > Configuração.");

            using var transacao = conexao.BeginTransaction();
            EnfileirarNaTransacao(conexao, transacao, venda.IdVenda, venda);
            transacao.Commit();
        }

        /// <summary>Depois de corrigir o cadastro: volta para a fila.</summary>
        public void Reenviar(int idNota)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE nota_fiscal SET status = 'PENDENTE', mensagem = NULL, proxima_tentativa = NULL
                WHERE id_nf = @id AND status = 'ERRO'", conexao);
            comando.Parameters.AddWithValue("@id", idNota);

            if (comando.ExecuteNonQuery() == 0)
                throw new Exception("Só nota com erro pode ser reenviada.");
        }

        /// <summary>Cancelamento pedido na tela, com a venda continuando valida.</summary>
        public void SolicitarCancelamento(int idNota, string justificativa)
        {
            if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Trim().Length < 15)
                throw new Exception("Escreva a justificativa com pelo menos 15 caracteres.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE nota_fiscal SET cancelar = 1, justificativa_cancelamento = @justificativa, mensagem = NULL
                WHERE id_nf = @id AND status = 'AUTORIZADA'", conexao);
            comando.Parameters.AddWithValue("@id", idNota);
            comando.Parameters.AddWithValue("@justificativa", Justificativa(justificativa));

            if (comando.ExecuteNonQuery() == 0)
                throw new Exception("Só nota autorizada pode ser cancelada.");
        }

        // ---------- emissor em segundo plano ----------

        /// <summary>Na fila e fora do intervalo de espera de uma falha anterior.</summary>
        public List<NotaFiscal> ListarParaEnviar(int limite) =>
            ListarOnde(@"status = 'PENDENTE'
                         AND (proxima_tentativa IS NULL OR proxima_tentativa <= NOW())", limite);

        public List<NotaFiscal> ListarProcessando(int limite) =>
            ListarOnde("status = 'PROCESSANDO'", limite);

        public List<NotaFiscal> ListarParaCancelar(int limite) =>
            ListarOnde("status = 'AUTORIZADA' AND cancelar = 1", limite);

        private List<NotaFiscal> ListarOnde(string condicao, int limite)
        {
            var lista = new List<NotaFiscal>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                $"SELECT * FROM nota_fiscal WHERE {condicao} ORDER BY id_nf LIMIT @limite", conexao);
            comando.Parameters.AddWithValue("@limite", limite);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(Ler(leitor));
            return lista;
        }

        /// <summary>
        /// Reserva a nota para um envio. Na troca de versao do Railway duas
        /// instancias rodam juntas por um instante; so uma ganha o UPDATE.
        /// </summary>
        public bool ReservarParaEnvio(int idNota)
        {
            return Executar(@"
                UPDATE nota_fiscal SET proxima_tentativa = NOW() + INTERVAL 2 MINUTE
                WHERE id_nf = @id AND status = 'PENDENTE'
                  AND (proxima_tentativa IS NULL OR proxima_tentativa <= NOW())",
                ("@id", idNota)) > 0;
        }

        public void MarcarProcessando(int idNota, string referencia, string ambiente, int serie, long numero, decimal valor)
        {
            Executar(@"
                UPDATE nota_fiscal
                   SET status = 'PROCESSANDO', referencia = @ref, ambiente = @ambiente,
                       serie_dps = @serie, numero_dps = @numero, valor = @valor,
                       tentativas = tentativas + 1, mensagem = NULL, proxima_tentativa = NULL
                 WHERE id_nf = @id",
                ("@id", idNota), ("@ref", referencia), ("@ambiente", ambiente),
                ("@serie", serie), ("@numero", numero), ("@valor", valor));
        }

        public void MarcarErro(int idNota, string mensagem)
        {
            Executar(@"
                UPDATE nota_fiscal SET status = 'ERRO', mensagem = @mensagem, proxima_tentativa = NULL
                WHERE id_nf = @id",
                ("@id", idNota), ("@mensagem", Cortar(mensagem)));
        }

        /// <summary>Falha de rede ou da Focus: tenta de novo mais tarde, sem perder a vez.</summary>
        public void Adiar(int idNota, string mensagem, TimeSpan espera)
        {
            Executar(@"
                UPDATE nota_fiscal
                   SET mensagem = @mensagem, tentativas = tentativas + 1,
                       proxima_tentativa = NOW() + INTERVAL @segundos SECOND
                 WHERE id_nf = @id",
                ("@id", idNota), ("@mensagem", Cortar(mensagem)), ("@segundos", (int)espera.TotalSeconds));
        }

        public void MarcarAutorizada(
            int idNota, string? numero, string? codigoVerificacao, string? url, string? urlPdf, string? caminhoXml)
        {
            Executar(@"
                UPDATE nota_fiscal
                   SET status = 'AUTORIZADA', numero = @numero, codigo_verificacao = @codigo,
                       url_consulta = @url, url_pdf = @pdf, caminho_xml = @xml,
                       mensagem = NULL, emitida_em = NOW()
                 WHERE id_nf = @id",
                ("@id", idNota), ("@numero", numero), ("@codigo", codigoVerificacao),
                ("@url", url), ("@pdf", urlPdf), ("@xml", caminhoXml));
        }

        public void MarcarCancelada(int idNota)
        {
            Executar("UPDATE nota_fiscal SET status = 'CANCELADA', cancelar = 0, mensagem = NULL WHERE id_nf = @id",
                ("@id", idNota));
        }

        /// <summary>A prefeitura recusou: a nota segue autorizada e o motivo fica a vista.</summary>
        public void RecusarCancelamento(int idNota, string mensagem)
        {
            Executar("UPDATE nota_fiscal SET cancelar = 0, mensagem = @mensagem WHERE id_nf = @id",
                ("@id", idNota), ("@mensagem", Cortar("Cancelamento recusado: " + mensagem)));
        }

        public void Descartar(int idNota, string mensagem)
        {
            Executar("UPDATE nota_fiscal SET status = 'DESCARTADA', mensagem = @mensagem WHERE id_nf = @id",
                ("@id", idNota), ("@mensagem", Cortar(mensagem)));
        }

        private int Executar(string sql, params (string Nome, object? Valor)[] parametros)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(sql, conexao);
            foreach (var (nome, valor) in parametros)
                comando.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
            return comando.ExecuteNonQuery();
        }

        private static string Cortar(string texto) => texto.Length <= 1000 ? texto : texto[..1000];

        private static NotaFiscal Ler(MySqlDataReader leitor)
        {
            string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

            return new NotaFiscal
            {
                IdNota = leitor.GetInt32("id_nf"),
                IdVenda = leitor.GetInt32("id_vend_fk"),
                Status = leitor.GetString("status"),
                Ambiente = Texto("ambiente"),
                Referencia = Texto("referencia"),
                SerieDps = DAOHelper.IsNull(leitor, "serie_dps") ? null : leitor.GetInt32("serie_dps"),
                NumeroDps = DAOHelper.IsNull(leitor, "numero_dps") ? null : leitor.GetInt64("numero_dps"),
                Valor = DAOHelper.IsNull(leitor, "valor") ? null : leitor.GetDecimal("valor"),
                Numero = Texto("numero"),
                CodigoVerificacao = Texto("codigo_verificacao"),
                UrlConsulta = Texto("url_consulta"),
                UrlPdf = Texto("url_pdf"),
                CaminhoXml = Texto("caminho_xml"),
                Mensagem = Texto("mensagem"),
                Tentativas = leitor.GetInt32("tentativas"),
                Cancelar = leitor.GetBoolean("cancelar"),
                JustificativaCancelamento = Texto("justificativa_cancelamento"),
                CriadoEm = leitor.GetDateTime("criado_em"),
                EmitidaEm = DAOHelper.GetDateTime(leitor, "emitida_em")
            };
        }
    }
}
