namespace LucasAguiar.Models
{
    public class Servico
    {
        public int IdServico {get; set;}

        public string? NomeServico {get; set;}

        public float PrecoServico {get; set;}

        public int DuracaoServico {get; set;}
        
        public float ComissaoServico {get; set;}

    }
}
/*  id_serv int primary key auto_increment,
  nome_serv varchar(100),
  preco_serv float,
  duracao_min int,
  comis_funcionario_cli float
*/