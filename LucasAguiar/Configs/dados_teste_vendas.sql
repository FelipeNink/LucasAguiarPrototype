-- =====================================================================
-- DADOS DE TESTE - vendas, caixa e movimento
--
-- Complementa o dados_teste.sql, que cria os cadastros. Este preenche
-- o que alimenta o Painel e a tela de Vendas: a venda em si, seus
-- itens, a comissao congelada e o vinculo com o caixa.
--
-- Cada venda vem com os venda_item correspondentes e com a comissao
-- gravada na linha do item -- e assim que o sistema calcula a folha.
-- Inserir venda sem item deixaria o detalhe vazio e a comissao zerada.
--
-- COMO RODAR (depois do dados_teste.sql):
--   mysql -u USUARIO -p --default-character-set=utf8mb4 BANCO < dados_teste_vendas.sql
--
-- * So insere. Rodar duas vezes duplica.
-- * Datas relativas a CURDATE, entao o painel sempre mostra movimento.
-- =====================================================================
SET NAMES utf8mb4;
START TRANSACTION;

-- ---------------------------------------------------------------
-- Caixa
-- Fecha o que estiver aberto e abre um do dia. Sem isso as vendas
-- de hoje nao teriam caixa para entrar, e a gaveta nao bateria.
-- ---------------------------------------------------------------
UPDATE caixa
SET status = 'FECHADO',
    data_fechamento = COALESCE(data_fechamento, NOW()),
    usuario_fechamento = COALESCE(usuario_fechamento, 'Administrador'),
    observacao = CONCAT(COALESCE(observacao, ''), ' [fechado pelo script de teste]')
WHERE status = 'ABERTO';

INSERT INTO caixa (data_abertura, valor_abertura, status, usuario_abertura, observacao)
VALUES (TIMESTAMP(CURDATE(), '08:45:00'), 200.00, 'ABERTO', 'Administrador', 'Abertura do dia');
SET @caixa = LAST_INSERT_ID();

INSERT INTO movimento_caixa (id_caixa_fk, tipo, valor, descricao, data_hora, usuario)
VALUES (@caixa, 'SUPRIMENTO', 100.00, 'Troco extra para o movimento da tarde',
        TIMESTAMP(CURDATE(), '13:00:00'), 'Administrador');

-- ---------------------------------------------------------------
-- Vendas de hoje
-- ---------------------------------------------------------------
INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (50.00, TIMESTAMP(CURDATE(), '09:20:00'), 0, 1, 'DINHEIRO', 0,
        'Corte degradê', 'CONFIRMADA', 3, 4, @caixa);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 5, 'Corte degradê', 1, 50.00, 50.00, 15.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (80.00, TIMESTAMP(CURDATE(), '10:40:00'), 0, 2, 'PIX', 0,
        'Corte degradê, Barba terapia', 'CONFIRMADA', 4, 5, @caixa);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 5, 'Corte degradê', 1, 50.00, 50.00, 15.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 6, 'Barba terapia', 1, 45.00, 45.00, 13.50, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (97.00, TIMESTAMP(CURDATE(), '11:55:00'), 1, 1, 'DEBITO', 0,
        'Corte masculino, Pomada efeito seco', 'CONFIRMADA', 1, 6, @caixa);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 1, 'Corte masculino', 1, 45.00, 45.00, 15.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'PRODUTO', 3, 'Pomada efeito seco 120g', 1, 42.00, 42.00, 0.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, quant_parcela_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (180.00, TIMESTAMP(CURDATE(), '15:10:00'), 0, 1, 'CREDITO', 0, 3,
        'Platinado', 'CONFIRMADA', 5, 7, @caixa);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 8, 'Platinado', 1, 180.00, 180.00, 50.00, 0);

-- Cancelada: a tela risca o valor e a folha nao conta a comissao.
INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk,
                   data_cancelamento, motivo_cancelamento, usuario_cancelamento)
VALUES (15.00, TIMESTAMP(CURDATE(), '16:30:00'), 0, 1, 'DINHEIRO', 0,
        'Pezinho', 'CANCELADA', 3, 4, @caixa,
        TIMESTAMP(CURDATE(), '16:35:00'), 'Cliente desistiu', 'Administrador');
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 7, 'Pezinho', 1, 15.00, 15.00, 4.00, 0);

-- ---------------------------------------------------------------
-- Ontem
-- ---------------------------------------------------------------
INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (45.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 1 DAY), '09:15:00'), 0, 1, 'PIX', 0,
        'Barba terapia', 'CONFIRMADA', 4, 1, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 6, 'Barba terapia', 1, 45.00, 45.00, 13.50, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (133.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 1 DAY), '14:20:00'), 1, 2, 'DINHEIRO', 10.00,
        'Corte degradê, Sobrancelha, Óleo para barba', 'CONFIRMADA', 3, 2, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 5, 'Corte degradê', 1, 50.00, 50.00, 15.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 4, 'Sobrancelha', 1, 20.00, 20.00, 8.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'PRODUTO', 4, 'Óleo para barba 30ml', 1, 55.00, 55.00, 0.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (35.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 1 DAY), '17:40:00'), 0, 1, 'DEBITO', 0,
        'Corte infantil', 'CONFIRMADA', 5, 6, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 3, 'Corte infantil', 1, 35.00, 35.00, 10.00, 0);

-- ---------------------------------------------------------------
-- Dias anteriores, para o painel ter serie e a lista ter grupos
-- ---------------------------------------------------------------
INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (95.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 3 DAY), '10:05:00'), 0, 2, 'PIX', 0,
        'Corte degradê, Sobrancelha', 'CONFIRMADA', 1, 5, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 5, 'Corte degradê', 1, 50.00, 50.00, 15.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 4, 'Sobrancelha', 1, 20.00, 20.00, 8.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (68.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 4 DAY), '16:00:00'), 1, 0, 'CREDITO', 0,
        'Tônico capilar 150ml', 'CONFIRMADA', 4, 7, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'PRODUTO', 5, 'Tônico capilar 150ml', 1, 68.00, 68.00, 0.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (90.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 6 DAY), '11:30:00'), 0, 2, 'DINHEIRO', 0,
        'Barba terapia, Corte degradê', 'CONFIRMADA', 3, 1, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 6, 'Barba terapia', 1, 45.00, 45.00, 13.50, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 5, 'Corte degradê', 1, 50.00, 50.00, 15.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (160.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 9 DAY), '14:45:00'), 0, 1, 'PIX', 20.00,
        'Platinado', 'CONFIRMADA', 5, 6, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 8, 'Platinado', 1, 180.00, 180.00, 50.00, 0);

INSERT INTO venda (valor_vend, data_vend, quantidade_prod_vend, quantidade_serv_vend, forma_pagamento_vend,
                   desconto_vend, descricao_vend, status_vend, id_fun_fk, id_cli_fk, id_caixa_fk)
VALUES (60.00, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 13 DAY), '09:50:00'), 0, 2, 'DEBITO', 0,
        'Corte masculino, Pezinho', 'CONFIRMADA', 1, 2, NULL);
SET @v = LAST_INSERT_ID();
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 1, 'Corte masculino', 1, 45.00, 45.00, 15.00, 0);
INSERT INTO venda_item (id_vend_fk, tipo_item, id_ref, descricao, quantidade, valor_unit, valor_total, comissao, coberto_plano)
VALUES (@v, 'SERVICO', 7, 'Pezinho', 1, 15.00, 15.00, 4.00, 0);

COMMIT;
