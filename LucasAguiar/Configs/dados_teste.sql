-- =====================================================================
-- DADOS DE TESTE - conjunto enxuto
--
-- Tres a cinco registros por tabela, escolhidos para exercitar estados
-- diferentes e nao so encher a listagem: uma despesa em aberto, um
-- produto com estoque baixo, agendamentos de hoje/amanha/concluido,
-- um servico longo e um curto, e um plano com dois servicos.
--
-- Diferenca para dados_exemplo.sql, que fica ao lado: aquele e um
-- conjunto grande, com dezenas de vendas e meses de historico, para
-- demonstracao. Este e pequeno, para quem quer conferir uma tela sem
-- rolar pagina. Rode um ou outro -- os dois juntos so embaralham.
--
-- COMO RODAR:
--   mysql -u USUARIO -p --default-character-set=utf8mb4 NOME_DO_BANCO < dados_teste.sql
--
-- * O --default-character-set=utf8mb4 nao e opcional: sem ele o cliente
--   do Windows abre em cp850 e os acentos entram errados.
-- * So insere, nao apaga. Rodar duas vezes duplica os registros.
-- * As chaves sao encadeadas por LAST_INSERT_ID, entao funciona em
--   qualquer banco, com qualquer id ja existente.
-- * Datas relativas a CURDATE, entao a agenda sempre parece atual.
-- =====================================================================
-- para nao deixar o banco pela metade se alguma chave falhar.
SET NAMES utf8mb4;
START TRANSACTION;

-- ---------------------------------------------------------------
-- Fornecedores
-- ---------------------------------------------------------------
INSERT INTO fornecedor (nome_forn, email_forn, telefone_forn, tipo_prod_forn)
VALUES ('Barba Nobre Distribuidora', 'contato@barbanobre.com.br', '(48)3344-2010', 'Pomadas, óleos e balms');
SET @forn_a = LAST_INSERT_ID();

INSERT INTO fornecedor (nome_forn, email_forn, telefone_forn, tipo_prod_forn)
VALUES ('Lâmina & Cia', 'vendas@laminaecia.com.br', '(48)3344-2011', 'Navalhas, tesouras e máquinas');
SET @forn_b = LAST_INSERT_ID();

INSERT INTO fornecedor (nome_forn, email_forn, telefone_forn, tipo_prod_forn)
VALUES ('Essência Capilar', 'comercial@essenciacapilar.com', '(48)98822-3012', 'Shampoos, tônicos e condicionadores');
SET @forn_c = LAST_INSERT_ID();

-- ---------------------------------------------------------------
-- Funcionarios
-- ---------------------------------------------------------------
INSERT INTO funcionario (nome_fun, telefone_fun, cpf_fun, data_nasc_fun, email_fun, estado_fun, cidade_fun, bairro_fun, salario_fixo)
VALUES ('Marcos Vinicius Rocha', '(48)99101-2233', '512.447.880-21', '1992-04-18', 'marcos.rocha@exemplo.com', 'SC', 'Florianópolis', 'Trindade', 1600.00);
SET @fun_a = LAST_INSERT_ID();

INSERT INTO funcionario (nome_fun, telefone_fun, cpf_fun, data_nasc_fun, email_fun, estado_fun, cidade_fun, bairro_fun, salario_fixo)
VALUES ('Diego Nascimento', '(48)99202-3344', '623.558.991-32', '1988-11-02', 'diego.nascimento@exemplo.com', 'SC', 'São José', 'Kobrasol', 1450.00);
SET @fun_b = LAST_INSERT_ID();

INSERT INTO funcionario (nome_fun, telefone_fun, cpf_fun, data_nasc_fun, email_fun, estado_fun, cidade_fun, bairro_fun, salario_fixo)
VALUES ('Henrique Prado', '(48)99303-4455', '734.669.002-43', '1995-07-25', 'henrique.prado@exemplo.com', 'SC', 'Palhoça', 'Pedra Branca', 1380.00);
SET @fun_c = LAST_INSERT_ID();

