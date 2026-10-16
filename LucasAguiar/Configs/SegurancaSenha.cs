using System.Security.Cryptography;
using System.Text;

namespace LucasAguiar.Configs
{
    /// <summary>
    /// Senha e token de sessao.
    ///
    /// O sistema guardava a senha como um SHA-256 sem sal e de uma passada
    /// so -- uma lista de hashes vazada se quebra por tabela pronta. Agora
    /// a senha nova nasce em PBKDF2-SHA256 com sal proprio.
    ///
    /// Trocar tudo de uma vez invalidaria a senha de quem ja usa o sistema
    /// e ninguem mais entraria. Por isso a linha do usuario diz em que
    /// algoritmo a senha dele esta: o antigo continua sendo aceito, e o
    /// proprio login regrava a senha no formato novo na primeira entrada
    /// (ver <see cref="PrecisaRegravar"/>). Quando nao houver mais nenhuma
    /// linha em 'sha256', o ramo legado pode sair.
    /// </summary>
    public static class SegurancaSenha
    {
        public const string Atual = "pbkdf2";
        public const string Legado = "sha256";

        /// <summary>
        /// Custo do PBKDF2. Numero alto de proposito: conferir uma senha
        /// passa a custar ~100 ms, irrelevante para quem entra uma vez e
        /// caro para quem tenta milhoes de combinacoes.
        /// </summary>
        private const int Iteracoes = 210_000;
        private const int BytesSal = 16;
        private const int BytesChave = 32;

        public const int MinimoCaracteres = 8;

        // ---------- gerar e conferir ----------

        /// <summary>
        /// Formato gravado: <c>pbkdf2$iteracoes$sal$chave</c>, com sal e
        /// chave em base64. Guardar as iteracoes junto permite aumentar o
        /// custo depois sem invalidar as senhas de hoje.
        /// </summary>
        public static string Gerar(string senha)
        {
            var sal = RandomNumberGenerator.GetBytes(BytesSal);
            var chave = Derivar(senha, sal, Iteracoes);

            return string.Join('$',
                Atual, Iteracoes, Convert.ToBase64String(sal), Convert.ToBase64String(chave));
        }

        /// <summary>
        /// Confere a senha contra o que esta gravado. Comparacao em tempo
        /// constante nos dois ramos: um <c>==</c> de string vaza, pelo tempo
        /// de resposta, quantos caracteres do hash bateram.
        /// </summary>
        public static bool Confere(string? senha, string? gravado, string? algoritmo)
        {
            if (string.IsNullOrEmpty(senha) || string.IsNullOrWhiteSpace(gravado))
                return false;

            // Senha antiga: SHA-256 hex, sem sal.
            if (!string.Equals(algoritmo, Atual, StringComparison.OrdinalIgnoreCase))
            {
                var esperado = Encoding.ASCII.GetBytes(gravado.Trim().ToUpperInvariant());
                var calculado = Encoding.ASCII.GetBytes(Sha256Hex(senha));

                return esperado.Length == calculado.Length
                    && CryptographicOperations.FixedTimeEquals(esperado, calculado);
            }

            var partes = gravado.Split('$');
            if (partes.Length != 4) return false;
            if (!int.TryParse(partes[1], out var iteracoes) || iteracoes < 1) return false;

            byte[] sal, chave;
            try
            {
                sal = Convert.FromBase64String(partes[2]);
                chave = Convert.FromBase64String(partes[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var candidata = Derivar(senha, sal, iteracoes, chave.Length);
            return CryptographicOperations.FixedTimeEquals(chave, candidata);
        }

        /// <summary>
        /// true quando a senha gravada esta num formato antigo e deve ser
        /// regravada. O login chama isto depois de conferir com acerto: e o
        /// unico momento em que a senha em texto existe para ser rederivada.
        /// </summary>
        public static bool PrecisaRegravar(string? algoritmo) =>
            !string.Equals(algoritmo, Atual, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gasta o mesmo tempo de uma conferencia de verdade.
        ///
        /// Sem isto, "usuario inexistente" responde na hora e "senha
        /// errada" responde em 100 ms -- a diferenca basta para descobrir
        /// quais logins existem, mesmo com a mensagem de erro sendo a
        /// mesma nos dois casos.
        /// </summary>
        public static void GastarTempoDeConferencia()
        {
            var sal = RandomNumberGenerator.GetBytes(BytesSal);
            var chave = Derivar("nao-existe", sal, Iteracoes);
            CryptographicOperations.ZeroMemory(chave);
        }

        private static byte[] Derivar(string senha, byte[] sal, int iteracoes, int bytes = BytesChave) =>
            Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(senha), sal, iteracoes, HashAlgorithmName.SHA256, bytes);

        private static string Sha256Hex(string texto) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

        // ---------- politica ----------

        /// <summary>
        /// Devolve a mensagem do problema, ou null se a senha serve. Oito
        /// caracteres com uma letra e um numero: o suficiente para barrar
        /// "12345678" e "senha" sem obrigar a equipe a decorar simbolo.
        /// </summary>
        public static string? Criticar(string? senha)
        {
            if (string.IsNullOrWhiteSpace(senha))
                return "Informe a senha.";

            if (senha.Length < MinimoCaracteres)
                return $"A senha precisa de pelo menos {MinimoCaracteres} caracteres.";

            if (senha.Length > 128)
                return "A senha pode ter no máximo 128 caracteres.";

            if (!senha.Any(char.IsLetter) || !senha.Any(char.IsDigit))
                return "A senha precisa ter ao menos uma letra e um número.";

            return null;
        }

        public static string DicaPolitica =>
            $"Mín. {MinimoCaracteres} caracteres, com uma letra e um número.";

        // ---------- token de sessao ----------

        /// <summary>
        /// 32 bytes de aleatoriedade criptografica. Vai no cookie; o banco
        /// guarda so o hash.
        /// </summary>
        public static string TokenNovo() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        /// <summary>
        /// O que vai para a coluna token_hash. SHA-256 simples serve aqui,
        /// e nao PBKDF2: o token ja tem 256 bits de entropia, nao ha o que
        /// adivinhar, e isto roda a cada requisicao.
        /// </summary>
        public static string HashDoToken(string token) => Sha256Hex(token);
    }
}
