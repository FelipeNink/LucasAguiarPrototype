using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Jornada semanal e bloqueios de cada profissional. As regras ficam em
    /// Disponibilidade.
    /// </summary>
    public class JornadaDAO
    {
        private readonly Conexao _conexao;

        public JornadaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>Os sete dias, na ordem do DayOfWeek; null se o profissional ainda nao tem jornada.</summary>
        public List<JornadaDia>? Listar(int idFuncionario)
        {
            var grade = Enum.GetValues<DayOfWeek>()
                .Select(d => new JornadaDia { Dia = d })
                .ToList();
            var achou = false;

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT dia_semana, trabalha, entrada, saida, almoco_inicio, almoco_fim
                  FROM jornada_funcionario WHERE id_fun_fk = @id", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);
            using var leitor = comando.ExecuteReader();

            while (leitor.Read())
            {
                var numero = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("dia_semana")));
                if (numero is < 0 or > 6) continue;

                var dia = grade[numero];
                dia.Trabalha = leitor.GetBoolean("trabalha");
                dia.Entrada = leitor.GetTimeSpan("entrada");
                dia.Saida = leitor.GetTimeSpan("saida");
                dia.AlmocoInicio = leitor.IsDBNull(leitor.GetOrdinal("almoco_inicio")) ? null : leitor.GetTimeSpan("almoco_inicio");
                dia.AlmocoFim = leitor.IsDBNull(leitor.GetOrdinal("almoco_fim")) ? null : leitor.GetTimeSpan("almoco_fim");
                achou = true;
            }

            return achou ? grade : null;
        }

        public void Salvar(int idFuncionario, IEnumerable<JornadaDia> jornada)
        {
            var lista = jornada.ToList();
            if (Disponibilidade.ErroJornada(lista) is string problema)
                throw new Exception(problema);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                foreach (var d in lista)
                {
                    using var comando = new MySqlCommand(@"
                        INSERT INTO jornada_funcionario
                            (id_fun_fk, dia_semana, trabalha, entrada, saida, almoco_inicio, almoco_fim)
                        VALUES (@id, @dia, @trabalha, @entrada, @saida, @almocoInicio, @almocoFim) AS novo
                        ON DUPLICATE KEY UPDATE trabalha = novo.trabalha, entrada = novo.entrada, saida = novo.saida,
                            almoco_inicio = novo.almoco_inicio, almoco_fim = novo.almoco_fim",
                        conexao, transacao);

                    comando.Parameters.AddWithValue("@id", idFuncionario);
                    comando.Parameters.AddWithValue("@dia", (int)d.Dia);
                    comando.Parameters.AddWithValue("@trabalha", d.Trabalha ? 1 : 0);
                    comando.Parameters.AddWithValue("@entrada", d.Entrada);
                    comando.Parameters.AddWithValue("@saida", d.Saida);
                    comando.Parameters.AddWithValue("@almocoInicio", d.AlmocoInicio ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@almocoFim", d.AlmocoFim ?? (object)DBNull.Value);
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

        /// <summary>Bloqueios do profissional que ainda nao terminaram ate <paramref name="desde"/>.</summary>
        public List<BloqueioFuncionario> ListarBloqueios(int idFuncionario, DateTime desde) =>
            ListarBloqueios(idFuncionario, desde, new DateTime(9999, 12, 31));

        /// <summary>Bloqueios que cruzam o periodo [inicio, fim).</summary>
        public List<BloqueioFuncionario> ListarBloqueios(int idFuncionario, DateTime inicio, DateTime fim)
        {
            var lista = new List<BloqueioFuncionario>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                SELECT id_bloq, id_fun_fk, inicio, fim, motivo
                  FROM bloqueio_funcionario
                 WHERE id_fun_fk = @id AND fim > @inicio AND inicio < @fim
                 ORDER BY inicio", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);
            comando.Parameters.AddWithValue("@inicio", inicio);
            comando.Parameters.AddWithValue("@fim", fim);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new BloqueioFuncionario
                {
                    IdBloqueio = leitor.GetInt32("id_bloq"),
                    IdFuncionario = leitor.GetInt32("id_fun_fk"),
                    Inicio = leitor.GetDateTime("inicio"),
                    Fim = leitor.GetDateTime("fim"),
                    Motivo = leitor.IsDBNull(leitor.GetOrdinal("motivo")) ? null : leitor.GetString("motivo")
                });
            }

            return lista;
        }

        /// <summary>
        /// Grava os bloqueios novos (IdBloqueio 0) e apaga os removidos, numa
        /// transacao so -- o formulario do profissional salva tudo junto.
        /// </summary>
        public void SalvarBloqueios(int idFuncionario, IEnumerable<BloqueioFuncionario> novos, IEnumerable<int> removidos)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                foreach (var id in removidos)
                {
                    using var apagar = new MySqlCommand(
                        "DELETE FROM bloqueio_funcionario WHERE id_bloq = @bloq AND id_fun_fk = @id", conexao, transacao);
                    apagar.Parameters.AddWithValue("@bloq", id);
                    apagar.Parameters.AddWithValue("@id", idFuncionario);
                    apagar.ExecuteNonQuery();
                }

                foreach (var b in novos.Where(b => b.IdBloqueio == 0))
                {
                    if (b.Fim <= b.Inicio)
                        throw new Exception("O fim do bloqueio deve ser depois do início.");

                    using var inserir = new MySqlCommand(@"
                        INSERT INTO bloqueio_funcionario (id_fun_fk, inicio, fim, motivo)
                        VALUES (@id, @inicio, @fim, @motivo)", conexao, transacao);
                    inserir.Parameters.AddWithValue("@id", idFuncionario);
                    inserir.Parameters.AddWithValue("@inicio", b.Inicio);
                    inserir.Parameters.AddWithValue("@fim", b.Fim);
                    inserir.Parameters.AddWithValue("@motivo",
                        string.IsNullOrWhiteSpace(b.Motivo) ? DBNull.Value : b.Motivo.Trim());
                    inserir.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch
            {
                transacao.Rollback();
                throw;
            }
        }
    }
}