-- ---------------------------------------------------------------
-- Servicos  (comis_funcionario_cli e percentual de comissao)
-- ---------------------------------------------------------------
INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli)
VALUES ('Corte degradê', 50.00, 40, 15.00);
SET @serv_a = LAST_INSERT_ID();

INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli)
VALUES ('Barba terapia', 45.00, 35, 13.50);
SET @serv_b = LAST_INSERT_ID();

INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli)
VALUES ('Pezinho', 15.00, 10, 4.00);
SET @serv_c = LAST_INSERT_ID();

INSERT INTO servico (nome_serv, preco_serv, duracao_min, comis_funcionario_cli)
VALUES ('Platinado', 180.00, 120, 50.00);
SET @serv_d = LAST_INSERT_ID();

-- ---------------------------------------------------------------
-- Produtos
-- ---------------------------------------------------------------
INSERT INTO produto (nome_prod, quantidade_prod, valor_prod, descricao_prod, marca_prod, id_forn_fk)
VALUES ('Pomada efeito seco 120g', 18, 42.00, 'Fixação forte, acabamento matte', 'Barba Nobre', @forn_a);

INSERT INTO produto (nome_prod, quantidade_prod, valor_prod, descricao_prod, marca_prod, id_forn_fk)
VALUES ('Óleo para barba 30ml', 12, 55.00, 'Hidrata e reduz o frizz', 'Barba Nobre', @forn_a);

INSERT INTO produto (nome_prod, quantidade_prod, valor_prod, descricao_prod, marca_prod, id_forn_fk)
VALUES ('Tônico capilar 150ml', 3, 68.00, 'Estimula o crescimento', 'Essência', @forn_c);

INSERT INTO produto (nome_prod, quantidade_prod, valor_prod, descricao_prod, marca_prod, id_forn_fk)
VALUES ('Navalha profissional', 6, 95.00, 'Cabo de madeira, inox', 'Lâmina & Cia', @forn_b);

-- ---------------------------------------------------------------
-- Clientes
-- ---------------------------------------------------------------
INSERT INTO cliente (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, estado_cli, cidade_cli, bairro_cli)
VALUES ('Rafael Andrade', '(48)99411-5566', '845.770.113-54', '1990-02-14', 'SC', 'Florianópolis', 'Centro');
SET @cli_a = LAST_INSERT_ID();

INSERT INTO cliente (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, estado_cli, cidade_cli, bairro_cli)
VALUES ('Bruno Tavares', '(48)99522-6677', '956.881.224-65', '1997-09-30', 'SC', 'São José', 'Campinas');
SET @cli_b = LAST_INSERT_ID();

INSERT INTO cliente (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, estado_cli, cidade_cli, bairro_cli)
VALUES ('Thiago Menezes', '(48)99633-7788', '067.992.335-76', '1985-06-08', 'SC', 'Biguaçu', 'Universitário');
SET @cli_c = LAST_INSERT_ID();

INSERT INTO cliente (nome_cli, telefone_cli, cpf_cli, data_nasc_cli, estado_cli, cidade_cli, bairro_cli)
VALUES ('Eduardo Pacheco', '(48)99744-8899', '178.003.446-87', '2001-12-19', 'SC', 'Palhoça', 'Bela Vista');
SET @cli_d = LAST_INSERT_ID();

-- ---------------------------------------------------------------
-- Planos e seus componentes
-- ---------------------------------------------------------------
INSERT INTO plano (nome_plan, descricao_plan, valor_plan)
VALUES ('Plano Degradê Mensal', '4 cortes degradê por mês', 170.00);
SET @plan_a = LAST_INSERT_ID();

INSERT INTO plano_item (id_plan_fk, tipo_item, id_ref, quantidade)
VALUES (@plan_a, 'SERVICO', @serv_a, 4);

INSERT INTO plano (nome_plan, descricao_plan, valor_plan)
VALUES ('Plano Barba Semanal', '4 barbas terapia por mês', 150.00);
SET @plan_b = LAST_INSERT_ID();

