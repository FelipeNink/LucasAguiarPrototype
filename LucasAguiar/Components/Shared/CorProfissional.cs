using LucasAguiar.Models;

namespace LucasAguiar.Components.Shared
{
    /// <summary>
    /// Cor de cada profissional: a da agenda, escolhida no cadastro, ou uma
    /// da paleta fixa pelo id -- a mesma em toda tela, para o Beto ser
    /// sempre azul na agenda e nas comissoes.
    /// </summary>
    public static class CorProfissional
    {
        private static readonly string[] Paleta = { "#2f6fed", "#0f9f8f", "#e38a12", "#8b5cf6", "#e0457b", "#4b5563" };

        public static string De(IEnumerable<Funcionario> funcionarios, int idFuncionario)
        {
            var cor = funcionarios.FirstOrDefault(f => f.IdFuncionario == idFuncionario)?.CorAgenda;
            return !string.IsNullOrWhiteSpace(cor) ? cor : Paleta[Math.Abs(idFuncionario) % Paleta.Length];
        }

        /// <summary>Texto branco em cor escura, quase preto em cor clara.</summary>
        public static string TextoSobre(string hex)
        {
            try
            {
                var h = hex.TrimStart('#');
                var r = Convert.ToInt32(h[..2], 16);
                var g = Convert.ToInt32(h[2..4], 16);
                var b = Convert.ToInt32(h[4..6], 16);
                return (0.299 * r + 0.587 * g + 0.114 * b) > 170 ? "#111827" : "#fff";
            }
            catch
            {
                return "#fff";
            }
        }

        public static string Inicial(string? nome) =>
            string.IsNullOrWhiteSpace(nome) ? "?" : nome.Trim()[..1].ToUpper();

        public static string PrimeiroNome(string? nome) =>
            string.IsNullOrWhiteSpace(nome) ? "—" : nome.Trim().Split(' ')[0];
    }
}
