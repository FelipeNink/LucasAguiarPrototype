using System.Net.Mail;

namespace LucasAguiar.Configs
{
    /// <summary>
    /// Formato e conferencia de telefone, CPF, CNPJ, CEP e e-mail.
    ///
    /// A mascara do navegador (ui.js, data-mascara) so ajuda quem digita;
    /// quem decide se o valor vale e esta classe, no servidor. Por isso
    /// cada Formatar aceita o valor com ou sem pontuacao: colar "12345678"
    /// ou "12345-678" grava o mesmo "12345-678".
    /// </summary>
    public static class Documentos
    {
        public static readonly IReadOnlyList<string> Ufs = new[]
        {
            "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
            "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
        };

        public static string SoDigitos(string? valor) =>
            new string((valor ?? "").Where(char.IsDigit).ToArray());

        // ---------- telefone ----------

        /// <summary>Fixo com DDD (10 digitos) ou celular com DDD (11, comecando em 9).</summary>
        public static bool TelefoneValido(string? valor)
        {
            var d = SoDigitos(valor);
            if (d.Length == 10) return d[0] != '0';
            if (d.Length == 11) return d[0] != '0' && d[2] == '9';
            return false;
        }

        public static string FormatarTelefone(string? valor)
        {
            var d = SoDigitos(valor);
            return d.Length switch
            {
                10 => $"({d[..2]}) {d[2..6]}-{d[6..]}",
                11 => $"({d[..2]}) {d[2..7]}-{d[7..]}",
                _ => (valor ?? "").Trim()
            };
        }

        // ---------- CPF ----------

        public static bool CpfValido(string? valor)
        {
            var d = SoDigitos(valor);
            if (d.Length != 11 || d.Distinct().Count() == 1) return false;

            return DigitoCpf(d[..9]) == d[9] - '0'
                && DigitoCpf(d[..10]) == d[10] - '0';
        }

        private static int DigitoCpf(string base_)
        {
            var peso = base_.Length + 1;
            var soma = base_.Sum(c => (c - '0') * peso--);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        public static string FormatarCpf(string? valor)
        {
            var d = SoDigitos(valor);
            return d.Length == 11
                ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}"
                : (valor ?? "").Trim();
        }

        // ---------- CNPJ ----------

        public static bool CnpjValido(string? valor)
        {
            var d = SoDigitos(valor);
            if (d.Length != 14 || d.Distinct().Count() == 1) return false;

            return DigitoCnpj(d[..12]) == d[12] - '0'
                && DigitoCnpj(d[..13]) == d[13] - '0';
        }

        private static int DigitoCnpj(string base_)
        {
            // Pesos de 2 a 9 da direita para a esquerda, recomecando no 2.
            var soma = 0;
            for (int i = base_.Length - 1, peso = 2; i >= 0; i--, peso = peso == 9 ? 2 : peso + 1)
                soma += (base_[i] - '0') * peso;

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        public static string FormatarCnpj(string? valor)
        {
            var d = SoDigitos(valor);
            return d.Length == 14
                ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}"
                : (valor ?? "").Trim();
        }

        // ---------- CEP ----------

        public static bool CepValido(string? valor) => SoDigitos(valor).Length == 8;

        public static string FormatarCep(string? valor)
        {
            var d = SoDigitos(valor);
            return d.Length == 8 ? $"{d[..5]}-{d[5..]}" : (valor ?? "").Trim();
        }

        // ---------- e-mail ----------

        /// <summary>
        /// Usuario, arroba e dominio com ponto. MailAddress sozinho aceita
        /// "a@b" e nomes com espaco, que ninguem digita de proposito.
        /// </summary>
        public static bool EmailValido(string? valor)
        {
            var texto = (valor ?? "").Trim();
            if (texto.Length == 0 || texto.Contains(' ')) return false;

            try
            {
                var endereco = new MailAddress(texto);
                var dominio = endereco.Host;
                return endereco.Address == texto
                    && dominio.Contains('.')
                    && !dominio.StartsWith('.') && !dominio.EndsWith('.');
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // ---------- mensagens ----------
        // Vazio nunca e erro aqui: obrigatoriedade e outra regra, de cada tela.

        public static string? ErroTelefone(string? v) =>
            string.IsNullOrWhiteSpace(v) || TelefoneValido(v) ? null : "Telefone incorreto. Use DDD + número.";

        public static string? ErroCpf(string? v) =>
            string.IsNullOrWhiteSpace(v) || CpfValido(v) ? null : "CPF incorreto.";

        public static string? ErroCnpj(string? v) =>
            string.IsNullOrWhiteSpace(v) || CnpjValido(v) ? null : "CNPJ incorreto.";

        public static string? ErroCep(string? v) =>
            string.IsNullOrWhiteSpace(v) || CepValido(v) ? null : "CEP incorreto. São 8 números.";

        public static string? ErroEmail(string? v) =>
            string.IsNullOrWhiteSpace(v) || EmailValido(v) ? null : "E-mail incorreto.";
    }
}
