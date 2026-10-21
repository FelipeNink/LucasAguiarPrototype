using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LucasAguiar.Configs;
using LucasAguiar.Models;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Cliente da NFS-e Nacional da Focus NFe (/v2/nfsen).
    ///
    /// O envio e assincrono do lado deles: o POST so diz se a DPS foi
    /// aceita para a fila; a autorizacao vem depois, pela consulta. Quem
    /// cuida desse vai e volta e o EmissorNotasFiscais.
    ///
    /// Autenticacao e Basic com o token como usuario e senha vazia. Um
    /// token por ambiente, lidos de Fiscal:TokenHomologacao e
    /// Fiscal:TokenProducao (no Railway, Fiscal__TokenHomologacao...).
    /// </summary>
    public class FocusNfse
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuracao;

        public FocusNfse(HttpClient http, IConfiguration configuracao)
        {
            _http = http;
            _http.Timeout = TimeSpan.FromSeconds(30);
            _configuracao = configuracao;
        }

        public string? Token(string? ambiente)
        {
            var chave = ambiente == AmbienteFiscal.Producao ? "Fiscal:TokenProducao" : "Fiscal:TokenHomologacao";
            var token = _configuracao[chave];
            return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        private static string Base(string? ambiente) =>
            ambiente == AmbienteFiscal.Producao
                ? "https://api.focusnfe.com.br"
                : "https://homologacao.focusnfe.com.br";

        /// <summary>Resposta crua: codigo HTTP e o corpo, quando veio JSON.</summary>
        public record Retorno(int Codigo, JsonObject? Corpo, string Texto)
        {
            public string? Status => Corpo?["status"]?.GetValue<string>();

            public string? Campo(string nome) => Corpo?[nome]?.ToString();
        }

        public Task<Retorno> EmitirAsync(string ambiente, string referencia, JsonObject dps, CancellationToken ct) =>
            EnviarAsync(HttpMethod.Post, ambiente, $"/v2/nfsen?ref={Uri.EscapeDataString(referencia)}", dps, ct);

        public Task<Retorno> ConsultarAsync(string ambiente, string referencia, CancellationToken ct) =>
            EnviarAsync(HttpMethod.Get, ambiente, $"/v2/nfsen/{Uri.EscapeDataString(referencia)}", null, ct);

        public Task<Retorno> CancelarAsync(string ambiente, string referencia, string justificativa, CancellationToken ct) =>
            EnviarAsync(HttpMethod.Delete, ambiente, $"/v2/nfsen/{Uri.EscapeDataString(referencia)}",
                new JsonObject { ["justificativa"] = justificativa }, ct);

        private async Task<Retorno> EnviarAsync(
            HttpMethod metodo, string ambiente, string caminho, JsonObject? corpo, CancellationToken ct)
        {
            var token = Token(ambiente)
                ?? throw new InvalidOperationException(
                    $"Token da Focus NFe de {AmbienteFiscal.Rotulo(ambiente)} não configurado.");

            using var pedido = new HttpRequestMessage(metodo, Base(ambiente) + caminho);
            pedido.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(token + ":")));

            if (corpo != null)
                pedido.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");

            using var resposta = await _http.SendAsync(pedido, ct);
            var texto = await resposta.Content.ReadAsStringAsync(ct);

            JsonObject? json = null;
            try { json = JsonNode.Parse(texto) as JsonObject; }
            catch (JsonException) { }

            return new Retorno((int)resposta.StatusCode, json, texto);
        }

        /// <summary>
        /// Motivo legivel de uma recusa. A Focus manda "mensagem" no topo
        /// (pre-validacao) ou a lista "erros" (ambiente nacional).
        /// </summary>
        public static string MensagemDeErro(Retorno retorno)
        {
            if (retorno.Corpo?["erros"] is JsonArray erros && erros.Count > 0)
            {
                return string.Join(" | ", erros.Select(e =>
                {
                    var codigo = e?["codigo"]?.ToString();
                    var mensagem = e?["mensagem"]?.ToString();
                    var correcao = e?["correcao"]?.ToString();
                    return $"{codigo} {mensagem}{(string.IsNullOrWhiteSpace(correcao) ? "" : " — " + correcao)}".Trim();
                }));
            }

            var unica = retorno.Campo("mensagem");
            if (!string.IsNullOrWhiteSpace(unica)) return unica;

            return $"HTTP {retorno.Codigo}: {(retorno.Texto.Length > 300 ? retorno.Texto[..300] : retorno.Texto)}";
        }

        /// <summary>
        /// Monta a DPS da venda. Sem I/O, para dar para testar o que vai
        /// para a prefeitura sem rede nem banco.
        ///
        /// Tomador so entra com CPF valido: sem identificacao, a nota sai
        /// para consumidor nao identificado, como no balcao.
        /// </summary>
        public static JsonObject MontarDps(
            ConfigFiscal config, Venda venda, Cliente? cliente, int serie, long numero, DateTimeOffset agora)
        {
            var valores = RegrasNfse.Calcular(venda);
            var municipio = Documentos.SoDigitos(config.CodigoMunicipio);

            var dps = new JsonObject
            {
                ["data_emissao"] = agora.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                ["data_competencia"] = venda.DataVenda.ToString("yyyy-MM-dd"),
                ["serie_dps"] = serie,
                ["numero_dps"] = numero,
                ["emitente_dps"] = 1,
                ["codigo_municipio_emissora"] = int.TryParse(municipio, out var codigo) ? codigo : 0,
                ["cnpj_prestador"] = Documentos.SoDigitos(config.Cnpj),
                ["codigo_opcao_simples_nacional"] = config.OpcaoSimples,
                ["regime_especial_tributacao"] = config.RegimeEspecial,
                ["codigo_municipio_prestacao"] = municipio,
                ["codigo_tributacao_nacional_iss"] = Documentos.SoDigitos(config.CodigoTributacaoNacional),
                ["descricao_servico"] = RegrasNfse.Descrever(venda),
                ["valor_servico"] = valores.Bruto,
                ["tributacao_iss"] = 1,
                ["tipo_retencao_iss"] = 1
            };

            if (!string.IsNullOrWhiteSpace(config.InscricaoMunicipal))
                dps["inscricao_municipal_prestador"] = config.InscricaoMunicipal.Trim();

            // Regime de apuracao so existe para ME/EPP.
            if (config.OpcaoSimples == 3 && config.RegimeSimples.HasValue)
                dps["regime_tributario_simples_nacional"] = config.RegimeSimples.Value;

            if (valores.Desconto > 0)
                dps["desconto_incondicionado"] = valores.Desconto;

            if (!string.IsNullOrWhiteSpace(config.CodigoTributacaoMunicipal))
                dps["codigo_tributacao_municipal_iss"] = config.CodigoTributacaoMunicipal.Trim();

            if (!string.IsNullOrWhiteSpace(config.CodigoNbs))
                dps["codigo_nbs"] = Documentos.SoDigitos(config.CodigoNbs);

            if (config.AliquotaIss is > 0)
                dps["percentual_aliquota_relativa_municipio"] = config.AliquotaIss.Value;

            if (cliente != null && Documentos.CpfValido(cliente.CPF))
            {
                dps["cpf_tomador"] = Documentos.SoDigitos(cliente.CPF);
                if (!string.IsNullOrWhiteSpace(cliente.NomeCliente))
                    dps["razao_social_tomador"] = cliente.NomeCliente.Trim();
                if (!string.IsNullOrWhiteSpace(cliente.Email))
                    dps["email_tomador"] = cliente.Email.Trim();
            }

            // Reforma tributaria: o grupo IBS/CBS so vai quando o contador
            // definir CST e classificacao. Servico pessoal, consumidor final.
            if (!string.IsNullOrWhiteSpace(config.IbsCbsCst) && !string.IsNullOrWhiteSpace(config.IbsCbsClassificacao))
            {
                dps["finalidade_emissao"] = 0;
                dps["consumidor_final"] = 1;
                dps["indicador_destinatario"] = 0;
                dps["ibs_cbs_situacao_tributaria"] = config.IbsCbsCst.Trim();
                dps["ibs_cbs_classificacao_tributaria"] = config.IbsCbsClassificacao.Trim();
            }

            return dps;
        }
    }
}
