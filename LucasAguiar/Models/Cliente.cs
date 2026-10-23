
namespace LucasAguiar.Models

{
    /// <summary>Como o cliente conheceu a barbearia.</summary>
    public static class OrigemCliente
    {
        public static readonly (string Valor, string Rotulo)[] Todas =
        {
            ("INDICACAO", "Indicação"),
            ("INSTAGRAM", "Instagram"),
            ("FACEBOOK", "Facebook"),
            ("GOOGLE", "Google"),
            ("FACHADA", "Passou em frente"),
            ("OUTRO", "Outro")
        };

        public static string Rotulo(string? valor) =>
            Todas.FirstOrDefault(o => o.Valor == valor).Rotulo ?? (valor ?? "—");
    }

    /// <summary>Onde o cliente disse se aceita mensagens no WhatsApp.</summary>
    public static class OrigemConsentimento
    {
        public const string Cadastro = "CADASTRO";
        public const string Online = "ONLINE";

        public static string Rotulo(string? valor) => valor switch
        {
            Cadastro => "no cadastro",
            Online => "no agendamento online",
            _ => ""
        };
    }

    public class Cliente : IEndereco
    {
        public int IdCliente{ get; set; }
        public string? NomeCliente { get; set; }
        public string? Telefone { get; set; }
        public string? CPF { get; set; }
        public DateTime? DataNascimento { get; set; }
        public string? RG { get; set; }
        public string? Cep { get; set; }
        public string? Estado { get; set; }
        public string? Cidade { get; set; }
        public string? Bairro { get; set; }
        public string? Rua { get; set; }
        public string? Numero { get; set; }

        public int? IdPlano { get; set; }

        public string? Email { get; set; }
        public string? Origem { get; set; }
        public string? Observacoes { get; set; }

        /// <summary>Profissional preferido escolhido no cadastro (cliente.id_fun_pref_fk).</summary>
        public int? IdProfissionalPreferidoCadastro { get; set; }

        public bool Ativo { get; set; } = true;

        /// <summary>
        /// Aceita mensagens de relacionamento no WhatsApp (LGPD): aniversario,
        /// "sumiu", pedido de avaliacao. Null e ainda nao perguntado. O
        /// lembrete do horario marcado e parte do servico e nao depende disto.
        /// </summary>
        public bool? AceitaWhatsApp { get; set; }

        /// <summary>Quando a resposta mudou pela ultima vez, e onde: a prova do consentimento.</summary>
        public DateTime? AceitaWhatsAppEm { get; set; }
        public string? AceitaWhatsAppOrigem { get; set; }

        /// <summary>Disse que nao quer: nenhuma mensagem de relacionamento para ele.</summary>
        public bool RecusouWhatsApp => AceitaWhatsApp == false;

        // Preenchidos por join, para exibicao
        public string? NomePlano { get; set; }
        public decimal ValorPlano { get; set; }

        // Historico de vendas, preenchido so por ClienteDAO.ListarComHistorico
        public DateTime? UltimaVisita { get; set; }
        public int Visitas { get; set; }
        public decimal TotalGasto { get; set; }

        /// <summary>O do cadastro; sem ele, quem mais atendeu o cliente.</summary>
        public int? IdProfissionalPreferido { get; set; }
        public string? NomeProfissionalPreferido { get; set; }

        public bool TemPlano => IdPlano.HasValue && IdPlano.Value > 0;

        /// <summary>Nome com um complemento para diferenciar homônimos na busca.</summary>
        public string Identificacao =>
            string.IsNullOrWhiteSpace(Telefone)
                ? (NomeCliente ?? "(sem nome)")
                : $"{NomeCliente} — {Telefone}";
    }
}


