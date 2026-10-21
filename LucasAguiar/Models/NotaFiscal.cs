namespace LucasAguiar.Models
{
    /// <summary>
    /// Ciclo da nota. PENDENTE e PROCESSANDO sao do emissor em segundo
    /// plano; ERRO espera alguem corrigir o cadastro e reenviar.
    /// </summary>
    public static class StatusNota
    {
        /// <summary>Na fila, ainda nao enviada.</summary>
        public const string Pendente = "PENDENTE";

        /// <summary>Aceita pela Focus, aguardando o ambiente nacional.</summary>
        public const string Processando = "PROCESSANDO";

        public const string Autorizada = "AUTORIZADA";

        /// <summary>Recusada. O motivo fica na mensagem.</summary>
        public const string Erro = "ERRO";

        public const string Cancelada = "CANCELADA";

        /// <summary>Venda cancelada antes de a nota existir: nunca foi emitida.</summary>
        public const string Descartada = "DESCARTADA";

        /// <summary>Enquanto existir uma nota nestes status, a venda nao muda valores.</summary>
        public static readonly string[] Vigentes = { Pendente, Processando, Autorizada };

        public static string Rotulo(string? status) => status switch
        {
            Pendente => "Na fila",
            Processando => "Processando",
            Autorizada => "Autorizada",
            Erro => "Erro",
            Cancelada => "Cancelada",
            Descartada => "Descartada",
            _ => status ?? "—"
        };
    }

    public static class AmbienteFiscal
    {
        public const string Homologacao = "HOMOLOGACAO";
        public const string Producao = "PRODUCAO";

        public static string Rotulo(string? ambiente) =>
            ambiente == Producao ? "Produção" : "Homologação";
    }

    /// <summary>
    /// Dados do prestador e da tributacao. Linha unica em config_fiscal.
    /// O token da Focus NFe nao fica aqui: vem das variaveis
    /// Fiscal__TokenHomologacao e Fiscal__TokenProducao.
    /// </summary>
    public class ConfigFiscal
    {
        /// <summary>Desligado, as vendas nao geram nota.</summary>
        public bool Ativo { get; set; }
        public string Ambiente { get; set; } = AmbienteFiscal.Homologacao;

        public string? Cnpj { get; set; }
        public string? InscricaoMunicipal { get; set; }
        public string? RazaoSocial { get; set; }

        /// <summary>Codigo IBGE de 7 digitos do municipio do salao.</summary>
        public string? CodigoMunicipio { get; set; }

        /// <summary>1 nao optante, 2 MEI, 3 ME/EPP.</summary>
        public int OpcaoSimples { get; set; } = 3;

        /// <summary>So para ME/EPP: 1 tudo pelo SN, 2 ISS fora do SN, 3 tudo fora.</summary>
        public int? RegimeSimples { get; set; } = 1;

        /// <summary>Regime especial municipal; 0 e nenhum.</summary>
        public int RegimeEspecial { get; set; }

        /// <summary>cTribNac. 06.01.01 e barbearia, cabeleireiros e congeneres (LC 116, item 6.01).</summary>
        public string CodigoTributacaoNacional { get; set; } = "060101";
        public string? CodigoTributacaoMunicipal { get; set; }
        public string? CodigoNbs { get; set; }

        /// <summary>
        /// Aliquota do ISS em %. Em branco, o ambiente nacional usa a do
        /// municipio, quando ele esta parametrizado.
        /// </summary>
        public decimal? AliquotaIss { get; set; }

        /// <summary>IBS/CBS da reforma. Em branco, o grupo nao vai na nota.</summary>
        public string? IbsCbsCst { get; set; }
        public string? IbsCbsClassificacao { get; set; }

        public int SerieDps { get; set; } = 1;
        public long ProximoNumeroDps { get; set; } = 1;
    }

    public class NotaFiscal
    {
        public int IdNota { get; set; }
        public int IdVenda { get; set; }
        public string Status { get; set; } = StatusNota.Pendente;
        public string? Ambiente { get; set; }
        public string? Referencia { get; set; }
        public int? SerieDps { get; set; }
        public long? NumeroDps { get; set; }
        public decimal? Valor { get; set; }

        public string? Numero { get; set; }
        public string? CodigoVerificacao { get; set; }
        public string? UrlConsulta { get; set; }
        public string? UrlPdf { get; set; }
        public string? CaminhoXml { get; set; }

        public string? Mensagem { get; set; }
        public int Tentativas { get; set; }

        /// <summary>Pedido de cancelamento ainda nao atendido pelo emissor.</summary>
        public bool Cancelar { get; set; }
        public string? JustificativaCancelamento { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime? EmitidaEm { get; set; }

        // Preenchidos por join
        public DateTime? DataVenda { get; set; }
        public string? NomeCliente { get; set; }
        public string? StatusVenda { get; set; }

        public bool PodeReenviar => Status == StatusNota.Erro;
        public bool PodeCancelar => Status == StatusNota.Autorizada && !Cancelar;
    }

    /// <summary>
    /// O que de uma venda vai para a NFS-e. Produto fica de fora (e NFC-e,
    /// outra fase); plano entra pelo valor pago; servico coberto por plano
    /// sai por zero e nao gera nota -- o plano ja foi tributado na venda dele.
    /// </summary>
    public static class RegrasNfse
    {
        public record Valores(decimal Bruto, decimal Desconto)
        {
            public decimal Liquido => Math.Max(0, Bruto - Desconto);
        }

        public static bool EntraNaNota(VendaItem item) =>
            item.TipoItem != TipoItem.Produto && !item.CobertoPlano;

        /// <summary>
        /// Bruto dos itens tributaveis e o desconto deles: o da propria linha
        /// mais a parte do desconto geral proporcional ao peso dos itens no
        /// subtotal da venda.
        /// </summary>
        public static Valores Calcular(Venda venda)
        {
            var itens = venda.Itens.Where(EntraNaNota).ToList();

            var bruto = Math.Round(itens.Sum(i => i.ValorUnitario * i.Quantidade), 2);
            var liquidoItens = itens.Sum(i => i.ValorTotal);
            var descontoItens = bruto - liquidoItens;

            var subtotal = venda.Subtotal;
            var parteGeral = subtotal > 0
                ? Math.Round(Math.Min(venda.Desconto, subtotal) * liquidoItens / subtotal, 2)
                : 0m;

            return new Valores(bruto, Math.Min(bruto, descontoItens + parteGeral));
        }

        /// <summary>"Corte (2x), Barba" -- limite de 1000 caracteres da NFS-e.</summary>
        public static string Descrever(Venda venda)
        {
            var partes = venda.Itens
                .Where(EntraNaNota)
                .Select(i => i.Quantidade > 1 ? $"{i.Descricao} ({i.Quantidade}x)" : i.Descricao);

            var texto = string.Join(", ", partes);
            return texto.Length <= 1000 ? texto : texto[..997] + "...";
        }
    }
}
