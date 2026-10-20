using System.Reflection;
using LucasAguiar.Configs;
using MySqlConnector;

namespace LucasAguiar.Data
{
    /// <summary>
    /// Prepara o banco na primeira subida.
    ///
    /// O projeto nao usa EF Core, entao nao ha migrations que criem o
    /// esquema sozinhas. Sem isto, publicar significa abrir um cliente
    /// MySQL e rodar um script de 17 tabelas na mao antes do primeiro
    /// acesso -- e depois lembrar de inserir o usuario de login, sem o
    /// qual ninguem entra.
    ///
    /// Nada aqui apaga ou altera o que ja existe: as duas etapas so
    /// rodam quando encontram o banco vazio.
    /// </summary>
    public class BancoInicializador
    {
        private readonly Conexao _conexao;
        private readonly ILogger<BancoInicializador> _log;

        public BancoInicializador(Conexao conexao, ILogger<BancoInicializador> log)
        {
            _conexao = conexao;
            _log = log;
        }

        public void Preparar()
        {
            try
            {
                using var conexao = _conexao.GetConnection();

                if (!TabelasExistem(conexao))
                {
                    CriarEsquema(conexao);
                }

                CriarPrimeiroUsuario(conexao);

                // Depois do login: se falhar, nao impede o primeiro acesso.
                CriarTabelasNovas(conexao);

                MigrarAcessos(conexao);

                MigrarCadastros(conexao);

                MigrarAtributos(conexao);
            }
            catch (Exception ex)
            {
                // Falhar aqui nao pode derrubar a aplicacao: se o banco
                // estiver fora do ar, e melhor subir e mostrar o erro na
                // tela do que o container nem iniciar e esconder a causa.
                _log.LogError(ex,
                    "Não foi possível preparar o banco de dados. Tentativa em: {Onde}. "
                    + "Confira a variável ConnectionStrings__MySqlConnection.",
                    _conexao.Descricao);
            }
        }

        /// <summary>A tabela de login e a prova de que o esquema ja rodou.</summary>
        private static bool TabelasExistem(MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.tables
                  WHERE table_schema = DATABASE() AND table_name = 'usuario'", conexao);

            return Convert.ToInt32(comando.ExecuteScalar()) > 0;
        }

        private void CriarEsquema(MySqlConnection conexao)
        {
            var script = LerScript();
            if (string.IsNullOrWhiteSpace(script))
            {
                _log.LogError("O script do esquema não foi encontrado no assembly.");
                return;
            }

            using (var comando = new MySqlCommand(script, conexao))
            {
                comando.CommandTimeout = 120;
                comando.ExecuteNonQuery();
            }

            _log.LogWarning("Banco vazio: esquema criado a partir de banco_dados.sql.");
        }

