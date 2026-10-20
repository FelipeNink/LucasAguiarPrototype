using LucasAguiar.Data;
using LucasAguiar.Models;
using MySqlConnector;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>ClienteDAO.AniversariantesDoMes, contra o MySQL de teste.</summary>
    public class AniversariantesTeste
    {
        [FatoComBanco]
        public void Traz_so_quem_nasceu_no_mes_pela_ordem_do_dia()
        {
            var dao = new ClienteDAO(BancoDeTeste.Fabricar());
            var criados = new List<int>();

            try
            {
                int Criar(DateTime? nascimento) =>
                    Registrar(criados, dao.Inserir(new Cliente
                    {
                        NomeCliente = BancoDeTeste.NomeUnico("aniv"),
                        Telefone = "(48) 99999-1234",
                        DataNascimento = nascimento
                    }));

                var dia20 = Criar(new DateTime(1990, 3, 20));
                var dia5 = Criar(new DateTime(2001, 3, 5));
                var outroMes = Criar(new DateTime(1990, 4, 20));
                var semData = Criar(null);

                var ids = dao.AniversariantesDoMes(3)
                    .Select(c => c.IdCliente)
                    .Where(criados.Contains)
                    .ToList();

                Assert.Equal(new[] { dia5, dia20 }, ids);
                Assert.DoesNotContain(outroMes, ids);
                Assert.DoesNotContain(semData, ids);
            }
            finally
            {
                using var conexao = BancoDeTeste.Abrir();
                foreach (var id in criados)
                {
                    using var comando = new MySqlCommand("DELETE FROM cliente WHERE id_cli = @id", conexao);
                    comando.Parameters.AddWithValue("@id", id);
                    comando.ExecuteNonQuery();
                }
            }
        }

        private static int Registrar(List<int> criados, int id)
        {
            criados.Add(id);
            return id;
        }
    }
}
