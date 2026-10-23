namespace LucasAguiar.Models
{
    public static class TipoComissao
    {
        /// <summary>Valor em R$ por atendimento. É como a comissão sempre foi gravada.</summary>
        public const string Fixo = "FIXO";

        /// <summary>% sobre o valor cobrado.</summary>
        public const string Percentual = "PERCENTUAL";
    }

    public class Servico
    {
        public int IdServico {get; set;}

        public string? NomeServico {get; set;}

        public decimal PrecoServico {get; set;}

        public int DuracaoServico {get; set;}

        /// <summary>Valor da comissão: R$ ou %, conforme <see cref="TipoComissao"/>.</summary>
        public decimal ComissaoServico {get; set;}

        public string TipoComissao { get; set; } = Models.TipoComissao.Fixo;

        public int? IdCategoria { get; set; }
        public decimal? CustoEstimado { get; set; }
        public bool Ativo { get; set; } = true;

        // Preenchido por join
        public string? NomeCategoria { get; set; }

        public bool ComissaoPercentual => TipoComissao == Models.TipoComissao.Percentual;

        /// <summary>"R$ 15,00" ou "40%", conforme o tipo.</summary>
        public string ComissaoTexto =>
            ComissaoPercentual ? $"{ComissaoServico:0.##}%" : ComissaoServico.ToString("C");

        /// <summary>Comissão em R$ sobre o preço cheio, qualquer que seja o tipo.</summary>
        public decimal ComissaoEmReais =>
            ComissaoPercentual
                ? Math.Round((decimal)PrecoServico * (decimal)ComissaoServico / 100m, 2)
                : (decimal)ComissaoServico;
    }
}
/*  id_serv int primary key auto_increment,
  nome_serv varchar(100),
  preco_serv float,
  duracao_min int,
  comis_funcionario_cli float
*/