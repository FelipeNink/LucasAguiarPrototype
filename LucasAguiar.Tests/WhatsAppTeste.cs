using LucasAguiar.Configs;
using LucasAguiar.Models;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// O numero que vai no wa.me. Errar aqui abre a conversa com outra
    /// pessoa, ou com ninguem.
    /// </summary>
    public class WhatsAppTeste
    {
        [Theory]
        [InlineData("(48) 99999-1234", "5548999991234")]
        [InlineData("48999991234", "5548999991234")]
        [InlineData("048 99999-1234", "5548999991234")]
        [InlineData("+55 (48) 99999-1234", "5548999991234")]
        [InlineData("(48) 3333-1001", "554833331001")]
        public void Telefone_cadastrado_vira_numero_do_wa_me(string telefone, string esperado)
            => Assert.Equal(esperado, WhatsApp.Numero(telefone));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("99999-1234")]        // sem DDD
        [InlineData("(48) 89999-1234")]   // 11 digitos sem o 9 de celular
        public void Telefone_que_nao_serve_nao_gera_link(string? telefone)
        {
            Assert.Null(WhatsApp.Numero(telefone));
            Assert.Null(WhatsApp.Link(telefone, "oi"));
        }

        [Fact]
        public void Link_leva_a_mensagem_codificada()
        {
            var link = WhatsApp.Link("(48) 99999-1234", "Olá, João! 14:30 & cia");
            Assert.Equal("https://wa.me/5548999991234?text=Ol%C3%A1%2C%20Jo%C3%A3o%21%2014%3A30%20%26%20cia", link);
        }

        [Theory]
        [InlineData("joão da silva", "João")]
        [InlineData("  MARCOS  VINICIUS ", "Marcos")]
        [InlineData("", "tudo bem")]
        [InlineData(null, "tudo bem")]
        public void Primeiro_nome(string? nome, string esperado)
            => Assert.Equal(esperado, WhatsApp.PrimeiroNome(nome));

        [Fact]
        public void Quando_diz_hoje_amanha_ou_o_dia_da_semana()
        {
            var hoje = new DateTime(2026, 10, 19); // segunda-feira
            Assert.Equal("hoje às 14:30", WhatsApp.Quando(hoje.AddHours(14.5), hoje));
            Assert.Equal("amanhã às 09:00", WhatsApp.Quando(hoje.AddDays(1).AddHours(9), hoje));
            Assert.Equal("sexta-feira, 23/10 às 15:00", WhatsApp.Quando(hoje.AddDays(4).AddHours(15), hoje));
        }

        [Fact]
        public void Comprovante_lista_itens_desconto_total_e_pagamento()
        {
            var venda = new Venda
            {
                IdVenda = 12,
                DataVenda = new DateTime(2026, 10, 19, 14, 30, 0),
                NomeCliente = "joão silva",
                Desconto = 5,
                ValorVenda = 129,
                FormaPagamentoVenda = "PIX",
                Itens =
                {
                    new VendaItem { Descricao = "Corte degradê", Quantidade = 1, ValorUnitario = 50 },
                    new VendaItem { Descricao = "Pomada", Quantidade = 2, ValorUnitario = 42 },
                    new VendaItem { Descricao = "Barba", Quantidade = 1, ValorUnitario = 0, CobertoPlano = true }
                }
            };

            var texto = WhatsApp.ComprovanteVenda(venda);

            Assert.StartsWith("Olá, João!", texto);
            Assert.Contains("Venda #12 — 19/10/2026 às 14:30", texto);
            Assert.Contains("• Corte degradê — R$ 50,00", texto);
            Assert.Contains("• 2× Pomada — R$ 84,00", texto);
            Assert.Contains("• Barba — plano", texto);
            Assert.Contains("Desconto: − R$ 5,00", texto);
            Assert.Contains("Total: R$ 129,00", texto);
        }
    }
}
