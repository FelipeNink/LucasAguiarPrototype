using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class FuncionarioDAO
    {
        private readonly Conexao _conexao;

        public FuncionarioDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        /// <summary>
        /// Cadastro mínimo feito de dentro de outra tela, sem perder o que
        /// já estava preenchido lá. Devolve o id gerado.
        /// </summary>
        public int InserirRapido(string nome, string? telefone = null)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome do profissional.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO funcionario (nome_fun, telefone_fun)
                VALUES (@nome, @telefone);
                SELECT LAST_INSERT_ID();", conexao);

            comando.Parameters.AddWithValue("@nome", nome.Trim());
            comando.Parameters.AddWithValue("@telefone",
                string.IsNullOrWhiteSpace(telefone) ? (object)DBNull.Value : telefone.Trim());

            return Convert.ToInt32(comando.ExecuteScalar());
        }

        /// <summary>Insere e devolve o id gerado.</summary>
        public int Inserir(Funcionario funcionario)
        {
            try
            {
                using (var comando = _conexao.CreateCommand(@"
                        INSERT INTO funcionario (nome_fun, telefone_fun, cpf_fun, data_nasc_fun, ctps_fun, rg_fun, email_fun, cep_fun, estado_fun, cidade_fun, bairro_fun, rua_fun, numero_fun, salario_fixo,
                            cargo_fun, comissao_serv_padrao, comissao_prod_padrao, data_admissao, data_desligamento, chave_pix, dados_bancarios, cor_agenda, ativo_fun,
                            parceiro_fun, cnpj_parceiro_fun, vinculo_fun)
                        VALUES (@_nome, @_telefone, @_cpf, @_dataNasc,@_ctps, @_rg, @_email, @_cep, @_estado, @_cidade, @_bairro, @_rua, @_numero, @_salario,
                            @cargo, @comServ, @comProd, @admissao, @desligamento, @pix, @banco, @cor, @ativo,
                            @parceiro, @cnpjParceiro, @vinculo);
                        SELECT LAST_INSERT_ID();
                "))
                {
                    PreencherNovos(comando, funcionario);
                    comando.Parameters.AddWithValue("@_salario", funcionario.SalarioFixo);
                    comando.Parameters.AddWithValue("@_email", funcionario.Email ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_cep", funcionario.Cep ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_nome", funcionario.NomeFuncionario ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_telefone", funcionario.Telefone ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_cpf", funcionario.CPF ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_dataNasc", funcionario.DataNascimento ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_ctps", funcionario.CTPS ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_rg", funcionario.RG ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_estado", funcionario.Estado ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_cidade", funcionario.Cidade ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_bairro", funcionario.Bairro ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_rua", funcionario.Rua ?? (object)DBNull.Value);
                    comando.Parameters.AddWithValue("@_numero", funcionario.Numero ?? (object)DBNull.Value);

                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao inserir funcionario: " + ex.Message);
            }
        }

        public Funcionario? BuscarPorId(int id)
        {
            return ListarTodos().FirstOrDefault(f => f.IdFuncionario == id);
        }

        public void Atualizar(Funcionario funcionario)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                UPDATE funcionario SET
                    nome_fun = @nome, telefone_fun = @telefone, cpf_fun = @cpf,
                    data_nasc_fun = @dataNasc, ctps_fun = @ctps, rg_fun = @rg,
                    email_fun = @email, cep_fun = @cep, estado_fun = @estado, cidade_fun = @cidade,
                    bairro_fun = @bairro, rua_fun = @rua, numero_fun = @numero,
                    salario_fixo = @salario,
                    cargo_fun = @cargo, comissao_serv_padrao = @comServ, comissao_prod_padrao = @comProd,
                    data_admissao = @admissao, data_desligamento = @desligamento, chave_pix = @pix,
                    dados_bancarios = @banco, cor_agenda = @cor, ativo_fun = @ativo,
                    parceiro_fun = @parceiro, cnpj_parceiro_fun = @cnpjParceiro, vinculo_fun = @vinculo
                WHERE id_fun = @id", conexao);

            PreencherNovos(comando, funcionario);
            comando.Parameters.AddWithValue("@nome", funcionario.NomeFuncionario ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@telefone", funcionario.Telefone ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cpf", funcionario.CPF ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@dataNasc", funcionario.DataNascimento ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@ctps", funcionario.CTPS ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@rg", funcionario.RG ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@email", funcionario.Email ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cep", funcionario.Cep ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@estado", funcionario.Estado ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cidade", funcionario.Cidade ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@bairro", funcionario.Bairro ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@rua", funcionario.Rua ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@numero", funcionario.Numero ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@salario", funcionario.SalarioFixo);
            comando.Parameters.AddWithValue("@id", funcionario.IdFuncionario);

            comando.ExecuteNonQuery();
        }

        public void Excluir(int id)
        {
            try
            {
                using var conexao = _conexao.GetConnection();
                using var comando = new MySqlCommand("DELETE FROM funcionario WHERE id_fun = @id", conexao);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (DAOHelper.EhRegistroEmUso(ex))
            {
                throw new Exception(
                    "Este funcionário tem vendas, compras, folhas ou agendamentos registrados e não pode ser excluído.");
            }
        }

        public List<Funcionario> ListarTodos()
        {
            var lista = new List<Funcionario>();
            using (var comando = _conexao.CreateCommand(@"
                SELECT f.*,
                       EXISTS (SELECT 1 FROM funcionario_foto ff WHERE ff.id_fun_fk = f.id_fun) AS tem_foto,
                       (SELECT u.nome_usr FROM usuario u WHERE u.id_fun_fk = f.id_fun
                         ORDER BY u.id_usr LIMIT 1) AS login_vinculado
                FROM funcionario f"))
            using (var leitor = comando.ExecuteReader())
            {
                while (leitor.Read())
                {
                    string? Texto(string coluna) => DAOHelper.IsNull(leitor, coluna) ? null : leitor.GetString(coluna);

                    var funcionario = new Funcionario
                    {
                        Cargo = Texto("cargo_fun"),
                        ComissaoServicoPadrao = leitor.GetDecimal("comissao_serv_padrao"),
                        ComissaoProdutoPadrao = leitor.GetDecimal("comissao_prod_padrao"),
                        DataAdmissao = DAOHelper.GetDateTime(leitor, "data_admissao"),
                        DataDesligamento = DAOHelper.GetDateTime(leitor, "data_desligamento"),
                        ChavePix = Texto("chave_pix"),
                        DadosBancarios = Texto("dados_bancarios"),
                        CorAgenda = Texto("cor_agenda"),
                        Ativo = leitor.GetBoolean("ativo_fun"),
                        Parceiro = leitor.GetBoolean("parceiro_fun"),
                        CnpjParceiro = Texto("cnpj_parceiro_fun"),
                        Vinculo = Texto("vinculo_fun"),
                        TemFoto = Convert.ToInt32(leitor.GetValue(leitor.GetOrdinal("tem_foto"))) == 1,
                        LoginVinculado = Texto("login_vinculado"),
                        IdFuncionario = leitor.GetInt32("id_fun"),
                        NomeFuncionario = leitor.IsDBNull(leitor.GetOrdinal("nome_fun")) ? null : leitor.GetString("nome_fun"),
                        Telefone = leitor.IsDBNull(leitor.GetOrdinal("telefone_fun")) ? null : leitor.GetString("telefone_fun"),
                        CPF = leitor.IsDBNull(leitor.GetOrdinal("cpf_fun")) ? null : leitor.GetString("cpf_fun"),
                        DataNascimento = leitor.IsDBNull(leitor.GetOrdinal("data_nasc_fun")) ? null : leitor.GetDateTime("data_nasc_fun"),
                        CTPS = leitor.IsDBNull(leitor.GetOrdinal("ctps_fun")) ? null : leitor.GetString("ctps_fun"),
                        RG = leitor.IsDBNull(leitor.GetOrdinal("rg_fun")) ? null : leitor.GetString("rg_fun"),
                        Email = leitor.IsDBNull(leitor.GetOrdinal("email_fun")) ? null : leitor.GetString("email_fun"),
                        Cep = leitor.IsDBNull(leitor.GetOrdinal("cep_fun")) ? null : leitor.GetString("cep_fun"),
                        Estado = leitor.IsDBNull(leitor.GetOrdinal("estado_fun")) ? null : leitor.GetString("estado_fun"),
                        Cidade = leitor.IsDBNull(leitor.GetOrdinal("cidade_fun")) ? null : leitor.GetString("cidade_fun"),
                        Bairro = leitor.IsDBNull(leitor.GetOrdinal("bairro_fun")) ? null : leitor.GetString("bairro_fun"),
                        Rua = leitor.IsDBNull(leitor.GetOrdinal("rua_fun")) ? null : leitor.GetString("rua_fun"),
                        Numero = leitor.IsDBNull(leitor.GetOrdinal("numero_fun")) ? null : leitor.GetString("numero_fun"),
                        SalarioFixo = leitor.IsDBNull(leitor.GetOrdinal("salario_fixo")) ? 0m : leitor.GetDecimal("salario_fixo"),
                    };

                    lista.Add(funcionario);
                }
            }

            return lista;
        }

        private static void PreencherNovos(MySqlCommand comando, Funcionario funcionario)
        {
            comando.Parameters.AddWithValue("@cargo", funcionario.Cargo ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@comServ", funcionario.ComissaoServicoPadrao);
            comando.Parameters.AddWithValue("@comProd", funcionario.ComissaoProdutoPadrao);
            comando.Parameters.AddWithValue("@admissao", funcionario.DataAdmissao ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@desligamento", funcionario.DataDesligamento ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@pix", funcionario.ChavePix ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@banco", funcionario.DadosBancarios ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@cor", funcionario.CorAgenda ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@ativo", funcionario.Ativo ? 1 : 0);
            comando.Parameters.AddWithValue("@parceiro", funcionario.Parceiro ? 1 : 0);
            comando.Parameters.AddWithValue("@cnpjParceiro", funcionario.CnpjParceiro ?? (object)DBNull.Value);
            comando.Parameters.AddWithValue("@vinculo", funcionario.Vinculo ?? (object)DBNull.Value);
        }

        // ---------- servicos que o profissional realiza ----------

        public List<FuncionarioServico> ListarServicos(int idFuncionario)
        {
            var lista = new List<FuncionarioServico>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT * FROM funcionario_servico WHERE id_fun_fk = @id", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);

            using var leitor = comando.ExecuteReader();
            var iComissao = leitor.GetOrdinal("comissao");
            var iPreco = leitor.GetOrdinal("preco");

            while (leitor.Read())
            {
                lista.Add(new FuncionarioServico
                {
                    IdServico = leitor.GetInt32("id_serv_fk"),
                    Comissao = leitor.IsDBNull(iComissao) ? null : leitor.GetDecimal(iComissao),
                    Preco = leitor.IsDBNull(iPreco) ? null : leitor.GetDecimal(iPreco)
                });
            }

            return lista;
        }

        /// <summary>Todos os pares profissional/servico, para a venda e a agenda resolverem preco e comissao.</summary>
        public Dictionary<(int IdFuncionario, int IdServico), FuncionarioServico> MapaServicos()
        {
            var mapa = new Dictionary<(int, int), FuncionarioServico>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("SELECT * FROM funcionario_servico", conexao);
            using var leitor = comando.ExecuteReader();
            var iComissao = leitor.GetOrdinal("comissao");
            var iPreco = leitor.GetOrdinal("preco");

            while (leitor.Read())
            {
                var item = new FuncionarioServico
                {
                    IdServico = leitor.GetInt32("id_serv_fk"),
                    Comissao = leitor.IsDBNull(iComissao) ? null : leitor.GetDecimal(iComissao),
                    Preco = leitor.IsDBNull(iPreco) ? null : leitor.GetDecimal(iPreco)
                };
                mapa[(leitor.GetInt32("id_fun_fk"), item.IdServico)] = item;
            }

            return mapa;
        }

        /// <summary>Substitui a lista de servicos do profissional numa transacao.</summary>
        public void SalvarServicos(int idFuncionario, IEnumerable<FuncionarioServico> servicos)
        {
            using var conexao = _conexao.GetConnection();
            using var transacao = conexao.BeginTransaction();
            try
            {
                using (var limpar = new MySqlCommand(
                    "DELETE FROM funcionario_servico WHERE id_fun_fk = @id", conexao, transacao))
                {
                    limpar.Parameters.AddWithValue("@id", idFuncionario);
                    limpar.ExecuteNonQuery();
                }

                foreach (var s in servicos)
                {
                    if (s.Comissao < 0 || s.Preco < 0)
                        throw new Exception("Comissão e preço do profissional não podem ser negativos.");

                    using var inserir = new MySqlCommand(@"
                        INSERT INTO funcionario_servico (id_fun_fk, id_serv_fk, comissao, preco)
                        VALUES (@idFun, @idServ, @comissao, @preco)", conexao, transacao);
                    inserir.Parameters.AddWithValue("@idFun", idFuncionario);
                    inserir.Parameters.AddWithValue("@idServ", s.IdServico);
                    inserir.Parameters.AddWithValue("@comissao", s.Comissao ?? (object)DBNull.Value);
                    inserir.Parameters.AddWithValue("@preco", s.Preco ?? (object)DBNull.Value);
                    inserir.ExecuteNonQuery();
                }

                transacao.Commit();
            }
            catch (Exception ex)
            {
                transacao.Rollback();
                throw new Exception("Erro ao salvar os serviços do profissional: " + ex.Message);
            }
        }

        // ---------- foto ----------

        public const int TamanhoMaximoFoto = 1024 * 1024;

        public (string Tipo, byte[] Conteudo)? BuscarFoto(int idFuncionario)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                "SELECT tipo, conteudo FROM funcionario_foto WHERE id_fun_fk = @id", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);

            using var leitor = comando.ExecuteReader();
            if (!leitor.Read()) return null;
            return (leitor.GetString(0), (byte[])leitor.GetValue(1));
        }

        public void SalvarFoto(int idFuncionario, string tipo, byte[] conteudo)
        {
            if (!tipo.StartsWith("image/"))
                throw new Exception("A foto precisa ser uma imagem.");
            if (conteudo.Length > TamanhoMaximoFoto)
                throw new Exception("A foto pode ter no máximo 1 MB.");

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(@"
                INSERT INTO funcionario_foto (id_fun_fk, tipo, conteudo) VALUES (@id, @tipo, @conteudo)
                ON DUPLICATE KEY UPDATE tipo = VALUES(tipo), conteudo = VALUES(conteudo)", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);
            comando.Parameters.AddWithValue("@tipo", tipo);
            comando.Parameters.AddWithValue("@conteudo", conteudo);
            comando.ExecuteNonQuery();
        }

        public void RemoverFoto(int idFuncionario)
        {
            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand("DELETE FROM funcionario_foto WHERE id_fun_fk = @id", conexao);
            comando.Parameters.AddWithValue("@id", idFuncionario);
            comando.ExecuteNonQuery();
        }
    }
}