INSERT INTO plano_item (id_plan_fk, tipo_item, id_ref, quantidade)
VALUES (@plan_b, 'SERVICO', @serv_b, 4);

INSERT INTO plano (nome_plan, descricao_plan, valor_plan)
VALUES ('Plano Combo Pezinho', '2 cortes e 4 pezinhos', 130.00);
SET @plan_c = LAST_INSERT_ID();

INSERT INTO plano_item (id_plan_fk, tipo_item, id_ref, quantidade)
VALUES (@plan_c, 'SERVICO', @serv_a, 2);
INSERT INTO plano_item (id_plan_fk, tipo_item, id_ref, quantidade)
VALUES (@plan_c, 'SERVICO', @serv_c, 4);

-- ---------------------------------------------------------------
-- Despesas  (uma em aberto, para exercitar o estado "pendente")
-- ---------------------------------------------------------------
INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, id_forn_fk, observacao)
VALUES (DATE_SUB(CURDATE(), INTERVAL 5 DAY), 'Reposição de pomadas', 'PRODUTOS', 420.00, 'PIX', 1, @forn_a, 'Pedido mensal');

INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, observacao)
VALUES (DATE_SUB(CURDATE(), INTERVAL 3 DAY), 'Conta de energia', 'CONTAS', 318.45, 'PIX', 1, NULL);

INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, id_forn_fk, observacao)
VALUES (DATE_SUB(CURDATE(), INTERVAL 1 DAY), 'Troca de lâminas', 'PRODUTOS', 190.00, 'DEBITO', 1, @forn_b, NULL);

INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, observacao)
VALUES (CURDATE(), 'Manutenção da cadeira 2', 'MANUTENCAO', 260.00, 'DINHEIRO', 0, 'Orçamento aprovado, a pagar na entrega');

-- ---------------------------------------------------------------
-- Compras
-- ---------------------------------------------------------------
INSERT INTO compras (data_comp, valor_comp, item_comp, quantidade, id_forn_fk, id_fun_fk)
VALUES (DATE_SUB(CURDATE(), INTERVAL 12 DAY), 504.00, 'Pomada efeito seco 120g', 12, @forn_a, @fun_a);

INSERT INTO compras (data_comp, valor_comp, item_comp, quantidade, id_forn_fk, id_fun_fk)
VALUES (DATE_SUB(CURDATE(), INTERVAL 8 DAY), 340.00, 'Tônico capilar 150ml', 5, @forn_c, @fun_b);

INSERT INTO compras (data_comp, valor_comp, item_comp, quantidade, id_forn_fk, id_fun_fk)
VALUES (DATE_SUB(CURDATE(), INTERVAL 2 DAY), 285.00, 'Navalha profissional', 3, @forn_b, @fun_a);

-- ---------------------------------------------------------------
-- Agendamentos  (hoje, amanha e um concluido, para cobrir os filtros)
-- ---------------------------------------------------------------
INSERT INTO agendamento (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
VALUES (TIMESTAMP(CURDATE(), '09:30:00'), 40, @cli_a, @fun_a, @serv_a, 'Primeira vez', 'AGENDADO');

INSERT INTO agendamento (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
VALUES (TIMESTAMP(CURDATE(), '11:00:00'), 35, @cli_b, @fun_b, @serv_b, NULL, 'AGENDADO');

INSERT INTO agendamento (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
VALUES (TIMESTAMP(CURDATE(), '15:00:00'), 120, @cli_c, @fun_c, @serv_d, 'Trouxe foto de referência', 'AGENDADO');

INSERT INTO agendamento (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
VALUES (TIMESTAMP(DATE_ADD(CURDATE(), INTERVAL 1 DAY), '10:00:00'), 10, @cli_d, @fun_a, @serv_c, NULL, 'AGENDADO');

INSERT INTO agendamento (data_hora, duracao_min, id_cli_fk, id_fun_fk, id_serv_fk, observacao, status)
VALUES (TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 2 DAY), '14:00:00'), 40, @cli_a, @fun_b, @serv_a, NULL, 'CONCLUIDO');

COMMIT;