        /// <summary>
        /// Tabelas que chegaram depois do primeiro deploy. O script completo
        /// so roda com o banco vazio, entao um banco que ja esta em uso nunca
        /// as receberia por ele. IF NOT EXISTS deixa rodar a cada subida.
        /// </summary>
        private void CriarTabelasNovas(MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(@"
                CREATE TABLE IF NOT EXISTS `agendamento` (
                  `id_agend` int NOT NULL AUTO_INCREMENT,
                  `data_hora` datetime NOT NULL,
                  `duracao_min` int NOT NULL DEFAULT '30',
                  `id_cli_fk` int NOT NULL,
                  `id_fun_fk` int NOT NULL,
                  `id_serv_fk` int NOT NULL,
                  `observacao` varchar(300) DEFAULT NULL,
                  `status` varchar(20) NOT NULL DEFAULT 'AGENDADO',
                  `id_vend_fk` int DEFAULT NULL,
                  `data_criacao` datetime DEFAULT CURRENT_TIMESTAMP,
                  PRIMARY KEY (`id_agend`),
                  KEY `idx_agendamento_data` (`data_hora`),
                  KEY `idx_agendamento_funcionario` (`id_fun_fk`,`data_hora`),
                  KEY `fk_agendamento_cliente` (`id_cli_fk`),
                  KEY `fk_agendamento_servico` (`id_serv_fk`),
                  KEY `fk_agendamento_venda` (`id_vend_fk`),
                  CONSTRAINT `fk_agendamento_cliente` FOREIGN KEY (`id_cli_fk`) REFERENCES `cliente` (`id_cli`),
                  CONSTRAINT `fk_agendamento_funcionario` FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`),
                  CONSTRAINT `fk_agendamento_servico` FOREIGN KEY (`id_serv_fk`) REFERENCES `servico` (`id_serv`),
                  CONSTRAINT `fk_agendamento_venda` FOREIGN KEY (`id_vend_fk`) REFERENCES `venda` (`id_vend`) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;", conexao);

            comando.ExecuteNonQuery();
        }

        // =============================================================
        // EQUIPE, ACESSOS E PERMISSOES
        //
        // Tudo aqui e aditivo e roda a cada subida: nenhuma coluna
        // existente muda de tipo, nada e apagado e rodar duas vezes nao
        // tem efeito diferente de rodar uma.
        //
        // O MySQL nao aceita ADD COLUMN IF NOT EXISTS nem CREATE INDEX IF
        // NOT EXISTS (isso e MariaDB), entao a existencia e conferida no
        // information_schema antes de cada ALTER.
        // =============================================================

        private void MigrarAcessos(MySqlConnection conexao)
        {
            // A funcao vem antes da coluna que aponta para ela.
            CriarTabela(conexao, "funcao", @"
                CREATE TABLE IF NOT EXISTS `funcao` (
                  `id_funcao` int NOT NULL AUTO_INCREMENT,
                  `nome_funcao` varchar(60) NOT NULL,
                  `criado_em` datetime DEFAULT CURRENT_TIMESTAMP,
                  PRIMARY KEY (`id_funcao`),
                  UNIQUE KEY `uk_funcao_nome` (`nome_funcao`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- usuario ----------
            // O nome de verdade da pessoa. A tabela so tinha nome_usr, que
            // e o login: a lista e a lateral mostravam "lucasaguiar" onde
            // devia aparecer "Lucas Aguiar".
            GarantirColuna(conexao, "usuario", "nome_exib_usr",
                "varchar(100) DEFAULT NULL");
            GarantirColuna(conexao, "usuario", "perfil_usr",
                "varchar(10) NOT NULL DEFAULT 'COMUM'");
            GarantirColuna(conexao, "usuario", "raiz_usr",
                "tinyint(1) NOT NULL DEFAULT 0");
            GarantirColuna(conexao, "usuario", "trocar_senha_usr",
                "tinyint(1) NOT NULL DEFAULT 0");
            // O login le esta coluna para saber como conferir a senha
            // gravada. Quem ja existe continua em sha256 e e promovido a
            // pbkdf2 na primeira entrada, sem perder o acesso.
            GarantirColuna(conexao, "usuario", "algo_senha_usr",
                "varchar(12) NOT NULL DEFAULT 'sha256'");
            GarantirColuna(conexao, "usuario", "id_funcao_fk", "int DEFAULT NULL");
            // Liga o acesso ao profissional da barbearia: a comissao e o
            // login passam a ser a mesma pessoa.
            GarantirColuna(conexao, "usuario", "id_fun_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "usuario", "ultimo_login_usr", "datetime DEFAULT NULL");
            // Comparado no salvar para avisar de edicao concorrente em vez
            // de sobrescrever o que outra pessoa acabou de mudar.
            GarantirColuna(conexao, "usuario", "atualizado_usr",
                "datetime DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

            GarantirIndiceUnico(conexao, "usuario", "uk_usuario_nome", "nome_usr");
            GarantirChave(conexao, "usuario", "fk_usuario_funcao",
                "FOREIGN KEY (`id_funcao_fk`) REFERENCES `funcao` (`id_funcao`) ON DELETE SET NULL");
            GarantirChave(conexao, "usuario", "fk_usuario_funcionario",
                "FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`) ON DELETE SET NULL");

            // ---------- sessao ----------
            // Guarda o hash do token, nunca o token: um vazamento da tabela
            // nao permite entrar como ninguem. Ter a sessao no banco e o que
            // torna possivel derrubar o acesso de alguem na hora.
            CriarTabela(conexao, "sessao", @"
                CREATE TABLE IF NOT EXISTS `sessao` (
                  `id_sessao` int NOT NULL AUTO_INCREMENT,
                  `token_hash` char(64) NOT NULL,
                  `id_usr_fk` int NOT NULL,
                  `criado_em` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  `expira_em` datetime NOT NULL,
                  `encerrada_em` datetime DEFAULT NULL,
                  `ip` varchar(45) DEFAULT NULL,
                  `agente` varchar(255) DEFAULT NULL,
                  PRIMARY KEY (`id_sessao`),
                  UNIQUE KEY `uk_sessao_token` (`token_hash`),
                  KEY `idx_sessao_usuario` (`id_usr_fk`,`encerrada_em`),
                  CONSTRAINT `fk_sessao_usuario` FOREIGN KEY (`id_usr_fk`)
                      REFERENCES `usuario` (`id_usr`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- permissao ----------
            // Uma linha por usuario e tela. Apagar o usuario leva as
            // permissoes dele: nao e dado de negocio, e configuracao.
            CriarTabela(conexao, "permissao", @"
                CREATE TABLE IF NOT EXISTS `permissao` (
                  `id_usr_fk` int NOT NULL,
                  `tela` varchar(30) NOT NULL,
                  `ver` tinyint(1) NOT NULL DEFAULT 0,
                  `criar` tinyint(1) NOT NULL DEFAULT 0,
                  `editar` tinyint(1) NOT NULL DEFAULT 0,
                  `excluir` tinyint(1) NOT NULL DEFAULT 0,
                  `exportar` tinyint(1) NOT NULL DEFAULT 0,
                  `estornar` tinyint(1) NOT NULL DEFAULT 0,
                  `liquidar` tinyint(1) NOT NULL DEFAULT 0,
                  `ver_valores` tinyint(1) NOT NULL DEFAULT 0,
                  PRIMARY KEY (`id_usr_fk`,`tela`),
                  CONSTRAINT `fk_permissao_usuario` FOREIGN KEY (`id_usr_fk`)
                      REFERENCES `usuario` (`id_usr`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- log_sistema ----------
            // nome_gravado guarda o nome de quem agiu no momento da acao:
            // o log sobrevive a exclusao do usuario, e por isso a chave e
            // SET NULL e nao CASCADE.
            CriarTabela(conexao, "log_sistema", @"
                CREATE TABLE IF NOT EXISTS `log_sistema` (
                  `id_log` int NOT NULL AUTO_INCREMENT,
                  `quando` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  `tipo` varchar(12) NOT NULL DEFAULT 'sistema',
                  `nivel` varchar(6) NOT NULL DEFAULT 'info',
                  `acao` varchar(40) NOT NULL,
                  `descricao` varchar(400) DEFAULT NULL,
                  `id_usr_fk` int DEFAULT NULL,
                  `nome_gravado` varchar(100) DEFAULT NULL,
                  `ip` varchar(45) DEFAULT NULL,
                  `alvo` varchar(120) DEFAULT NULL,
                  `meta` json DEFAULT NULL,
                  PRIMARY KEY (`id_log`),
                  KEY `idx_log_quando` (`quando`),
                  KEY `idx_log_tipo_nivel` (`tipo`,`nivel`),
                  CONSTRAINT `fk_log_usuario` FOREIGN KEY (`id_usr_fk`)
                      REFERENCES `usuario` (`id_usr`) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- tentativa_login ----------
            // No banco, e nao em memoria: reiniciar a aplicacao nao pode
            // zerar o bloqueio de quem esta tentando adivinhar senha.
            CriarTabela(conexao, "tentativa_login", @"
                CREATE TABLE IF NOT EXISTS `tentativa_login` (
                  `chave` varchar(190) NOT NULL,
                  `contagem` int NOT NULL DEFAULT 0,
                  `bloqueado_ate` datetime DEFAULT NULL,
                  `atualizado_em` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP
                      ON UPDATE CURRENT_TIMESTAMP,
                  PRIMARY KEY (`chave`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            MigrarEstoque(conexao);

            PromoverPrimeiroAdmin(conexao);
            SincronizarPermissoes(conexao);
        }

        /// <summary>
        /// Movimentacao de estoque.
        ///
        /// Ate aqui o sistema so guardava o saldo em produto.quantidade_prod
        /// e o alterava em quatro lugares -- compra, venda, cancelamento de
        /// venda e edicao do produto -- sem registrar nada. Quando o numero
        /// nao batia com a prateleira, nao havia como descobrir onde tinha
        /// se perdido.
        ///
        /// O saldo continua em produto, porque e ele que as telas de venda
        /// e de produto leem; o que muda e que toda alteracao passa a
        /// deixar uma linha aqui, com o saldo que ficou depois dela.
        /// </summary>
        private void MigrarEstoque(MySqlConnection conexao)
        {
            GarantirColuna(conexao, "produto", "estoque_min_prod",
                "int NOT NULL DEFAULT 0");

            CriarTabela(conexao, "movimento_estoque", @"
                CREATE TABLE IF NOT EXISTS `movimento_estoque` (
                  `id_mov_est` int NOT NULL AUTO_INCREMENT,
                  `id_prod_fk` int NOT NULL,
                  `data_hora` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  `tipo` varchar(12) NOT NULL,
                  `quantidade` int NOT NULL,
                  `saldo_apos` int NOT NULL,
                  `motivo` varchar(200) DEFAULT NULL,
                  `id_ref` int DEFAULT NULL,
                  `usuario` varchar(100) DEFAULT NULL,
                  PRIMARY KEY (`id_mov_est`),
                  KEY `idx_mov_est_produto` (`id_prod_fk`,`data_hora`),
                  KEY `idx_mov_est_data` (`data_hora`),
                  CONSTRAINT `fk_mov_est_produto` FOREIGN KEY (`id_prod_fk`)
                      REFERENCES `produto` (`id_prod`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            AbrirSaldoInicial(conexao);
        }

        /// <summary>
        /// Primeira linha de cada produto: o saldo que ele ja tinha quando
        /// a movimentacao passou a existir.
        ///
        /// Sem isto o extrato comecaria do nada e o saldo de hoje pareceria
        /// ter surgido sozinho. So roda uma vez por produto -- quem ja tem
        /// movimento nao recebe outro.
        /// </summary>
        private void AbrirSaldoInicial(MySqlConnection conexao)
        {
            using var comando = new MySqlCommand(@"
                INSERT INTO movimento_estoque
                    (id_prod_fk, data_hora, tipo, quantidade, saldo_apos, motivo, usuario)
                SELECT p.id_prod, NOW(), 'ABERTURA',
                       COALESCE(p.quantidade_prod, 0), COALESCE(p.quantidade_prod, 0),
                       'Saldo existente quando a movimentação passou a ser registrada',
                       'Sistema'
                  FROM produto p
                 WHERE NOT EXISTS (
                       SELECT 1 FROM movimento_estoque m
                        WHERE m.id_prod_fk = p.id_prod)", conexao);

            var criados = comando.ExecuteNonQuery();

            if (criados > 0)
                _log.LogWarning("Estoque: saldo de abertura criado para {Quantos} produto(s).", criados);
        }

        /// <summary>
        /// Depois de criar perfil_usr, todo mundo nasce COMUM -- inclusive
        /// quem era o unico acesso do sistema, que ficaria sem nenhuma tela.
        /// Com a tabela sem nenhum administrador, o acesso mais antigo e
        /// ativo e promovido a administrador e a raiz.
        ///
        /// So acontece quando nao existe admin: depois disso quem concede o
        /// perfil e a raiz, nunca o codigo.
        /// </summary>
        private void PromoverPrimeiroAdmin(MySqlConnection conexao)
        {
            using (var conta = new MySqlCommand(
                "SELECT COUNT(*) FROM usuario WHERE perfil_usr = 'ADMIN'", conexao))
            {
                if (Convert.ToInt32(conta.ExecuteScalar()) > 0) return;
            }

            using var comando = new MySqlCommand(
                @"UPDATE usuario
                     SET perfil_usr = 'ADMIN', raiz_usr = 1
                   WHERE ativo_usr = 1
                   ORDER BY id_usr
                   LIMIT 1", conexao);

            if (comando.ExecuteNonQuery() > 0)
            {
                _log.LogWarning(
                    "Nenhum administrador cadastrado: o acesso mais antigo foi "
                    + "promovido a administrador e super-administrador.");
            }
        }

        /// <summary>
        /// Garante uma linha de permissao para cada par (usuario comum,
        /// tela do catalogo). As colunas ficam no default, que e zero:
        /// uma tela nova nasce negada, e a matriz nunca aparece incompleta
        /// na tela de edicao do membro.
        ///
        /// Administrador nao entra: para ele a matriz e ignorada.
        /// </summary>
        private void SincronizarPermissoes(MySqlConnection conexao)
        {
            var criadas = 0;

            foreach (var tela in Catalogo.Telas)
            {
                using var comando = new MySqlCommand(
                    @"INSERT INTO permissao (id_usr_fk, tela)
                      SELECT u.id_usr, @tela
                        FROM usuario u
                       WHERE u.perfil_usr <> 'ADMIN'
                         AND NOT EXISTS (
                             SELECT 1 FROM permissao p
                              WHERE p.id_usr_fk = u.id_usr AND p.tela = @tela)",
                    conexao);

                comando.Parameters.AddWithValue("@tela", tela.Chave);
                criadas += comando.ExecuteNonQuery();
            }

            if (criadas > 0)
            {
                _log.LogWarning(
                    "Sincronização de permissões: {Quantas} linha(s) criada(s), "
                    + "todas negadas.", criadas);
            }
        }

        // =============================================================
        // CADASTROS
        //
        // Campos que os formularios padronizados pedem. Mesma regra das
        // migracoes de acesso: so acrescenta, e rodar duas vezes nao muda
        // nada.
        // =============================================================

        private void MigrarCadastros(MySqlConnection conexao)
        {
            // CEP antes do endereco, com preenchimento automatico.
            GarantirColuna(conexao, "cliente", "cep_cli", "varchar(9) DEFAULT NULL AFTER `rg_cli`");

            // "(69) 99999-9999" tem 15 caracteres e a coluna era de 14: o
            // celular com mascara nao cabia.
            GarantirTamanho(conexao, "cliente", "telefone_cli", 20, "varchar(20) DEFAULT NULL");

            GarantirColuna(conexao, "fornecedor", "documento_forn", "varchar(18) DEFAULT NULL AFTER `nome_forn`");
            GarantirTamanho(conexao, "fornecedor", "telefone_forn", 20, "varchar(20) DEFAULT NULL");

            GarantirColuna(conexao, "funcionario", "cep_fun", "varchar(9) DEFAULT NULL AFTER `email_fun`");
            GarantirTamanho(conexao, "funcionario", "telefone_fun", 20, "varchar(20) DEFAULT NULL");
        }

        // =============================================================
        // ATRIBUTOS NOVOS DOS CADASTROS E MOVIMENTOS
        //
        // Mesma regra: so acrescenta. Os preenchimentos (UPDATE/INSERT ...
        // SELECT) so tocam linhas que ainda nao foram migradas, entao rodar
        // de novo nao muda nada.
        // =============================================================

        private void MigrarAtributos(MySqlConnection conexao)
        {
            // ---------- categorias, uma tabela por tipo ----------
            CriarTabela(conexao, "categoria_servico", @"
                CREATE TABLE IF NOT EXISTS `categoria_servico` (
                  `id_cat_serv` int NOT NULL AUTO_INCREMENT,
                  `nome_cat` varchar(60) NOT NULL,
                  `ativo_cat` tinyint(1) NOT NULL DEFAULT 1,
                  PRIMARY KEY (`id_cat_serv`),
                  UNIQUE KEY `uk_categoria_servico_nome` (`nome_cat`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            CriarTabela(conexao, "categoria_produto", @"
                CREATE TABLE IF NOT EXISTS `categoria_produto` (
                  `id_cat_prod` int NOT NULL AUTO_INCREMENT,
                  `nome_cat` varchar(60) NOT NULL,
                  `ativo_cat` tinyint(1) NOT NULL DEFAULT 1,
                  PRIMARY KEY (`id_cat_prod`),
                  UNIQUE KEY `uk_categoria_produto_nome` (`nome_cat`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // despesa.categoria guarda o codigo: as despesas antigas continuam
            // validas sem migrar dado, e os filtros por categoria seguem iguais.
            CriarTabela(conexao, "categoria_despesa", @"
                CREATE TABLE IF NOT EXISTS `categoria_despesa` (
                  `id_cat_desp` int NOT NULL AUTO_INCREMENT,
                  `codigo_cat` varchar(40) NOT NULL,
                  `nome_cat` varchar(60) NOT NULL,
                  `ativo_cat` tinyint(1) NOT NULL DEFAULT 1,
                  PRIMARY KEY (`id_cat_desp`),
                  UNIQUE KEY `uk_categoria_despesa_codigo` (`codigo_cat`),
                  UNIQUE KEY `uk_categoria_despesa_nome` (`nome_cat`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            foreach (var (codigo, rotulo) in Models.CategoriaDespesa.Todas)
            {
                using var semear = new MySqlCommand(
                    "INSERT IGNORE INTO categoria_despesa (codigo_cat, nome_cat) VALUES (@c, @n)", conexao);
                semear.Parameters.AddWithValue("@c", codigo);
                semear.Parameters.AddWithValue("@n", rotulo);
                semear.ExecuteNonQuery();
            }

            // ---------- cliente ----------
            GarantirColuna(conexao, "cliente", "email_cli", "varchar(100) DEFAULT NULL");
            GarantirColuna(conexao, "cliente", "origem_cli", "varchar(30) DEFAULT NULL");
            GarantirColuna(conexao, "cliente", "observacoes_cli", "varchar(500) DEFAULT NULL");
            GarantirColuna(conexao, "cliente", "id_fun_pref_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "cliente", "ativo_cli", "tinyint(1) NOT NULL DEFAULT 1");
            GarantirChave(conexao, "cliente", "fk_cliente_profissional_pref",
                "FOREIGN KEY (`id_fun_pref_fk`) REFERENCES `funcionario` (`id_fun`) ON DELETE SET NULL");

            // ---------- funcionario ----------
            GarantirColuna(conexao, "funcionario", "cargo_fun", "varchar(20) DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "comissao_serv_padrao", "decimal(5,2) NOT NULL DEFAULT 0");
            GarantirColuna(conexao, "funcionario", "comissao_prod_padrao", "decimal(5,2) NOT NULL DEFAULT 0");
            GarantirColuna(conexao, "funcionario", "data_admissao", "date DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "data_desligamento", "date DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "chave_pix", "varchar(100) DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "dados_bancarios", "varchar(300) DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "cor_agenda", "varchar(7) DEFAULT NULL");
            GarantirColuna(conexao, "funcionario", "ativo_fun", "tinyint(1) NOT NULL DEFAULT 1");

            // A foto fica fora de funcionario: as listagens fazem SELECT * e
            // carregariam a imagem de todo mundo a cada tela.
            CriarTabela(conexao, "funcionario_foto", @"
                CREATE TABLE IF NOT EXISTS `funcionario_foto` (
                  `id_fun_fk` int NOT NULL,
                  `tipo` varchar(50) NOT NULL,
                  `conteudo` mediumblob NOT NULL,
                  PRIMARY KEY (`id_fun_fk`),
                  CONSTRAINT `fk_funcionario_foto` FOREIGN KEY (`id_fun_fk`)
                      REFERENCES `funcionario` (`id_fun`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // Servicos que o profissional faz, com comissao e preco proprios.
            // Nulo nos dois e "usa o do servico".
            CriarTabela(conexao, "funcionario_servico", @"
                CREATE TABLE IF NOT EXISTS `funcionario_servico` (
                  `id_fun_fk` int NOT NULL,
                  `id_serv_fk` int NOT NULL,
                  `comissao` decimal(10,2) DEFAULT NULL,
                  `preco` decimal(10,2) DEFAULT NULL,
                  PRIMARY KEY (`id_fun_fk`,`id_serv_fk`),
                  KEY `fk_funcionario_servico_servico` (`id_serv_fk`),
                  CONSTRAINT `fk_funcionario_servico_funcionario` FOREIGN KEY (`id_fun_fk`)
                      REFERENCES `funcionario` (`id_fun`) ON DELETE CASCADE,
                  CONSTRAINT `fk_funcionario_servico_servico` FOREIGN KEY (`id_serv_fk`)
                      REFERENCES `servico` (`id_serv`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- servico ----------
            // comis_funcionario_cli continua sendo o valor da comissao; o tipo
            // nasce FIXO porque e assim que ela sempre foi gravada (em R$).
            GarantirColuna(conexao, "servico", "id_cat_serv_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "servico", "tipo_comissao", "varchar(10) NOT NULL DEFAULT 'FIXO'");
            GarantirColuna(conexao, "servico", "custo_estimado", "decimal(10,2) DEFAULT NULL");
            GarantirColuna(conexao, "servico", "ativo_serv", "tinyint(1) NOT NULL DEFAULT 1");
            GarantirChave(conexao, "servico", "fk_servico_categoria",
                "FOREIGN KEY (`id_cat_serv_fk`) REFERENCES `categoria_servico` (`id_cat_serv`) ON DELETE SET NULL");

            // ---------- produto ----------
            // valor_prod e o preco de venda e quantidade_prod o estoque atual.
            GarantirColuna(conexao, "produto", "id_cat_prod_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "produto", "codigo_barras", "varchar(50) DEFAULT NULL");
            GarantirColuna(conexao, "produto", "sku", "varchar(50) DEFAULT NULL");
            GarantirColuna(conexao, "produto", "unidade", "varchar(10) NOT NULL DEFAULT 'UN'");
            GarantirColuna(conexao, "produto", "preco_custo", "decimal(10,2) DEFAULT NULL");
            GarantirColuna(conexao, "produto", "comissao_prod", "decimal(5,2) DEFAULT NULL");
            GarantirColuna(conexao, "produto", "ativo_prod", "tinyint(1) NOT NULL DEFAULT 1");
            GarantirChave(conexao, "produto", "fk_produto_categoria",
                "FOREIGN KEY (`id_cat_prod_fk`) REFERENCES `categoria_produto` (`id_cat_prod`) ON DELETE SET NULL");

            // ---------- plano e assinatura ----------
            GarantirColuna(conexao, "plano", "periodicidade", "varchar(12) NOT NULL DEFAULT 'MENSAL'");
            GarantirColuna(conexao, "plano", "regra_comissao_assinante", "varchar(12) NOT NULL DEFAULT 'NORMAL'");
            GarantirColuna(conexao, "plano", "ativo_plan", "tinyint(1) NOT NULL DEFAULT 1");
            // Quantidade nula e item ilimitado no plano e no credito.
            GarantirNulo(conexao, "plano_item", "quantidade", "int DEFAULT NULL");
            GarantirNulo(conexao, "assinatura_saldo", "quantidade_total", "int DEFAULT NULL");
            // data_fim continua sendo o vencimento.
            GarantirColuna(conexao, "assinatura", "dia_cobranca", "tinyint DEFAULT NULL");

            // ---------- fornecedor ----------
            // nome_forn e o nome/razao social; documento_forn o CPF/CNPJ.
            GarantirColuna(conexao, "fornecedor", "tipo_pessoa", "char(2) NOT NULL DEFAULT 'PJ'");
            GarantirColuna(conexao, "fornecedor", "nome_fantasia", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "contato_forn", "varchar(100) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "cep_forn", "varchar(9) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "estado_forn", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "cidade_forn", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "bairro_forn", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "rua_forn", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "numero_forn", "varchar(200) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "observacoes_forn", "varchar(500) DEFAULT NULL");
            GarantirColuna(conexao, "fornecedor", "ativo_forn", "tinyint(1) NOT NULL DEFAULT 1");

            // ---------- agendamento: varios servicos ----------
            // agendamento.id_serv_fk continua com o primeiro servico, para o
            // que ja le esta coluna (relatorios, exportacao) seguir valendo.
            CriarTabela(conexao, "agendamento_servico", @"
                CREATE TABLE IF NOT EXISTS `agendamento_servico` (
                  `id_agend_fk` int NOT NULL,
                  `id_serv_fk` int NOT NULL,
                  `ordem` int NOT NULL DEFAULT 0,
                  PRIMARY KEY (`id_agend_fk`,`id_serv_fk`),
                  KEY `fk_agendamento_servico_servico` (`id_serv_fk`),
                  CONSTRAINT `fk_agendamento_servico_agendamento` FOREIGN KEY (`id_agend_fk`)
                      REFERENCES `agendamento` (`id_agend`) ON DELETE CASCADE,
                  CONSTRAINT `fk_agendamento_servico_servico` FOREIGN KEY (`id_serv_fk`)
                      REFERENCES `servico` (`id_serv`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            Executar(conexao, "agendamento_servico preenchido", @"
                INSERT INTO agendamento_servico (id_agend_fk, id_serv_fk, ordem)
                SELECT a.id_agend, a.id_serv_fk, 0
                  FROM agendamento a
                 WHERE NOT EXISTS (SELECT 1 FROM agendamento_servico s WHERE s.id_agend_fk = a.id_agend)");

            // ---------- venda: profissional e desconto por item ----------
            GarantirColuna(conexao, "venda_item", "id_fun_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "venda_item", "desconto", "decimal(10,2) NOT NULL DEFAULT 0");
            GarantirChave(conexao, "venda_item", "fk_venda_item_funcionario",
                "FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`)");

            // Itens antigos ficam com o profissional da venda: a comissao
            // passa a ser apurada por item e nao pode mudar de dono.
            Executar(conexao, "venda_item.id_fun_fk preenchido", @"
                UPDATE venda_item vi
                  JOIN venda v ON v.id_vend = vi.id_vend_fk
                   SET vi.id_fun_fk = v.id_fun_fk
                 WHERE vi.id_fun_fk IS NULL AND v.id_fun_fk IS NOT NULL");

            // ---------- venda: grade de pagamentos ----------
            CriarTabela(conexao, "venda_pagamento", @"
                CREATE TABLE IF NOT EXISTS `venda_pagamento` (
                  `id_vend_pag` int NOT NULL AUTO_INCREMENT,
                  `id_vend_fk` int NOT NULL,
                  `forma` varchar(20) NOT NULL,
                  `valor` decimal(10,2) NOT NULL,
                  `parcelas` int DEFAULT NULL,
                  PRIMARY KEY (`id_vend_pag`),
                  KEY `idx_venda_pagamento_venda` (`id_vend_fk`),
                  CONSTRAINT `fk_venda_pagamento_venda` FOREIGN KEY (`id_vend_fk`)
                      REFERENCES `venda` (`id_vend`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // O caixa passa a somar pelos pagamentos: cada venda antiga vira
            // um pagamento unico com a forma e o valor que ja tinha.
            Executar(conexao, "venda_pagamento preenchido", @"
                INSERT INTO venda_pagamento (id_vend_fk, forma, valor, parcelas)
                SELECT v.id_vend, v.forma_pagamento_vend, COALESCE(v.valor_vend, 0), v.quant_parcela_vend
                  FROM venda v
                 WHERE v.forma_pagamento_vend IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM venda_pagamento p WHERE p.id_vend_fk = v.id_vend)");

            // ---------- caixa ----------
            // Sangria e suprimento antigos eram todos em dinheiro.
            GarantirColuna(conexao, "movimento_caixa", "forma_pagamento",
                "varchar(20) NOT NULL DEFAULT 'DINHEIRO'");

            // ---------- despesa ----------
            GarantirColuna(conexao, "despesa", "id_fun_fk", "int DEFAULT NULL");
            GarantirColuna(conexao, "despesa", "data_vencimento", "date DEFAULT NULL");
            GarantirColuna(conexao, "despesa", "data_pagamento", "date DEFAULT NULL");
            GarantirColuna(conexao, "despesa", "parcelas", "int NOT NULL DEFAULT 1");
            GarantirColuna(conexao, "despesa", "recorrente", "tinyint(1) NOT NULL DEFAULT 0");
            GarantirChave(conexao, "despesa", "fk_despesa_funcionario",
                "FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`) ON DELETE SET NULL");

            // data_desp era o vencimento da despesa em aberto e o dia do
            // pagamento da paga; as colunas novas nascem com esse valor.
            Executar(conexao, "despesa.data_vencimento preenchida",
                "UPDATE despesa SET data_vencimento = data_desp WHERE data_vencimento IS NULL");
            Executar(conexao, "despesa.data_pagamento preenchida",
                "UPDATE despesa SET data_pagamento = data_desp WHERE pago = 1 AND data_pagamento IS NULL");

            CriarTabela(conexao, "despesa_anexo", @"
                CREATE TABLE IF NOT EXISTS `despesa_anexo` (
                  `id_anexo` int NOT NULL AUTO_INCREMENT,
                  `id_desp_fk` int NOT NULL,
                  `nome` varchar(200) NOT NULL,
                  `tipo` varchar(100) NOT NULL,
                  `tamanho` int NOT NULL,
                  `conteudo` mediumblob NOT NULL,
                  `criado_em` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  PRIMARY KEY (`id_anexo`),
                  KEY `idx_despesa_anexo_despesa` (`id_desp_fk`),
                  CONSTRAINT `fk_despesa_anexo_despesa` FOREIGN KEY (`id_desp_fk`)
                      REFERENCES `despesa` (`id_desp`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // ---------- compras: cabecalho + itens ----------
            // compras vira o cabecalho da nota; item_comp, quantidade e
            // id_prod_fk ficam para as compras antigas e para a listagem.
            GarantirColuna(conexao, "compras", "numero_nota", "varchar(30) DEFAULT NULL");
            GarantirColuna(conexao, "compras", "frete", "decimal(10,2) NOT NULL DEFAULT 0");
            GarantirColuna(conexao, "compras", "desconto", "decimal(10,2) NOT NULL DEFAULT 0");
            GarantirColuna(conexao, "compras", "observacao", "varchar(300) DEFAULT NULL");
            GarantirColuna(conexao, "compras", "forma_pagamento", "varchar(20) DEFAULT NULL");
            GarantirColuna(conexao, "compras", "parcelas", "int NOT NULL DEFAULT 1");
            GarantirColuna(conexao, "compras", "vencimento_primeira", "date DEFAULT NULL");

            // Compra aguardando entrega nao da entrada no estoque ate ser recebida.
            // DEFAULT 1: as compras antigas ja entraram no estoque ao serem lancadas.
            GarantirColuna(conexao, "compras", "recebida", "tinyint(1) NOT NULL DEFAULT 1");
            GarantirColuna(conexao, "compras", "data_recebimento", "date DEFAULT NULL");

            CriarTabela(conexao, "compra_item", @"
                CREATE TABLE IF NOT EXISTS `compra_item` (
                  `id_comp_item` int NOT NULL AUTO_INCREMENT,
                  `id_comp_fk` int NOT NULL,
                  `id_prod_fk` int DEFAULT NULL,
                  `descricao` varchar(200) NOT NULL,
                  `quantidade` int NOT NULL,
                  `custo_unit` decimal(10,2) NOT NULL,
                  PRIMARY KEY (`id_comp_item`),
                  KEY `idx_compra_item_compra` (`id_comp_fk`),
                  KEY `fk_compra_item_produto` (`id_prod_fk`),
                  CONSTRAINT `fk_compra_item_compra` FOREIGN KEY (`id_comp_fk`)
                      REFERENCES `compras` (`id_comp`) ON DELETE CASCADE,
                  CONSTRAINT `fk_compra_item_produto` FOREIGN KEY (`id_prod_fk`)
                      REFERENCES `produto` (`id_prod`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

            // Cada compra antiga era um item so: vira a primeira linha da grade.
            Executar(conexao, "compra_item preenchido", @"
                INSERT INTO compra_item (id_comp_fk, id_prod_fk, descricao, quantidade, custo_unit)
                SELECT c.id_comp, c.id_prod_fk,
                       COALESCE(NULLIF(TRIM(c.item_comp), ''), p.nome_prod, 'Item da compra'),
                       GREATEST(COALESCE(c.quantidade, 1), 1),
                       ROUND(COALESCE(c.valor_comp, 0) / GREATEST(COALESCE(c.quantidade, 1), 1), 2)
                  FROM compras c
                  LEFT JOIN produto p ON p.id_prod = c.id_prod_fk
                 WHERE NOT EXISTS (SELECT 1 FROM compra_item i WHERE i.id_comp_fk = c.id_comp)");

            Executar(conexao, "compras.forma_pagamento preenchida", @"
                UPDATE compras c
                  JOIN despesa d ON d.id_desp = c.id_desp_fk
                   SET c.forma_pagamento = d.forma_pagamento
                 WHERE c.forma_pagamento IS NULL");
        }

        // ---------- apoio das migracoes ----------

        /// <summary>Roda um preenchimento e so registra no log se mexeu em alguma linha.</summary>
        private void Executar(MySqlConnection conexao, string oQue, string sql)
        {
            using var comando = new MySqlCommand(sql, conexao);
            var linhas = comando.ExecuteNonQuery();
            if (linhas > 0)
                _log.LogWarning("Migração: {OQue} ({Linhas} linha(s)).", oQue, linhas);
        }

        /// <summary>
        /// Libera NULL numa coluna que era NOT NULL. So age se ela ainda for
        /// NOT NULL; os valores existentes ficam como estao.
        /// </summary>
        private void GarantirNulo(
            MySqlConnection conexao, string tabela, string coluna, string definicao)
        {
            using (var atual = new MySqlCommand(
                @"SELECT is_nullable FROM information_schema.columns
                   WHERE table_schema = DATABASE()
                     AND table_name = @t AND column_name = @c", conexao))
            {
                atual.Parameters.AddWithValue("@t", tabela);
                atual.Parameters.AddWithValue("@c", coluna);

                if (atual.ExecuteScalar() is not string nulo || nulo == "YES") return;
            }

            using var alterar = new MySqlCommand(
                $"ALTER TABLE `{tabela}` MODIFY COLUMN `{coluna}` {definicao}", conexao);

            alterar.ExecuteNonQuery();
            _log.LogWarning("Coluna {Tabela}.{Coluna} passou a aceitar nulo.", tabela, coluna);
        }

        /// <summary>
        /// Alarga um varchar que ficou curto. So aumenta: se a coluna ja
        /// tiver o tamanho pedido ou mais, nao faz nada.
        /// </summary>
        private void GarantirTamanho(
            MySqlConnection conexao, string tabela, string coluna, int tamanho, string definicao)
        {
            using (var atual = new MySqlCommand(
                @"SELECT character_maximum_length FROM information_schema.columns
                   WHERE table_schema = DATABASE()
                     AND table_name = @t AND column_name = @c", conexao))
            {
                atual.Parameters.AddWithValue("@t", tabela);
                atual.Parameters.AddWithValue("@c", coluna);

                var valor = atual.ExecuteScalar();
                if (valor == null || valor == DBNull.Value || Convert.ToInt64(valor) >= tamanho) return;
            }

            using var alterar = new MySqlCommand(
                $"ALTER TABLE `{tabela}` MODIFY COLUMN `{coluna}` {definicao}", conexao);

            alterar.ExecuteNonQuery();
            _log.LogWarning("Coluna {Tabela}.{Coluna} ampliada para {Tamanho}.", tabela, coluna, tamanho);
        }

        private void CriarTabela(MySqlConnection conexao, string nome, string ddl)
        {
            using var comando = new MySqlCommand(ddl, conexao);
            comando.ExecuteNonQuery();

            if (!TabelaExiste(conexao, nome))
                _log.LogError("A tabela {Tabela} não foi criada.", nome);
        }

        private static bool TabelaExiste(MySqlConnection conexao, string tabela)
        {
            using var comando = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.tables
                   WHERE table_schema = DATABASE() AND table_name = @t", conexao);

            comando.Parameters.AddWithValue("@t", tabela);
            return Convert.ToInt32(comando.ExecuteScalar()) > 0;
        }

        private void GarantirColuna(
            MySqlConnection conexao, string tabela, string coluna, string definicao)
        {
            using (var existe = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.columns
                   WHERE table_schema = DATABASE()
                     AND table_name = @t AND column_name = @c", conexao))
            {
                existe.Parameters.AddWithValue("@t", tabela);
                existe.Parameters.AddWithValue("@c", coluna);

                if (Convert.ToInt32(existe.ExecuteScalar()) > 0) return;
            }

            // Nome de tabela e coluna vem do codigo, nunca de entrada do
            // usuario: nao ha como parametrizar identificador em DDL.
            using var alterar = new MySqlCommand(
                $"ALTER TABLE `{tabela}` ADD COLUMN `{coluna}` {definicao}", conexao);

            alterar.ExecuteNonQuery();
            _log.LogWarning("Coluna {Tabela}.{Coluna} criada.", tabela, coluna);
        }

        /// <summary>
        /// O login e por nome_usr, mas a tabela original so tem restricao em
        /// email_usr: dois acessos podiam nascer com o mesmo login. O indice
        /// fecha isso -- e, se o banco ja tiver nomes repetidos, avisa em vez
        /// de estourar, porque resolver a duplicidade e decisao de quem
        /// administra, nao do codigo.
        /// </summary>
        private void GarantirIndiceUnico(
            MySqlConnection conexao, string tabela, string indice, string coluna)
        {
            using (var existe = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.statistics
                   WHERE table_schema = DATABASE()
                     AND table_name = @t AND index_name = @i", conexao))
            {
                existe.Parameters.AddWithValue("@t", tabela);
                existe.Parameters.AddWithValue("@i", indice);

                if (Convert.ToInt32(existe.ExecuteScalar()) > 0) return;
            }

            using (var repetidos = new MySqlCommand(
                $@"SELECT COUNT(*) FROM (
                       SELECT `{coluna}` FROM `{tabela}`
                        WHERE `{coluna}` IS NOT NULL
                        GROUP BY `{coluna}` HAVING COUNT(*) > 1) d", conexao))
            {
                if (Convert.ToInt32(repetidos.ExecuteScalar()) > 0)
                {
                    _log.LogError(
                        "Não foi possível criar o índice único {Indice}: a coluna "
                        + "{Tabela}.{Coluna} tem valores repetidos. Resolva a "
                        + "duplicidade e reinicie.", indice, tabela, coluna);
                    return;
                }
            }

            using var criar = new MySqlCommand(
                $"CREATE UNIQUE INDEX `{indice}` ON `{tabela}` (`{coluna}`)", conexao);

            criar.ExecuteNonQuery();
            _log.LogWarning("Índice único {Indice} criado em {Tabela}.", indice, tabela);
        }

        private void GarantirChave(
            MySqlConnection conexao, string tabela, string nome, string definicao)
        {
            using (var existe = new MySqlCommand(
                @"SELECT COUNT(*) FROM information_schema.table_constraints
                   WHERE table_schema = DATABASE()
                     AND table_name = @t AND constraint_name = @n", conexao))
            {
                existe.Parameters.AddWithValue("@t", tabela);
                existe.Parameters.AddWithValue("@n", nome);

                if (Convert.ToInt32(existe.ExecuteScalar()) > 0) return;
            }

            using var criar = new MySqlCommand(
                $"ALTER TABLE `{tabela}` ADD CONSTRAINT `{nome}` {definicao}", conexao);

            criar.ExecuteNonQuery();
            _log.LogWarning("Chave estrangeira {Nome} criada em {Tabela}.", nome, tabela);
        }

        /// <summary>
        /// Cria o primeiro login a partir de ADMIN_USUARIO e ADMIN_SENHA.
        /// So acontece com a tabela vazia, entao reiniciar a aplicacao nao
        /// recria nem sobrescreve nada.
        /// </summary>
        private void CriarPrimeiroUsuario(MySqlConnection conexao)
        {
            using (var conta = new MySqlCommand("SELECT COUNT(*) FROM usuario", conexao))
            {
                if (Convert.ToInt32(conta.ExecuteScalar()) > 0) return;
            }

            var nome = Environment.GetEnvironmentVariable("ADMIN_USUARIO");
            var senha = Environment.GetEnvironmentVariable("ADMIN_SENHA");

            if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(senha))
            {
                _log.LogWarning(
                    "Não há usuário cadastrado e ADMIN_USUARIO/ADMIN_SENHA não foram " +
                    "definidas. Defina as duas e reinicie para criar o primeiro acesso.");
                return;
            }

            using var comando = new MySqlCommand(
                @"INSERT INTO usuario
                      (nome_usr, email_usr, senha_usr, ativo_usr, data_criacao_usr)
                  VALUES (@nome, @email, SHA2(@senha, 256), 1, NOW())", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@email",
                Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? $"{nome.Trim()}@local");
            comando.Parameters.AddWithValue("@senha", senha);

            comando.ExecuteNonQuery();

            // A senha nao vai para o log de proposito.
            _log.LogWarning("Primeiro usuário criado: {Nome}.", nome.Trim());
        }

        /// <summary>
        /// O .sql viaja dentro do assembly. Depender do arquivo em disco
        /// quebraria no container, onde so vai o resultado do publish.
        /// </summary>
        private static string LerScript()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var nome = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("banco_dados.sql", StringComparison.OrdinalIgnoreCase));

            if (nome == null) return "";

            using var fluxo = assembly.GetManifestResourceStream(nome)!;
            using var leitor = new StreamReader(fluxo);
            return leitor.ReadToEnd();
        }
    }
}
