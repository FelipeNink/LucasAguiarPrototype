using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Criar, editar, ativar, desativar e excluir os acessos da equipe.
    ///
    /// Separado do UsuarioDAO, que cuida de entrar no sistema: aqui sao as
    /// operacoes de quem administra, e todas passam pelas mesmas protecoes
    /// -- conferidas no servidor, e nao escondendo botao na tela, que e
    /// cortesia com quem olha e nao controle de acesso.
    ///
    /// As quatro protecoes:
    ///   1. Ninguem se desativa nem se exclui.
    ///   2. Ninguem desativa, exclui nem rebaixa a raiz.
    ///   3. O sistema nunca fica sem ao menos um administrador ativo.
    ///   4. So a raiz concede ou retira o perfil de administrador.
    /// </summary>
    public class EquipeDAO
    {
        private readonly Conexao _conexao;
        private readonly LogDAO _logs;

        public EquipeDAO(Conexao conexao, LogDAO logs)
        {
            _conexao = conexao;
            _logs = logs;
        }

        // =============================================================
        // LEITURA
        // =============================================================

        public List<Usuario> Listar(string? busca = null)
        {
            var lista = new List<Usuario>();
            var temBusca = !string.IsNullOrWhiteSpace(busca);

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                UsuarioDAO.SelecaoPadrao
                + (temBusca
                    ? @" WHERE u.nome_usr LIKE @busca
                         OR f.nome_funcao LIKE @busca
                         OR u.email_usr LIKE @busca"
                    : "")
                + " ORDER BY u.ativo_usr DESC, u.nome_usr", conexao);

            if (temBusca)
                comando.Parameters.AddWithValue("@busca", "%" + busca!.Trim() + "%");

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                lista.Add(SessaoDAO.LerUsuario(leitor));

            return lista;
        }

        public Usuario? BuscarPorId(int id)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                UsuarioDAO.SelecaoPadrao + " WHERE u.id_usr = @id LIMIT 1", conexao);

            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            return leitor.Read() ? SessaoDAO.LerUsuario(leitor) : null;
        }

        // =============================================================
        // CRIAR
        // =============================================================

        /// <summary>
        /// Cria o acesso e grava a matriz na mesma transacao.
        ///
        /// Numa operacao so de proposito: em duas chamadas, a segunda
        /// falhando deixaria um usuario capaz de entrar e incapaz de abrir
        /// qualquer tela -- e ninguem entenderia por que.
        /// </summary>
        public int Criar(
            Usuario novo, string? senha, IDictionary<string, Acao> matriz,
            bool exigirTrocaDeSenha, Autor autor)
        {
            var nome = Normalizar(novo.NomeUsr);

            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome de usuário.");

            var critica = SegurancaSenha.Criticar(senha);
            if (critica != null) throw new Exception(critica);

            var perfil = Perfil.Normalizar(novo.PerfilUsr);

            if (Perfil.EhAdmin(perfil) && !autor.EhRaiz)
            {
                throw new Exception(
                    "Apenas o super-administrador pode criar outro administrador.");
            }

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            if (NomeEmUso(conexao, transacao, nome, ignorar: null))
                throw new Exception($"O usuário \"{nome}\" já está cadastrado.");

            int id;

            using (var inserir = new MySqlCommand(
                @"INSERT INTO usuario
                      (nome_usr, email_usr, senha_usr, algo_senha_usr, ativo_usr,
                       perfil_usr, raiz_usr, trocar_senha_usr,
                       id_funcao_fk, id_fun_fk, data_criacao_usr)
                  VALUES
                      (@nome, @email, @senha, @algo, 1,
                       @perfil, 0, @trocar,
                       @funcao, @funcionario, NOW());
                  SELECT LAST_INSERT_ID();", conexao, transacao))
            {
                inserir.Parameters.AddWithValue("@nome", nome);
                inserir.Parameters.AddWithValue("@email", Texto(novo.EmailUsr));
                inserir.Parameters.AddWithValue("@senha", SegurancaSenha.Gerar(senha!));
                inserir.Parameters.AddWithValue("@algo", SegurancaSenha.Atual);
                inserir.Parameters.AddWithValue("@perfil", perfil);
                inserir.Parameters.AddWithValue("@trocar", exigirTrocaDeSenha);
                inserir.Parameters.AddWithValue("@funcao", Numero(novo.IdFuncao));
                inserir.Parameters.AddWithValue("@funcionario", Numero(novo.IdFuncionario));

                id = Convert.ToInt32(inserir.ExecuteScalar());
            }

            // Administrador ignora a matriz: guardar linhas para ele so
            // criaria a chance de um dia discordarem do perfil.
            if (!Perfil.EhAdmin(perfil))
                PermissaoDAO.SalvarNaTransacao(conexao, transacao, id, matriz);

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.UsuarioCriado,
                Descricao = $"Criou o acesso \"{nome}\" ({Perfil.Rotulo(perfil)}).",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = nome,
                Meta = new Dictionary<string, object?>
                {
                    ["perfil"] = perfil,
                    ["troca_de_senha_obrigatoria"] = exigirTrocaDeSenha,
                    ["permissoes"] = PermissaoDAO.Diferenca(
                        new Dictionary<string, Acao>(), matriz)
                }
            });

            return id;
        }

        // =============================================================
        // EDITAR
        // =============================================================

        /// <summary>
        /// Altera os dados do membro.
        ///
        /// Nao toca em ativo_usr de proposito: ativar e desativar sao acao
        /// propria, com confirmacao. Salvar o formulario nao pode
        /// ressuscitar um acesso que alguem desativou.
        ///
        /// <paramref name="atualizadoConhecido"/> e o carimbo que a tela
        /// leu ao abrir. Se a linha mudou desde entao, a gravacao para e
        /// avisa, em vez de apagar por cima do que outra pessoa fez.
        /// </summary>
        public void Atualizar(
            Usuario alterado, DateTime? atualizadoConhecido, Autor autor)
        {
            var nome = Normalizar(alterado.NomeUsr);

            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome de usuário.");

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var antes = LerParaAlteracao(conexao, transacao, alterado.IdUsr);

            if (antes.RaizUsr && !autor.EhRaiz)
                throw new Exception("Apenas o super-administrador pode alterar este acesso.");

            if (atualizadoConhecido.HasValue && antes.AtualizadoUsr.HasValue
                && Math.Abs((antes.AtualizadoUsr.Value - atualizadoConhecido.Value).TotalSeconds) > 1)
            {
                throw new Exception(
                    "Este acesso foi alterado por outra pessoa enquanto você editava. "
                    + "Abra de novo para ver os dados atuais.");
            }

            if (NomeEmUso(conexao, transacao, nome, ignorar: alterado.IdUsr))
                throw new Exception($"O usuário \"{nome}\" já está cadastrado.");

            using (var atualizar = new MySqlCommand(
                @"UPDATE usuario
                     SET nome_usr = @nome,
                         email_usr = @email,
                         id_funcao_fk = @funcao,
                         id_fun_fk = @funcionario
                   WHERE id_usr = @id", conexao, transacao))
            {
                atualizar.Parameters.AddWithValue("@nome", nome);
                atualizar.Parameters.AddWithValue("@email", Texto(alterado.EmailUsr));
                atualizar.Parameters.AddWithValue("@funcao", Numero(alterado.IdFuncao));
                atualizar.Parameters.AddWithValue("@funcionario", Numero(alterado.IdFuncionario));
                atualizar.Parameters.AddWithValue("@id", alterado.IdUsr);

                atualizar.ExecuteNonQuery();
            }

            transacao.Commit();

            var mudou = new Dictionary<string, object?>();

            if (!string.Equals(antes.NomeUsr, nome, StringComparison.Ordinal))
                mudou["usuário"] = $"{antes.NomeUsr} → {nome}";

            if (!string.Equals(antes.EmailUsr ?? "", alterado.EmailUsr ?? "", StringComparison.Ordinal))
                mudou["e-mail"] = $"{Vazio(antes.EmailUsr)} → {Vazio(alterado.EmailUsr)}";

            if (antes.IdFuncao != alterado.IdFuncao)
                mudou["função"] = $"{Vazio(antes.NomeFuncao)} → {Vazio(alterado.NomeFuncao)}";

            if (antes.IdFuncionario != alterado.IdFuncionario)
                mudou["profissional"] = $"{Vazio(antes.NomeFuncionario)} → {Vazio(alterado.NomeFuncionario)}";

            if (mudou.Count == 0) return;

            _logs.Registrar(new Evento
            {
                Acao = Acoes.UsuarioEditado,
                Descricao = $"Editou o acesso \"{nome}\": {string.Join("; ", mudou.Keys)}.",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = nome,
                Meta = mudou
            });
        }

        // =============================================================
        // ATIVAR E DESATIVAR
        // =============================================================

        /// <summary>
        /// Liga e desliga o acesso. Desativar encerra as sessoes abertas na
        /// mesma transacao: sem isso a pessoa continuaria dentro ate a
        /// sessao vencer.
        ///
        /// Preserva todo o historico -- vendas, comissoes, caixa. O acesso
        /// e que para de funcionar.
        /// </summary>
        public void AlterarStatus(int id, bool ativar, Autor autor)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var alvo = LerParaAlteracao(conexao, transacao, id);

            if (!ativar)
            {
                if (id == autor.Id)
                    throw new Exception("Não é possível desativar a si mesmo.");

                if (alvo.RaizUsr)
                    throw new Exception("O super-administrador não pode ser desativado.");

                GarantirQueSobraAdmin(conexao, transacao, id, alvo);
            }

            using (var atualizar = new MySqlCommand(
                "UPDATE usuario SET ativo_usr = @ativo WHERE id_usr = @id", conexao, transacao))
            {
                atualizar.Parameters.AddWithValue("@ativo", ativar);
                atualizar.Parameters.AddWithValue("@id", id);
                atualizar.ExecuteNonQuery();
            }

            if (!ativar)
                SessaoDAO.EncerrarTodasNaTransacao(conexao, transacao, id);

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = ativar ? Acoes.UsuarioAtivado : Acoes.UsuarioDesativado,
                Descricao = ativar
                    ? $"Reativou o acesso \"{alvo.NomeUsr}\"."
                    : $"Desativou o acesso \"{alvo.NomeUsr}\" e encerrou as sessões dele.",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = alvo.NomeUsr
            });
        }

        // =============================================================
        // PERFIL
        // =============================================================

        /// <summary>
        /// Promove a administrador ou rebaixa a comum. So a raiz faz isso.
        ///
        /// Promover apaga a matriz, que deixa de ter sentido. Rebaixar cria
        /// as linhas negadas: o ex-administrador fica sem nenhuma tela ate
        /// alguem definir o que ele pode, que e o estado seguro.
        /// </summary>
        public void AlterarPerfil(int id, string perfilNovo, Autor autor)
        {
            if (!autor.EhRaiz)
            {
                throw new Exception(
                    "Apenas o super-administrador pode conceder ou retirar o perfil "
                    + "de administrador.");
            }

            var perfil = Perfil.Normalizar(perfilNovo);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var alvo = LerParaAlteracao(conexao, transacao, id);

            if (string.Equals(alvo.PerfilUsr, perfil, StringComparison.OrdinalIgnoreCase))
                return;

            if (alvo.RaizUsr)
                throw new Exception("O perfil do super-administrador não pode ser alterado.");

            if (!Perfil.EhAdmin(perfil))
            {
                if (id == autor.Id)
                    throw new Exception("Não é possível retirar o próprio perfil de administrador.");

                GarantirQueSobraAdmin(conexao, transacao, id, alvo);
            }

            using (var atualizar = new MySqlCommand(
                "UPDATE usuario SET perfil_usr = @perfil WHERE id_usr = @id", conexao, transacao))
            {
                atualizar.Parameters.AddWithValue("@perfil", perfil);
                atualizar.Parameters.AddWithValue("@id", id);
                atualizar.ExecuteNonQuery();
            }

            if (Perfil.EhAdmin(perfil))
                PermissaoDAO.ApagarNaTransacao(conexao, transacao, id);
            else
                PermissaoDAO.SincronizarNaTransacao(conexao, transacao, id);

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.PerfilAlterado,
                Nivel = NivelLog.Alerta,
                Descricao = $"Alterou o perfil de \"{alvo.NomeUsr}\": "
                    + $"{Perfil.Rotulo(alvo.PerfilUsr)} → {Perfil.Rotulo(perfil)}.",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = alvo.NomeUsr,
                Meta = new Dictionary<string, object?>
                {
                    ["de"] = alvo.PerfilUsr,
                    ["para"] = perfil
                }
            });
        }

        // =============================================================
        // SENHA
        // =============================================================

        /// <summary>
        /// O administrador define uma senha nova para outra pessoa.
        ///
        /// Encerra as sessoes do alvo na mesma transacao -- quem estava
        /// usando aquele acesso para de usar agora, que e o ponto de
        /// redefinir senha -- e marca a troca obrigatoria na proxima
        /// entrada, para a senha provisoria nao virar permanente.
        /// </summary>
        public void RedefinirSenha(int id, string? senhaNova, bool exigirTroca, Autor autor)
        {
            var critica = SegurancaSenha.Criticar(senhaNova);
            if (critica != null) throw new Exception(critica);

            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var alvo = LerParaAlteracao(conexao, transacao, id);

            if (alvo.RaizUsr && !autor.EhRaiz)
            {
                throw new Exception(
                    "Apenas o super-administrador pode redefinir a senha deste acesso.");
            }

            using (var atualizar = new MySqlCommand(
                @"UPDATE usuario
                     SET senha_usr = @senha, algo_senha_usr = @algo,
                         trocar_senha_usr = @trocar
                   WHERE id_usr = @id", conexao, transacao))
            {
                atualizar.Parameters.AddWithValue("@senha", SegurancaSenha.Gerar(senhaNova!));
                atualizar.Parameters.AddWithValue("@algo", SegurancaSenha.Atual);
                atualizar.Parameters.AddWithValue("@trocar", exigirTroca);
                atualizar.Parameters.AddWithValue("@id", id);

                atualizar.ExecuteNonQuery();
            }

            SessaoDAO.EncerrarTodasNaTransacao(conexao, transacao, id);

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.SenhaRedefinida,
                Nivel = NivelLog.Alerta,
                Descricao = $"Redefiniu a senha de \"{alvo.NomeUsr}\" e encerrou as "
                    + "sessões dele.",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = alvo.NomeUsr,
                Meta = new Dictionary<string, object?>
                {
                    ["troca_obrigatoria"] = exigirTroca
                }
            });
        }

        // =============================================================
        // EXCLUIR
        // =============================================================

        /// <summary>
        /// Apaga o acesso. As permissoes e as sessoes saem junto pela chave
        /// estrangeira; nenhum dado de negocio e tocado -- as vendas e os
        /// movimentos de caixa continuam com o nome gravado neles.
        ///
        /// O log guarda quem foi excluido antes de a linha sumir: depois
        /// nao haveria mais de onde tirar.
        /// </summary>
        public void Excluir(int id, Autor autor)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var alvo = LerParaAlteracao(conexao, transacao, id);

            if (id == autor.Id)
                throw new Exception("Não é possível excluir a si mesmo.");

            if (alvo.RaizUsr)
                throw new Exception("O super-administrador não pode ser excluído.");

            GarantirQueSobraAdmin(conexao, transacao, id, alvo);

            using (var apagar = new MySqlCommand(
                "DELETE FROM usuario WHERE id_usr = @id", conexao, transacao))
            {
                apagar.Parameters.AddWithValue("@id", id);
                apagar.ExecuteNonQuery();
            }

            transacao.Commit();

            _logs.Registrar(new Evento
            {
                Acao = Acoes.UsuarioExcluido,
                Nivel = NivelLog.Alerta,
                Descricao = $"Excluiu o acesso \"{alvo.NomeUsr}\" "
                    + $"({Perfil.Rotulo(alvo.PerfilUsr)}).",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = alvo.NomeUsr,
                Meta = new Dictionary<string, object?>
                {
                    ["nome"] = alvo.NomeUsr,
                    ["email"] = alvo.EmailUsr,
                    ["perfil"] = alvo.PerfilUsr,
                    ["funcao"] = alvo.NomeFuncao
                }
            });
        }

        // =============================================================
        // PERMISSOES
        // =============================================================

        /// <summary>
        /// Grava a matriz de um membro e registra o que mudou, tela por
        /// tela. O log guarda a diferenca, nao um "permissoes atualizadas"
        /// que nao ajuda ninguem a investigar nada depois.
        /// </summary>
        public void SalvarPermissoes(int id, IDictionary<string, Acao> matriz, Autor autor)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();

            var alvo = LerParaAlteracao(conexao, transacao, id);

            if (alvo.EhAdmin)
            {
                throw new Exception(
                    "Administrador tem acesso total; a matriz não se aplica a ele.");
            }

            var antes = LerMatriz(conexao, transacao, id);

            PermissaoDAO.SalvarNaTransacao(conexao, transacao, id, matriz);

            var depois = LerMatriz(conexao, transacao, id);

            transacao.Commit();

            var diferenca = PermissaoDAO.Diferenca(antes, depois);
            if (diferenca.Count == 0) return;

            _logs.Registrar(new Evento
            {
                Acao = Acoes.PermissoesAtualizadas,
                Descricao = $"Alterou as permissões de \"{alvo.NomeUsr}\" em "
                    + $"{diferenca.Count} tela(s).",
                IdUsuario = autor.Id,
                NomeGravado = autor.Nome,
                Ip = autor.Ip,
                Alvo = alvo.NomeUsr,
                Meta = diferenca
            });
        }

        // =============================================================
        // APOIO
        // =============================================================

        /// <summary>
        /// Le o alvo com FOR UPDATE: duas pessoas desativando o mesmo
        /// acesso ao mesmo tempo nao podem ler o mesmo estado e as duas
        /// concluirem que ainda sobra administrador.
        /// </summary>
        private static Usuario LerParaAlteracao(
            MySqlConnection conexao, MySqlTransaction transacao, int id)
        {
            using var comando = new MySqlCommand(
                UsuarioDAO.SelecaoPadrao + " WHERE u.id_usr = @id FOR UPDATE",
                conexao, transacao);

            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();

            if (!leitor.Read())
                throw new Exception("Acesso não encontrado.");

            return SessaoDAO.LerUsuario(leitor);
        }

        /// <summary>
        /// O sistema nao pode ficar sem administrador ativo: sem nenhum,
        /// ninguem mais cria usuario nem concede permissao, e so uma
        /// escrita no banco a mao resolveria.
        /// </summary>
        private static void GarantirQueSobraAdmin(
            MySqlConnection conexao, MySqlTransaction transacao, int id, Usuario alvo)
        {
            if (!alvo.EhAdmin || !alvo.AtivoUsr) return;

            using var comando = new MySqlCommand(
                @"SELECT COUNT(*) FROM usuario
                   WHERE perfil_usr = 'ADMIN' AND ativo_usr = 1 AND id_usr <> @id",
                conexao, transacao);

            comando.Parameters.AddWithValue("@id", id);

            if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                throw new Exception("É preciso manter ao menos um administrador ativo.");
        }

        private static bool NomeEmUso(
            MySqlConnection conexao, MySqlTransaction transacao, string nome, int? ignorar)
        {
            using var comando = new MySqlCommand(
                "SELECT COUNT(*) FROM usuario WHERE nome_usr = @nome AND id_usr <> @ignorar",
                conexao, transacao);

            comando.Parameters.AddWithValue("@nome", nome);
            comando.Parameters.AddWithValue("@ignorar", ignorar ?? 0);

            return Convert.ToInt32(comando.ExecuteScalar()) > 0;
        }

        private static Dictionary<string, Acao> LerMatriz(
            MySqlConnection conexao, MySqlTransaction transacao, int id)
        {
            var mapa = new Dictionary<string, Acao>(StringComparer.OrdinalIgnoreCase);
            var colunas = string.Join(", ", Catalogo.Acoes.Select(Catalogo.Coluna));

            using var comando = new MySqlCommand(
                $"SELECT tela, {colunas} FROM permissao WHERE id_usr_fk = @id",
                conexao, transacao);

            comando.Parameters.AddWithValue("@id", id);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                var acoes = Acao.Nenhuma;

                foreach (var acao in Catalogo.Acoes)
                {
                    if (leitor.GetBoolean(Catalogo.Coluna(acao)))
                        acoes |= acao;
                }

                mapa[leitor.GetString("tela")] = acoes;
            }

            return mapa;
        }

        /// <summary>
        /// Espacos aparados e tudo em minusculas: o indice unico nao
        /// diferencia maiusculas, entao "Lucas" e "lucas" sao o mesmo
        /// login -- gravar sempre igual evita a confusao de ver um nome na
        /// lista e digitar outro para entrar.
        /// </summary>
        private static string Normalizar(string? nome) =>
            (nome ?? "").Trim().ToLowerInvariant();

        private static object Texto(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

        private static object Numero(int? valor) =>
            valor.HasValue && valor.Value > 0 ? valor.Value : DBNull.Value;

        private static string Vazio(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? "—" : valor;
    }
}
