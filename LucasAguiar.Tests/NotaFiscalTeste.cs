using System.Text.Json.Nodes;
using LucasAguiar.Data;
using LucasAguiar.Models;
using LucasAguiar.Services;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// O que sai da venda para a NFS-e. Nao toca rede nem banco: o envio
    /// em si so da para conferir no ambiente de homologacao da Focus.
    /// </summary>
    public class NotaFiscalTeste
    {
        private static VendaItem Item(string tipo, decimal valor, int qtd = 1, decimal desconto = 0, bool coberto = false) =>
            new()
            {
                TipoItem = tipo,
                Descricao = tipo == TipoItem.Produto ? "Pomada" : tipo == TipoItem.Plano ? "Plano Mensal" : "Corte",
                ValorUnitario = valor,
                Quantidade = qtd,
                Desconto = desconto,
                CobertoPlano = coberto
            };

        private static ConfigFiscal Config() => new()
        {
            Ativo = true,
            Cnpj = "11.222.333/0001-81",
            CodigoMunicipio = "1100205",
            OpcaoSimples = 3,
            RegimeSimples = 1
        };

        private static readonly DateTimeOffset Agora =
            new(2026, 10, 21, 14, 30, 0, TimeSpan.FromHours(-3));

        [Fact]
        public void Produto_fica_fora_e_o_desconto_geral_e_rateado()
        {
            var venda = new Venda
            {
                Itens = { Item(TipoItem.Servico, 50), Item(TipoItem.Produto, 30) },
                Desconto = 8
            };

            var valores = RegrasNfse.Calcular(venda);

            // 8 de desconto sobre 80: o servico pesa 50/80, leva 5.
            Assert.Equal(50m, valores.Bruto);
            Assert.Equal(5m, valores.Desconto);
            Assert.Equal(45m, valores.Liquido);
        }

        [Fact]
        public void Desconto_da_linha_entra_no_desconto_da_nota()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Servico, 40, qtd: 2, desconto: 10) } };

            var valores = RegrasNfse.Calcular(venda);

            Assert.Equal(80m, valores.Bruto);
            Assert.Equal(10m, valores.Desconto);
            Assert.Equal(70m, valores.Liquido);
        }

        [Fact]
        public void Servico_coberto_por_plano_nao_gera_nota()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Servico, 0, coberto: true) } };

            Assert.Equal(0m, RegrasNfse.Calcular(venda).Liquido);
            Assert.Equal("", RegrasNfse.Descrever(venda));
        }

        [Fact]
        public void Venda_de_plano_e_tributada_pelo_valor_pago()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Plano, 120) } };

            Assert.Equal(120m, RegrasNfse.Calcular(venda).Liquido);
            Assert.Equal("Plano Mensal", RegrasNfse.Descrever(venda));
        }

        [Fact]
        public void Descricao_mostra_a_quantidade_e_ignora_produto()
        {
            var venda = new Venda
            {
                Itens = { Item(TipoItem.Servico, 40, qtd: 2), Item(TipoItem.Produto, 30) }
            };

            Assert.Equal("Corte (2x)", RegrasNfse.Descrever(venda));
        }

        [Fact]
        public void Dps_sem_cpf_sai_para_consumidor_nao_identificado()
        {
            var venda = new Venda { DataVenda = new DateTime(2026, 10, 21), Itens = { Item(TipoItem.Servico, 50) } };
            var cliente = new Cliente { NomeCliente = "João", CPF = null };

            var dps = FocusNfse.MontarDps(Config(), venda, cliente, 1, 42, Agora);

            Assert.False(dps.ContainsKey("cpf_tomador"));
            Assert.False(dps.ContainsKey("razao_social_tomador"));
            Assert.Equal("11222333000181", dps["cnpj_prestador"]!.GetValue<string>());
            Assert.Equal(1100205, dps["codigo_municipio_emissora"]!.GetValue<int>());
            Assert.Equal("1100205", dps["codigo_municipio_prestacao"]!.GetValue<string>());
            Assert.Equal("060101", dps["codigo_tributacao_nacional_iss"]!.GetValue<string>());
            Assert.Equal(42L, dps["numero_dps"]!.GetValue<long>());
            Assert.Equal("2026-10-21T14:30:00-03:00", dps["data_emissao"]!.GetValue<string>());
            Assert.Equal("2026-10-21", dps["data_competencia"]!.GetValue<string>());
            Assert.Equal(50m, dps["valor_servico"]!.GetValue<decimal>());
            Assert.False(dps.ContainsKey("desconto_incondicionado"));
        }

        [Fact]
        public void Dps_com_cpf_valido_identifica_o_tomador()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Servico, 50) }, Desconto = 5 };
            var cliente = new Cliente { NomeCliente = " Maria ", CPF = "529.982.247-25", Email = "maria@exemplo.com" };

            var dps = FocusNfse.MontarDps(Config(), venda, cliente, 1, 1, Agora);

            Assert.Equal("52998224725", dps["cpf_tomador"]!.GetValue<string>());
            Assert.Equal("Maria", dps["razao_social_tomador"]!.GetValue<string>());
            Assert.Equal("maria@exemplo.com", dps["email_tomador"]!.GetValue<string>());
            Assert.Equal(5m, dps["desconto_incondicionado"]!.GetValue<decimal>());
        }

        [Fact]
        public void Regime_do_simples_so_vai_para_me_epp()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Servico, 50) } };
            var mei = Config();
            mei.OpcaoSimples = 2;

            Assert.True(FocusNfse.MontarDps(Config(), venda, null, 1, 1, Agora).ContainsKey("regime_tributario_simples_nacional"));
            Assert.False(FocusNfse.MontarDps(mei, venda, null, 1, 1, Agora).ContainsKey("regime_tributario_simples_nacional"));
        }

        [Fact]
        public void Grupo_ibs_cbs_so_vai_com_cst_e_classificacao()
        {
            var venda = new Venda { Itens = { Item(TipoItem.Servico, 50) } };
            var config = Config();
            config.IbsCbsCst = "000";

            Assert.False(FocusNfse.MontarDps(config, venda, null, 1, 1, Agora).ContainsKey("ibs_cbs_situacao_tributaria"));

            config.IbsCbsClassificacao = "000001";
            var dps = FocusNfse.MontarDps(config, venda, null, 1, 1, Agora);

            Assert.Equal("000", dps["ibs_cbs_situacao_tributaria"]!.GetValue<string>());
            Assert.Equal(1, dps["consumidor_final"]!.GetValue<int>());
        }

        [Fact]
        public void Config_incompleta_vira_erro_antes_de_gastar_numero()
        {
            Assert.Null(EmissorNotasFiscais.ConferirConfig(Config()));

            var semCnpj = Config();
            semCnpj.Cnpj = null;
            Assert.Contains("CNPJ", EmissorNotasFiscais.ConferirConfig(semCnpj));

            var semMunicipio = Config();
            semMunicipio.CodigoMunicipio = "11002";
            Assert.Contains("IBGE", EmissorNotasFiscais.ConferirConfig(semMunicipio));
        }

        [Fact]
        public void Justificativa_respeita_o_tamanho_do_padrao_nacional()
        {
            Assert.InRange(NotaFiscalDAO.Justificativa("erro").Length, 15, 255);
            Assert.InRange(NotaFiscalDAO.Justificativa(new string('x', 400)).Length, 15, 255);
            Assert.Equal("Emissão em duplicidade", NotaFiscalDAO.Justificativa("  Emissão em duplicidade "));
        }

        [Fact]
        public void Mensagem_de_erro_le_a_lista_do_ambiente_nacional_e_a_da_pre_validacao()
        {
            var nacional = new FocusNfse.Retorno(200, JsonNode.Parse(@"{
                ""status"": ""erro_autorizacao"",
                ""erros"": [{ ""codigo"": ""E0014"", ""mensagem"": ""DPS repetida"", ""correcao"": null }]
            }")!.AsObject(), "");
            var previa = new FocusNfse.Retorno(422, JsonNode.Parse(
                @"{ ""codigo"": ""erro_validacao"", ""mensagem"": ""Já existe um DPS com esta referência."" }")!.AsObject(), "");

            Assert.Equal("E0014 DPS repetida", FocusNfse.MensagemDeErro(nacional));
            Assert.Equal("Já existe um DPS com esta referência.", FocusNfse.MensagemDeErro(previa));
            Assert.StartsWith("HTTP 502", FocusNfse.MensagemDeErro(new FocusNfse.Retorno(502, null, "Bad Gateway")));
        }
    }
}
