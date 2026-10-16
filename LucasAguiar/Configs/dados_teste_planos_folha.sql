-- =====================================================================
-- DADOS DE TESTE - assinaturas de plano e folha de pagamento
--
-- Fecha as tabelas que sobraram: assinatura, assinatura_saldo e
-- folha_pagamento. Com isso nenhuma tela do sistema fica vazia.
--
-- A assinatura so e util com os saldos ao lado: e o saldo que a tela
-- de Nova Venda le para oferecer a baixa de credito. Uma assinatura
-- sem saldo aparece como "sem creditos" e nao serve para testar.
--
-- Os ids sao resolvidos por NOME, nao escritos a mao: cada banco tem a
-- sua numeracao, e id fixo so funciona na maquina onde foi escrito.
--
-- COMO RODAR (depois do dados_teste.sql):
--   mysql -u USUARIO -p --default-character-set=utf8mb4 BANCO < dados_teste_planos_folha.sql
-- =====================================================================
SET NAMES utf8mb4;
START TRANSACTION;

SELECT id_serv INTO @s_degrade  FROM servico WHERE nome_serv = 'Corte degradê' LIMIT 1;
SELECT id_serv INTO @s_barba    FROM servico WHERE nome_serv = 'Barba terapia' LIMIT 1;
SELECT id_serv INTO @s_pezinho  FROM servico WHERE nome_serv = 'Pezinho'       LIMIT 1;

SELECT id_plan INTO @pl_degrade FROM plano WHERE nome_plan = 'Plano Degradê Mensal' LIMIT 1;
SELECT id_plan INTO @pl_barba   FROM plano WHERE nome_plan = 'Plano Barba Semanal'  LIMIT 1;
SELECT id_plan INTO @pl_combo   FROM plano WHERE nome_plan = 'Plano Combo Pezinho'  LIMIT 1;

SELECT id_cli INTO @c_rafael    FROM cliente WHERE nome_cli = 'Rafael Andrade' LIMIT 1;
SELECT id_cli INTO @c_bruno     FROM cliente WHERE nome_cli = 'Bruno Tavares'  LIMIT 1;
SELECT id_cli INTO @c_thiago    FROM cliente WHERE nome_cli = 'Thiago Menezes' LIMIT 1;

SELECT id_fun INTO @f_marcos    FROM funcionario WHERE nome_fun = 'Marcos Vinicius Rocha' LIMIT 1;
SELECT id_fun INTO @f_diego     FROM funcionario WHERE nome_fun = 'Diego Nascimento'      LIMIT 1;
SELECT id_fun INTO @f_henrique  FROM funcionario WHERE nome_fun = 'Henrique Prado'        LIMIT 1;

-- ---------------------------------------------------------------
-- Assinaturas
-- Tres situacoes diferentes: intacta, pela metade e esgotada.
-- ---------------------------------------------------------------

-- Rafael Andrade: plano de corte novinho, nenhum credito usado.
INSERT INTO assinatura (id_cli_fk, id_plan_fk, data_inicio, data_fim, valor_pago, status)
VALUES (@c_rafael, @pl_degrade, DATE_SUB(CURDATE(), INTERVAL 5 DAY), DATE_ADD(CURDATE(), INTERVAL 25 DAY), 170.00, 'ATIVA');
SET @a = LAST_INSERT_ID();
INSERT INTO assinatura_saldo (id_assinatura_fk, tipo_item, id_ref, descricao, valor_referencia, quantidade_total, quantidade_usada)
VALUES (@a, 'SERVICO', @s_degrade, 'Corte degradê', 50.00, 4, 0);

-- Bruno Tavares: plano de barba pela metade.
INSERT INTO assinatura (id_cli_fk, id_plan_fk, data_inicio, data_fim, valor_pago, status)
VALUES (@c_bruno, @pl_barba, DATE_SUB(CURDATE(), INTERVAL 18 DAY), DATE_ADD(CURDATE(), INTERVAL 12 DAY), 150.00, 'ATIVA');
SET @a = LAST_INSERT_ID();
INSERT INTO assinatura_saldo (id_assinatura_fk, tipo_item, id_ref, descricao, valor_referencia, quantidade_total, quantidade_usada)
VALUES (@a, 'SERVICO', @s_barba, 'Barba terapia', 45.00, 4, 2);

-- Thiago Menezes: combo com dois servicos, um deles ja esgotado.
INSERT INTO assinatura (id_cli_fk, id_plan_fk, data_inicio, data_fim, valor_pago, status)
VALUES (@c_thiago, @pl_combo, DATE_SUB(CURDATE(), INTERVAL 22 DAY), DATE_ADD(CURDATE(), INTERVAL 8 DAY), 130.00, 'ATIVA');
SET @a = LAST_INSERT_ID();
INSERT INTO assinatura_saldo (id_assinatura_fk, tipo_item, id_ref, descricao, valor_referencia, quantidade_total, quantidade_usada)
VALUES (@a, 'SERVICO', @s_degrade, 'Corte degradê', 50.00, 2, 2);
INSERT INTO assinatura_saldo (id_assinatura_fk, tipo_item, id_ref, descricao, valor_referencia, quantidade_total, quantidade_usada)
VALUES (@a, 'SERVICO', @s_pezinho, 'Pezinho', 15.00, 4, 1);

-- ---------------------------------------------------------------
-- Folha de pagamento do mes passado
-- Cada pagamento gera a despesa correspondente, como o sistema faz:
-- a folha aponta para a despesa pelo id_desp_fk.
-- ---------------------------------------------------------------
INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, observacao)
VALUES (LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)),
        'Salário e comissões — Marcos Vinicius Rocha', 'SALARIO', 1742.50, 'PIX', 1,
        'Gerada pela folha de pagamento');
SET @d = LAST_INSERT_ID();
INSERT INTO folha_pagamento (id_fun_fk, competencia, salario_fixo, comissoes, total,
                             servicos, valor_servicos, produtos, valor_produtos,
                             data_pgto, id_desp_fk, observacao, usuario)
VALUES (@f_marcos, DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m'), 1600.00, 142.50, 1742.50,
        8, 475.00, 1, 42.00,
        LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)), @d, NULL, 'Administrador');

INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, observacao)
VALUES (LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)),
        'Salário e comissões — Diego Nascimento', 'SALARIO', 1540.00, 'PIX', 1,
        'Gerada pela folha de pagamento');
SET @d = LAST_INSERT_ID();
INSERT INTO folha_pagamento (id_fun_fk, competencia, salario_fixo, comissoes, total,
                             servicos, valor_servicos, produtos, valor_produtos,
                             data_pgto, id_desp_fk, observacao, usuario)
VALUES (@f_diego, DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m'), 1450.00, 90.00, 1540.00,
        6, 320.00, 2, 123.00,
        LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)), @d, NULL, 'Administrador');

INSERT INTO despesa (data_desp, descricao, categoria, valor, forma_pagamento, pago, observacao)
VALUES (LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)),
        'Salário e comissões — Henrique Prado', 'SALARIO', 1430.00, 'PIX', 1,
        'Gerada pela folha de pagamento');
SET @d = LAST_INSERT_ID();
INSERT INTO folha_pagamento (id_fun_fk, competencia, salario_fixo, comissoes, total,
                             servicos, valor_servicos, produtos, valor_produtos,
                             data_pgto, id_desp_fk, observacao, usuario)
VALUES (@f_henrique, DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m'), 1380.00, 50.00, 1430.00,
        3, 215.00, 0, 0.00,
        LAST_DAY(DATE_SUB(CURDATE(), INTERVAL 1 MONTH)), @d, NULL, 'Administrador');

COMMIT;
