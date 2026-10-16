using System.Text.Json;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Registro do que aconteceu: quem, quando, de onde e, quando faz
    /// sentido, o antes e o depois.
    ///
    /// Nunca grava senha, hash nem token -- nem dentro do meta.
    ///
    /// Gravar o log nao pode derrubar a operacao que o gerou: se a tabela
    /// de log falhar, o login ou o salvar precisam terminar de qualquer
    /// jeito. Por isso <see cref="Registrar"/> engole a excecao e manda
    /// para o ILogger, que no Railway aparece no painel.
    /// </summary>
    public class LogDAO
    {
        private readonly Conexao _conexao;
        private readonly ILogger<LogDAO> _log;

        public LogDAO(Conexao conexao, ILogger<LogDAO> log)
        {
            _conexao = conexao;
            _log = log;
        }

        public void Registrar(Evento evento)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand(
                    @"INSERT INTO log_sistema
                          (tipo, nivel, acao, descricao, id_usr_fk, nome_gravado,
                           ip, alvo, meta)
                      VALUES
                          (@tipo, @nivel, @acao, @descricao, @usuario, @nome,
                           @ip, @alvo, @meta)", conexao);

                comando.Parameters.AddWithValue("@tipo", evento.Tipo);
                comando.Parameters.AddWithValue("@nivel", evento.Nivel);
                comando.Parameters.AddWithValue("@acao", Corta(evento.Acao, 40));
                comando.Parameters.AddWithValue("@descricao", Corta(evento.Descricao, 400));
                comando.Parameters.AddWithValue("@usuario",
                    evento.IdUsuario.HasValue ? evento.IdUsuario.Value : DBNull.Value);
                comando.Parameters.AddWithValue("@nome", Corta(evento.NomeGravado, 100));
                comando.Parameters.AddWithValue("@ip", Corta(evento.Ip, 45));
                comando.Parameters.AddWithValue("@alvo", Corta(evento.Alvo, 120));
                comando.Parameters.AddWithValue("@meta", evento.MetaJson());

                comando.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Não foi possível registrar o evento {Acao} na auditoria.", evento.Acao);
            }
        }

        // ---------- atalhos dos eventos de acesso ----------

        public void Login(Usuario usuario, string? ip) => Registrar(new Evento
        {
            Tipo = TipoLog.Usuario,
            Acao = Acoes.Login,
            Descricao = $"{usuario.NomeVisivel} entrou no sistema.",
            IdUsuario = usuario.IdUsr,
            NomeGravado = usuario.NomeVisivel,
            Ip = ip
        });

        public void Logout(int idUsuario, string? nome, string? ip) => Registrar(new Evento
        {
            Tipo = TipoLog.Usuario,
            Acao = Acoes.Logout,
            Descricao = $"{nome} saiu do sistema.",
            IdUsuario = idUsuario,
            NomeGravado = nome,
            Ip = ip
        });

        /// <summary>
        /// O motivo real da falha fica no log -- usuario inexistente, senha
        /// errada ou conta desativada --, nunca na tela: lá a mensagem e
        /// sempre a mesma, para nao revelar quais logins existem.
        /// </summary>
        public void LoginFalhou(string? usuarioTentado, string motivo, string? ip,
            int? idUsuario = null) => Registrar(new Evento
            {
                Tipo = TipoLog.Auditoria,
                Nivel = NivelLog.Alerta,
                Acao = Acoes.LoginFalhou,
                Descricao = motivo,
                IdUsuario = idUsuario,
                Ip = ip,
                Alvo = usuarioTentado
            });

        public void LoginBloqueado(string? usuarioTentado, string? ip, int minutos) =>
            Registrar(new Evento
            {
                Tipo = TipoLog.Auditoria,
                Nivel = NivelLog.Alerta,
                Acao = Acoes.LoginBloqueado,
                Descricao = $"Bloqueado por {minutos} minuto(s) após "
                    + $"{TentativaLoginDAO.MaximoFalhas} tentativas.",
                Ip = ip,
                Alvo = usuarioTentado
            });

        // ---------- leitura (tela de Logs) ----------

        public class Filtro
        {
            public string? Tipo { get; set; }
            public string? Nivel { get; set; }
            public string? Busca { get; set; }
            public DateTime? De { get; set; }
            public DateTime? Ate { get; set; }
            public int Pagina { get; set; } = 1;
            public int PorPagina { get; set; } = 50;
        }

        public (List<LinhaLog> Linhas, int Total) Listar(Filtro filtro)
        {
            var onde = new List<string>();

            if (!string.IsNullOrWhiteSpace(filtro.Tipo)) onde.Add("tipo = @tipo");
            if (!string.IsNullOrWhiteSpace(filtro.Nivel)) onde.Add("nivel = @nivel");
            if (filtro.De.HasValue) onde.Add("quando >= @de");
            if (filtro.Ate.HasValue) onde.Add("quando < @ate");

            if (!string.IsNullOrWhiteSpace(filtro.Busca))
            {
                onde.Add(@"(acao LIKE @busca OR descricao LIKE @busca
                            OR nome_gravado LIKE @busca OR alvo LIKE @busca)");
            }

            var filtragem = onde.Count == 0 ? "" : " WHERE " + string.Join(" AND ", onde);

            using var conexao = _conexao.GetConnection();

            int total;
            using (var conta = new MySqlCommand(
                "SELECT COUNT(*) FROM log_sistema" + filtragem, conexao))
            {
                Parametros(conta, filtro);
                total = Convert.ToInt32(conta.ExecuteScalar());
            }

            var pagina = Math.Max(1, filtro.Pagina);
            var porPagina = Math.Clamp(filtro.PorPagina, 10, 200);

            using var comando = new MySqlCommand(
                @"SELECT id_log, quando, tipo, nivel, acao, descricao,
                         id_usr_fk, nome_gravado, ip, alvo, meta
                    FROM log_sistema" + filtragem +
                " ORDER BY quando DESC, id_log DESC LIMIT @limite OFFSET @salto", conexao);

            Parametros(comando, filtro);
            comando.Parameters.AddWithValue("@limite", porPagina);
            comando.Parameters.AddWithValue("@salto", (pagina - 1) * porPagina);

            var linhas = new List<LinhaLog>();

            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    linhas.Add(new LinhaLog
                    {
                        IdLog = leitor.GetInt32("id_log"),
                        Quando = leitor.GetDateTime("quando"),
                        Tipo = leitor.GetString("tipo"),
                        Nivel = leitor.GetString("nivel"),
                        Acao = leitor.GetString("acao"),
                        Descricao = DAOHelper.GetString(leitor, "descricao"),
                        IdUsuario = DAOHelper.IsNull(leitor, "id_usr_fk")
                            ? null : leitor.GetInt32("id_usr_fk"),
                        NomeGravado = DAOHelper.GetString(leitor, "nome_gravado"),
                        Ip = DAOHelper.GetString(leitor, "ip"),
                        Alvo = DAOHelper.GetString(leitor, "alvo"),
                        Meta = DAOHelper.GetString(leitor, "meta")
                    });
                }
            }

            return (linhas, total);
        }

        private static void Parametros(MySqlCommand comando, Filtro filtro)
        {
            if (!string.IsNullOrWhiteSpace(filtro.Tipo))
                comando.Parameters.AddWithValue("@tipo", filtro.Tipo);

            if (!string.IsNullOrWhiteSpace(filtro.Nivel))
                comando.Parameters.AddWithValue("@nivel", filtro.Nivel);

            if (filtro.De.HasValue)
                comando.Parameters.AddWithValue("@de", filtro.De.Value);

            if (filtro.Ate.HasValue)
                comando.Parameters.AddWithValue("@ate", filtro.Ate.Value);

            if (!string.IsNullOrWhiteSpace(filtro.Busca))
                comando.Parameters.AddWithValue("@busca", "%" + filtro.Busca.Trim() + "%");
        }

        /// <summary>
        /// Retencao: um ano de log. Mais que isso nao e consultado e a
        /// tabela so cresce. Roda na subida.
        /// </summary>
        public int AplicarRetencao()
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "DELETE FROM log_sistema WHERE quando < NOW() - INTERVAL 1 YEAR", conexao);

            return comando.ExecuteNonQuery();
        }

        private static object Corta(string? texto, int tamanho)
        {
            if (string.IsNullOrWhiteSpace(texto)) return DBNull.Value;

            return texto.Length <= tamanho ? texto : texto[..tamanho];
        }
    }

    public static class TipoLog
    {
        public const string Sistema = "sistema";
        public const string Usuario = "usuario";
        public const string Auditoria = "auditoria";

        public static readonly string[] Todos = { Sistema, Usuario, Auditoria };

        public static string Rotulo(string? tipo) => tipo switch
        {
            Sistema => "Sistema",
            Usuario => "Usuário",
            Auditoria => "Auditoria",
            _ => tipo ?? ""
        };
    }

    public static class NivelLog
    {
        public const string Info = "info";
        public const string Alerta = "warn";
        public const string Erro = "error";

        public static readonly string[] Todos = { Info, Alerta, Erro };

        public static string Rotulo(string? nivel) => nivel switch
        {
            Info => "Informação",
            Alerta => "Alerta",
            Erro => "Erro",
            _ => nivel ?? ""
        };
    }

    /// <summary>Nomes dos eventos. Constantes para o filtro e o log casarem.</summary>
    public static class Acoes
    {
        public const string Login = "login";
        public const string Logout = "logout";
        public const string LoginFalhou = "login_falhou";
        public const string LoginBloqueado = "login_bloqueado";
        public const string UsuarioCriado = "usuario_criado";
        public const string UsuarioEditado = "usuario_editado";
        public const string UsuarioAtivado = "usuario_ativado";
        public const string UsuarioDesativado = "usuario_desativado";
        public const string UsuarioExcluido = "usuario_excluido";
        public const string PerfilAlterado = "perfil_alterado";
        public const string SenhaAlterada = "senha_alterada";
        public const string SenhaRedefinida = "senha_redefinida";
        public const string SenhaRegravada = "senha_regravada";
        public const string SessoesEncerradas = "sessoes_encerradas";
        public const string PermissoesAtualizadas = "permissoes_atualizadas";
        public const string FuncaoCriada = "funcao_criada";
        public const string FuncaoRenomeada = "funcao_renomeada";
        public const string FuncaoExcluida = "funcao_excluida";
        public const string AcessoNegado = "acesso_negado";
        public const string Exportou = "exportou";
    }

    /// <summary>Um evento a gravar.</summary>
    public class Evento
    {
        public string Tipo { get; set; } = TipoLog.Usuario;
        public string Nivel { get; set; } = NivelLog.Info;
        public string Acao { get; set; } = "";
        public string? Descricao { get; set; }
        public int? IdUsuario { get; set; }

        /// <summary>
        /// O nome de quem agiu, gravado no momento: o log sobrevive a
        /// exclusao do usuario e continua dizendo quem fez.
        /// </summary>
        public string? NomeGravado { get; set; }

        public string? Ip { get; set; }

        /// <summary>Sobre quem ou sobre o que foi a acao.</summary>
        public string? Alvo { get; set; }

        /// <summary>Detalhe estruturado, como o antes e depois de um campo.</summary>
        public Dictionary<string, object?>? Meta { get; set; }

        public object MetaJson()
        {
            if (Meta == null || Meta.Count == 0) return DBNull.Value;

            try
            {
                return JsonSerializer.Serialize(Meta);
            }
            catch (NotSupportedException)
            {
                return DBNull.Value;
            }
        }
    }

    /// <summary>Uma linha da tela de Logs.</summary>
    public class LinhaLog
    {
        public int IdLog { get; set; }
        public DateTime Quando { get; set; }
        public string Tipo { get; set; } = "";
        public string Nivel { get; set; } = "";
        public string Acao { get; set; } = "";
        public string? Descricao { get; set; }
        public int? IdUsuario { get; set; }
        public string? NomeGravado { get; set; }
        public string? Ip { get; set; }
        public string? Alvo { get; set; }
        public string? Meta { get; set; }

        public bool TemDetalhes => !string.IsNullOrWhiteSpace(Meta);
    }
}
