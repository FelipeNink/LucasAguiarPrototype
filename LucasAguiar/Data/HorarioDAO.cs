using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Grade semanal do horario de funcionamento e as excecoes do dia
    /// ("fechar hoje", "abrir agora"). As regras ficam em Expediente.
    /// </summary>
    public class HorarioDAO
    {
        private readonly Conexao _conexao;

        public HorarioDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>Os sete dias, na ordem do DayOfWeek. Dia sem linha sai fechado.</summary>
        public List<HorarioDia> Listar()
        {
            var grade = Enum.GetValues<DayOfWeek>()
                .Select(d => new HorarioDia { Dia = d })
                .ToList();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT dia_semana, aberto, abre, fecha FROM horario_funcionamento", conexao);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                var numero = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("dia_semana")));
                if (numero is < 0 or > 6) continue;

                var dia = grade[numero];
                dia.Aberto = leitor.GetBoolean("aberto");
                dia.Abre = leitor.GetTimeSpan("abre");
                dia.Fecha = leitor.GetTimeSpan("fecha");
            }

            return grade;
        }

        public void Salvar(IEnumerable<HorarioDia> grade)
        {
            var lista = grade.ToList();

            foreach (var d in lista)
            {
                if (d.Aberto && d.Fecha <= d.Abre)
                    throw new Exception($"{Expediente.NomeDia[(int)d.Dia]}: o fechamento deve ser depois da abertura.");
            }

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                foreach (var d in lista)
                {
                    using var comando = new MySqlCommand(@"
                        INSERT INTO horario_funcionamento (dia_semana, aberto, abre, fecha)
                        VALUES (@dia, @aberto, @abre, @fecha) AS novo
                        ON DUPLICATE KEY UPDATE aberto = novo.aberto, abre = novo.abre, fecha = novo.fecha",
                        conexao, transacao);

                    comando.Parameters.AddWithValue("@dia", (int)d.Dia);
                    comando.Parameters.AddWithValue("@aberto", d.Aberto ? 1 : 0);
                    comando.Parameters.AddWithValue("@abre", d.Abre);
                    comando.Parameters.AddWithValue("@fecha", d.Fecha);
                    comando.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }

        /// <summary>true/false se abriram ou fecharam na mao nessa data; null segue a grade.</summary>
        public bool? Excecao(DateTime data)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT aberto FROM horario_excecao WHERE data = @data", conexao);
            comando.Parameters.AddWithValue("@data", data.Date);

            var valor = comando.ExecuteScalar();
            return valor == null || valor is DBNull ? null : Convert.ToBoolean(valor);
        }

        /// <summary>Abre ou fecha a barbearia na data; null volta a seguir a grade.</summary>
        public void DefinirExcecao(DateTime data, bool? aberto)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(aberto == null
                ? "DELETE FROM horario_excecao WHERE data = @data"
                : @"INSERT INTO horario_excecao (data, aberto) VALUES (@data, @aberto) AS novo
                    ON DUPLICATE KEY UPDATE aberto = novo.aberto", conexao);

            comando.Parameters.AddWithValue("@data", data.Date);
            if (aberto != null) comando.Parameters.AddWithValue("@aberto", aberto.Value ? 1 : 0);
            comando.ExecuteNonQuery();
        }
    }
}
