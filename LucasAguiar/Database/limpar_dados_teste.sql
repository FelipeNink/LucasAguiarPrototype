-- Remove os dados de exemplo criados durante o teste da tela de vendas.
-- Rode assim:
--   "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe" -uroot -proot < limpar_dados_teste.sql

USE BarbeariaAguiar_2_0;

DELETE FROM venda_item;
DELETE FROM venda;
DELETE FROM plano_item;
DELETE FROM cliente;
DELETE FROM plano;
DELETE FROM produto;
DELETE FROM servico;
DELETE FROM funcionario;

ALTER TABLE venda       AUTO_INCREMENT = 1;
ALTER TABLE venda_item  AUTO_INCREMENT = 1;
ALTER TABLE cliente     AUTO_INCREMENT = 1;
ALTER TABLE plano       AUTO_INCREMENT = 1;
ALTER TABLE plano_item  AUTO_INCREMENT = 1;
ALTER TABLE produto     AUTO_INCREMENT = 1;
ALTER TABLE servico     AUTO_INCREMENT = 1;
ALTER TABLE funcionario AUTO_INCREMENT = 1;
