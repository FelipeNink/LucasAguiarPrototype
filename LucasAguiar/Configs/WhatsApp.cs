using System.Globalization;
using System.Text;
using LucasAguiar.Models;

namespace LucasAguiar.Configs
{
    /// <summary>
    /// Link "clique para conversar" do WhatsApp (wa.me) com a mensagem
    /// pronta. Sem API nem bot: abre o WhatsApp de quem clicou com o texto
    /// no campo, e a pessoa confere e envia. Por isso nao depende de conta
    /// paga nem de servidor, e nada sai sem alguem apertar enviar.
    /// </summary>
    public static class WhatsApp
    {
        public const string NomeBarbearia = "Barbearia Lucas Aguiar";

        private static readonly CultureInfo Cultura = new("pt-BR");

        /// <summary>Null quando o telefone nao serve para o WhatsApp.</summary>
        public static string? Link(string? telefone, string mensagem)
        {
            var numero = Numero(telefone);
            return numero == null ? null : $"https://wa.me/{numero}?text={Uri.EscapeDataString(mensagem)}";
        }

        /// <summary>
        /// 55 + DDD + numero, como o wa.me pede. Aceita o telefone como foi
        /// cadastrado: com mascara, com 0 na frente do DDD ou ja com o 55.
        /// </summary>
        public static string? Numero(string? telefone)
        {
            var digitos = Documentos.SoDigitos(telefone).TrimStart('0');
            if (digitos.Length is 12 or 13 && digitos.StartsWith("55")) digitos = digitos[2..];
            return Documentos.TelefoneValido(digitos) ? "55" + digitos : null;
        }

        // ---------- mensagens ----------

        /// <summary>Serve de confirmacao logo depois de marcar e de lembrete na vespera.</summary>
        public static string ConfirmacaoAgendamento(string? cliente, DateTime quando, string? servico, string? profissional)
        {
            var oque = string.IsNullOrWhiteSpace(servico) ? "seu horário" : servico.Trim();
            var com = string.IsNullOrWhiteSpace(profissional) ? "" : $" com {PrimeiroNome(profissional)}";

            return $"Olá, {PrimeiroNome(cliente)}! Passando para confirmar: {oque}{com} " +
                   $"{Quando(quando)}, na {NomeBarbearia}. " +
                   "Se precisar remarcar, é só responder por aqui.";
        }

        /// <summary>Lembrete da vespera com o link onde o cliente confirma ou cancela.</summary>
        public static string Lembrete(string? cliente, DateTime quando, string? servico, string? profissional, string link)
        {
            var oque = string.IsNullOrWhiteSpace(servico) ? "seu horário" : servico.Trim();
            var com = string.IsNullOrWhiteSpace(profissional) ? "" : $" com {PrimeiroNome(profissional)}";

            return $"Olá, {PrimeiroNome(cliente)}! Lembrete da {NomeBarbearia}: {oque}{com} " +
                   $"{Quando(quando)}. Confirme ou cancele por aqui: {link}";
        }

        /// <summary>Depois do atendimento, com o link de dar a nota.</summary>
        public static string PedidoAvaliacao(string? cliente, string? profissional, string link)
        {
            var com = string.IsNullOrWhiteSpace(profissional) ? "" : $" com {PrimeiroNome(profissional)}";

            return $"Olá, {PrimeiroNome(cliente)}! Obrigado pela visita na {NomeBarbearia}. " +
                   $"Como foi seu atendimento{com}? Leva menos de um minuto: {link}";
        }

        public static string RemarcarFalta(string? cliente, DateTime quando)
            => $"Olá, {PrimeiroNome(cliente)}! Sentimos sua falta no horário de " +
               $"{quando.ToString("dd/MM 'às' HH:mm", Cultura)} aqui na {NomeBarbearia}. " +
               "Quer remarcar? É só responder com o melhor dia para você.";

        /// <summary>Null em diasSemVir e o cadastro que nunca virou cliente.</summary>
        public static string ClienteInativo(string? cliente, int? diasSemVir)
            => diasSemVir == null
                ? $"Olá, {PrimeiroNome(cliente)}! Aqui é da {NomeBarbearia}. " +
                  "Que tal marcar seu primeiro horário com a gente? É só responder por aqui."
                : $"Olá, {PrimeiroNome(cliente)}! Faz {diasSemVir} dias que você não passa na " +
                  $"{NomeBarbearia}. Bora dar um trato no visual? Responda com o melhor dia e horário.";

        public static string Aniversario(string? cliente)
            => $"Feliz aniversário, {PrimeiroNome(cliente)}! 🎉 A {NomeBarbearia} deseja um ótimo dia " +
               "e um ano incrível. Vem comemorar com um trato no visual!";

        public static string ComprovanteVenda(Venda venda)
        {
            var texto = new StringBuilder();
            texto.Append($"Olá, {PrimeiroNome(venda.NomeCliente)}! Segue o comprovante da sua visita na {NomeBarbearia}.\n\n");
            texto.Append($"Venda #{venda.IdVenda} — {venda.DataVenda.ToString("dd/MM/yyyy 'às' HH:mm", Cultura)}\n");

            foreach (var item in venda.Itens)
            {
                var quantidade = item.Quantidade > 1 ? $"{item.Quantidade}× " : "";
                var valor = item.CobertoPlano ? "plano" : item.ValorTotal.ToString("C", Cultura);
                texto.Append($"• {quantidade}{item.Descricao} — {valor}\n");
            }

            if (venda.Desconto > 0)
                texto.Append($"Desconto: − {venda.Desconto.ToString("C", Cultura)}\n");

            texto.Append($"Total: {venda.ValorVenda.ToString("C", Cultura)}\n");
            texto.Append($"Pagamento: {FormaPagamento.Rotulo(venda.FormaPagamentoVenda)}\n\n");
            texto.Append("Obrigado pela preferência!");
            return texto.ToString();
        }

        // ---------- apoio ----------

        /// <summary>"João" de "joão da silva"; "tudo bem" quando nao ha nome.</summary>
        public static string PrimeiroNome(string? nome)
        {
            var primeiro = (nome ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(primeiro)) return "tudo bem";
            return char.ToUpper(primeiro[0], Cultura) + primeiro[1..].ToLower(Cultura);
        }

        /// <summary>"hoje às 14:30", "amanhã às 09:00" ou "sexta-feira, 24/10 às 15:00".</summary>
        public static string Quando(DateTime quando, DateTime? hoje = null)
        {
            var dia = (hoje ?? DateTime.Today).Date;
            var hora = quando.ToString("HH:mm", Cultura);

            if (quando.Date == dia) return $"hoje às {hora}";
            if (quando.Date == dia.AddDays(1)) return $"amanhã às {hora}";
            return $"{Cultura.DateTimeFormat.GetDayName(quando.DayOfWeek)}, {quando.ToString("dd/MM", Cultura)} às {hora}";
        }
    }
}
