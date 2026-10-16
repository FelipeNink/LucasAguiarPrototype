namespace LucasAguiar.Models
{
    /// <summary>Cargo da equipe. Ligado ao acesso por id, nunca por texto.</summary>
    public class Funcao
    {
        public int IdFuncao { get; set; }
        public string Nome { get; set; } = "";

        /// <summary>Quantos acessos estao nesta funcao. Zero permite excluir.</summary>
        public int Membros { get; set; }

        public bool EmUso => Membros > 0;
    }
}
