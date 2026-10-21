using LucasAguiar.Data;
using LucasAguiar.Models;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Leva a fila de nota_fiscal ate a Focus NFe e traz o resultado.
    ///
    /// A cada ciclo: envia as pendentes, consulta as que estao em
    /// processamento e cancela as marcadas para cancelar. Falha de rede ou
    /// da Focus so adia a nota; recusa da prefeitura vira ERRO, que espera
    /// alguem corrigir o cadastro e reenviar pela tela.
    /// </summary>
    public class EmissorNotasFiscais : BackgroundService
    {
        private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(20);
        private const int PorCiclo = 10;

        /// <summary>Horario de Brasilia. O container roda em UTC e o Brasil nao tem mais horario de verao.</summary>
        private static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

        private readonly IServiceScopeFactory _escopos;
        private readonly ILogger<EmissorNotasFiscais> _log;

        public EmissorNotasFiscais(IServiceScopeFactory escopos, ILogger<EmissorNotasFiscais> log)
        {
            _escopos = escopos;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await CicloAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Banco fora do ar, tabela ainda nao criada... tenta no proximo ciclo.
                    _log.LogWarning(ex, "Emissor de notas fiscais: ciclo interrompido.");
                }

                try { await Task.Delay(Intervalo, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task CicloAsync(CancellationToken ct)
        {
            using var escopo = _escopos.CreateScope();
            var notas = escopo.ServiceProvider.GetRequiredService<NotaFiscalDAO>();
            var vendas = escopo.ServiceProvider.GetRequiredService<VendaDAO>();
            var clientes = escopo.ServiceProvider.GetRequiredService<ClienteDAO>();
            var focus = escopo.ServiceProvider.GetRequiredService<FocusNfse>();

            var config = notas.BuscarConfig();

            // Desligar a emissao para a fila de envio, mas o que ja saiu
            // continua sendo acompanhado e pode ser cancelado.
            if (config.Ativo && focus.Token(config.Ambiente) != null)
            {
                foreach (var nota in notas.ListarParaEnviar(PorCiclo))
                    await EnviarAsync(nota, config, notas, vendas, clientes, focus, ct);
            }

            foreach (var nota in notas.ListarProcessando(PorCiclo))
                await ConsultarAsync(nota, notas, focus, ct);

            foreach (var nota in notas.ListarParaCancelar(PorCiclo))
                await CancelarAsync(nota, notas, focus, ct);
        }

        private async Task EnviarAsync(
            NotaFiscal nota, ConfigFiscal config, NotaFiscalDAO notas,
            VendaDAO vendas, ClienteDAO clientes, FocusNfse focus, CancellationToken ct)
        {
            if (!notas.ReservarParaEnvio(nota.IdNota)) return;

            try
            {
                var venda = vendas.BuscarPorId(nota.IdVenda);
                if (venda == null || venda.Status == StatusVenda.Cancelada)
                {
                    notas.Descartar(nota.IdNota, "Venda cancelada antes do envio.");
                    return;
                }

                var valores = RegrasNfse.Calcular(venda);
                if (valores.Liquido <= 0)
                {
                    notas.Descartar(nota.IdNota, "Venda sem serviço a tributar.");
                    return;
                }

                var erroCadastro = ConferirConfig(config);
                if (erroCadastro != null)
                {
                    notas.MarcarErro(nota.IdNota, erroCadastro);
                    return;
                }

                var cliente = venda.IdCliente is > 0 ? clientes.BuscarPorId(venda.IdCliente.Value) : null;
                var (serie, numero) = notas.ReservarNumeroDps();
                var agora = DateTimeOffset.UtcNow.ToOffset(FusoBrasilia);
                var dps = FocusNfse.MontarDps(config, venda, cliente, serie, numero, agora);

                // A Focus nao aceita repetir a referencia: cada tentativa leva a sua.
                var referencia = $"venda{venda.IdVenda}-nota{nota.IdNota}-t{nota.Tentativas + 1}";

                var retorno = await focus.EmitirAsync(config.Ambiente, referencia, dps, ct);

                if (retorno.Codigo is >= 200 and < 300)
                {
                    notas.MarcarProcessando(nota.IdNota, referencia, config.Ambiente, serie, numero, valores.Liquido);
                }
                else if (retorno.Codigo is 400 or 422)
                {
                    notas.MarcarErro(nota.IdNota, FocusNfse.MensagemDeErro(retorno));
                }
                else
                {
                    // 401 (token errado), 429, 5xx: nao e culpa da nota.
                    notas.Adiar(nota.IdNota, FocusNfse.MensagemDeErro(retorno), Espera(nota.Tentativas));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                notas.Adiar(nota.IdNota, "Sem resposta da Focus NFe: " + ex.Message, Espera(nota.Tentativas));
            }
        }

        private async Task ConsultarAsync(NotaFiscal nota, NotaFiscalDAO notas, FocusNfse focus, CancellationToken ct)
        {
            if (nota.Referencia == null || nota.Ambiente == null) return;

            try
            {
                var retorno = await focus.ConsultarAsync(nota.Ambiente, nota.Referencia, ct);

                switch (retorno.Status)
                {
                    case "autorizado":
                        notas.MarcarAutorizada(nota.IdNota,
                            retorno.Campo("numero"), retorno.Campo("codigo_verificacao"),
                            retorno.Campo("url"), retorno.Campo("url_danfse"),
                            retorno.Campo("caminho_xml_nota_fiscal"));
                        break;

                    case "erro_autorizacao":
                        notas.MarcarErro(nota.IdNota, FocusNfse.MensagemDeErro(retorno));
                        break;

                    case "cancelado":
                        notas.MarcarCancelada(nota.IdNota);
                        break;

                    // processando_autorizacao: segue na proxima volta.
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogInformation("Consulta da nota {Id} sem resposta: {Erro}", nota.IdNota, ex.Message);
            }
        }

        private async Task CancelarAsync(NotaFiscal nota, NotaFiscalDAO notas, FocusNfse focus, CancellationToken ct)
        {
            if (nota.Referencia == null || nota.Ambiente == null) return;

            try
            {
                var retorno = await focus.CancelarAsync(nota.Ambiente, nota.Referencia,
                    nota.JustificativaCancelamento ?? NotaFiscalDAO.Justificativa("Cancelamento da venda"), ct);

                if (retorno.Status == "cancelado")
                    notas.MarcarCancelada(nota.IdNota);
                else if (retorno.Status == "erro_cancelamento" || retorno.Codigo is 400 or 404 or 422)
                    notas.RecusarCancelamento(nota.IdNota, FocusNfse.MensagemDeErro(retorno));
                // Outros codigos (401, 5xx): o pedido continua marcado e volta no proximo ciclo.
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogInformation("Cancelamento da nota {Id} sem resposta: {Erro}", nota.IdNota, ex.Message);
            }
        }

        /// <summary>1, 2, 4, 8... minutos, no maximo uma hora.</summary>
        private static TimeSpan Espera(int tentativas) =>
            TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(tentativas, 6))));

        /// <summary>O minimo para a DPS nao voltar recusada por falta de cadastro.</summary>
        public static string? ConferirConfig(ConfigFiscal config)
        {
            if (!Configs.Documentos.CnpjValido(config.Cnpj))
                return "CNPJ do prestador não configurado. Preencha em Notas fiscais > Configuração.";
            if (Configs.Documentos.SoDigitos(config.CodigoMunicipio).Length != 7)
                return "Código IBGE do município não configurado. Preencha em Notas fiscais > Configuração.";
            if (Configs.Documentos.SoDigitos(config.CodigoTributacaoNacional).Length != 6)
                return "Código de tributação nacional deve ter 6 dígitos.";
            return null;
        }
    }
}
