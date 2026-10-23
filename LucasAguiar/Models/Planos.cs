namespace LucasAguiar.Models
{
    public static class Periodicidade
    {
        public const string Mensal = "MENSAL";

        public static readonly (string Valor, string Rotulo, int Dias)[] Todas =
        {
            (Mensal, "Mensal", 30),
            ("TRIMESTRAL", "Trimestral", 90),
            ("SEMESTRAL", "Semestral", 180),
            ("ANUAL", "Anual", 365)
        };

        public static string Rotulo(string? valor) =>
            Todas.FirstOrDefault(p => p.Valor == valor).Rotulo ?? (valor ?? "—");

        /// <summary>Validade dos créditos de uma assinatura deste plano. Mensal continua 30 dias.</summary>
        public static int Dias(string? valor)
        {
            var dias = Todas.FirstOrDefault(p => p.Valor == valor).Dias;
            return dias > 0 ? dias : 30;
        }
    }

    /// <summary>O que o profissional recebe pelo serviço coberto pelo plano.</summary>
    public static class RegraComissaoAssinante
    {
        public const string Normal = "NORMAL";
        public const string SemComissao = "SEM_COMISSAO";

        public static readonly (string Valor, string Rotulo)[] Todas =
        {
            (Normal, "Comissão normal do serviço"),
            (SemComissao, "Sem comissão")
        };
    }

    public class Plano

    {
        public int IdPlano { get; set; }

        public string? NomePlano { get; set; }

        public string? Descricao { get; set; }


        public decimal? Valor { get; set; }

        public string Periodicidade { get; set; } = Models.Periodicidade.Mensal;
        public string RegraComissaoAssinante { get; set; } = Models.RegraComissaoAssinante.Normal;
        public bool Ativo { get; set; } = true;

        /// <summary>Soma das quantidades dos componentes. Preenchido na listagem.</summary>
        public int TotalItens { get; set; }

        /// <summary>Soma das quantidades de serviços. Preenchido na listagem.</summary>
        public int TotalServicos { get; set; }

        /// <summary>Assinaturas ativas e dentro da validade. Preenchido na listagem.</summary>
        public int AssinantesAtivos { get; set; }
    }
}
