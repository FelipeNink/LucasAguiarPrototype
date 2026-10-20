using System.Text.Json;
using LucasAguiar.Configs;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Endereco a partir do CEP, pelo ViaCEP (gratuito, sem chave).
    ///
    /// A consulta sai do servidor, nao do navegador: o componente roda no
    /// circuito do Blazor Server e preenche os campos direto no modelo.
    /// Qualquer falha (fora do ar, CEP inexistente, tempo esgotado) volta
    /// nulo, e a tela segue com o preenchimento manual.
    /// </summary>
    public class BuscaCep
    {
        public record Endereco(string Rua, string Bairro, string Cidade, string Uf);

        private readonly HttpClient _http;
        private readonly ILogger<BuscaCep> _log;

        public BuscaCep(HttpClient http, ILogger<BuscaCep> log)
        {
            _http = http;
            _http.Timeout = TimeSpan.FromSeconds(5);
            _log = log;
        }

        public async Task<Endereco?> BuscarAsync(string? cep)
        {
            var digitos = Documentos.SoDigitos(cep);
            if (digitos.Length != 8) return null;

            try
            {
                using var resposta = await _http.GetAsync($"https://viacep.com.br/ws/{digitos}/json/");
                if (!resposta.IsSuccessStatusCode) return null;

                using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
                var raiz = json.RootElement;

                // CEP com formato certo mas inexistente vem como {"erro": true}.
                if (raiz.TryGetProperty("erro", out _)) return null;

                string Texto(string nome) =>
                    raiz.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? "" : "";

                return new Endereco(Texto("logradouro"), Texto("bairro"), Texto("localidade"), Texto("uf"));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Busca do CEP {Cep} falhou.", digitos);
                return null;
            }
        }
    }
}
