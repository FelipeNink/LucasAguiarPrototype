-- Esquema do banco da Barbearia Lucas Aguiar.
--
-- Gerado a partir do banco em uso. O arquivo anterior tinha 8 tabelas e
-- parou no tempo: faltavam usuario (sem ela nao ha login), assinatura,
-- assinatura_saldo, caixa, movimento_caixa, despesa, folha_pagamento,
-- plano_item e venda_item. Num banco novo, o sistema nao subia.
--
-- Nao cria o banco nem o usuario de acesso: crie o schema antes e rode
-- este arquivo dentro dele. O usuario de login tambem nao vem pronto --
-- veja o final do arquivo.


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `assinatura` (
  `id_assinatura` int NOT NULL AUTO_INCREMENT,
  `id_cli_fk` int NOT NULL,
  `id_plan_fk` int NOT NULL,
  `data_inicio` date NOT NULL,
  `data_fim` date DEFAULT NULL,
  `valor_pago` decimal(10,2) NOT NULL DEFAULT '0.00',
  `id_vend_fk` int DEFAULT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'ATIVA',
  PRIMARY KEY (`id_assinatura`),
  KEY `fk_assinatura_plano` (`id_plan_fk`),
  KEY `idx_assinatura_cliente` (`id_cli_fk`,`status`),
  CONSTRAINT `fk_assinatura_cliente` FOREIGN KEY (`id_cli_fk`) REFERENCES `cliente` (`id_cli`),
  CONSTRAINT `fk_assinatura_plano` FOREIGN KEY (`id_plan_fk`) REFERENCES `plano` (`id_plan`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `assinatura_saldo` (
  `id_saldo` int NOT NULL AUTO_INCREMENT,
  `id_assinatura_fk` int NOT NULL,
  `tipo_item` varchar(10) NOT NULL,
  `id_ref` int NOT NULL,
  `descricao` varchar(200) NOT NULL,
  `valor_referencia` decimal(10,2) NOT NULL DEFAULT '0.00',
  `quantidade_total` int NOT NULL,
  `quantidade_usada` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id_saldo`),
  KEY `idx_saldo_assinatura` (`id_assinatura_fk`),
  CONSTRAINT `fk_saldo_assinatura` FOREIGN KEY (`id_assinatura_fk`) REFERENCES `assinatura` (`id_assinatura`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `caixa` (
  `id_caixa` int NOT NULL AUTO_INCREMENT,
  `data_abertura` datetime NOT NULL,
  `data_fechamento` datetime DEFAULT NULL,
  `valor_abertura` decimal(10,2) NOT NULL DEFAULT '0.00',
  `valor_informado` decimal(10,2) DEFAULT NULL,
  `valor_calculado` decimal(10,2) DEFAULT NULL,
  `diferenca` decimal(10,2) DEFAULT NULL,
  `status` varchar(10) NOT NULL DEFAULT 'ABERTO',
  `usuario_abertura` varchar(100) DEFAULT NULL,
  `usuario_fechamento` varchar(100) DEFAULT NULL,
  `observacao` varchar(300) DEFAULT NULL,
  PRIMARY KEY (`id_caixa`),
  KEY `idx_caixa_status` (`status`),
  KEY `idx_caixa_abertura` (`data_abertura`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `cliente` (
  `id_cli` int NOT NULL AUTO_INCREMENT,
  `nome_cli` varchar(100) DEFAULT NULL,
  `telefone_cli` varchar(14) DEFAULT NULL,
  `cpf_cli` varchar(14) DEFAULT NULL,
  `data_nasc_cli` date DEFAULT NULL,
  `rg_cli` varchar(7) DEFAULT NULL,
  `estado_cli` varchar(200) DEFAULT NULL,
  `cidade_cli` varchar(200) DEFAULT NULL,
  `bairro_cli` varchar(200) DEFAULT NULL,
  `rua_cli` varchar(200) DEFAULT NULL,
  `numero_cli` varchar(200) DEFAULT NULL,
  `id_plan_fk` int DEFAULT NULL,
  PRIMARY KEY (`id_cli`),
  KEY `id_plan_fk` (`id_plan_fk`),
  CONSTRAINT `cliente_ibfk_1` FOREIGN KEY (`id_plan_fk`) REFERENCES `plano` (`id_plan`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `compras` (
  `id_comp` int NOT NULL AUTO_INCREMENT,
  `data_comp` date DEFAULT NULL,
  `valor_comp` decimal(10,2) DEFAULT '0.00',
  `item_comp` varchar(200) DEFAULT NULL,
  `quantidade` int DEFAULT '0',
  `id_prod_fk` int DEFAULT NULL,
  `id_forn_fk` int DEFAULT NULL,
  `id_fun_fk` int DEFAULT NULL,
  `id_desp_fk` int DEFAULT NULL,
  PRIMARY KEY (`id_comp`),
  KEY `id_prod_fk` (`id_prod_fk`),
  KEY `id_forn_fk` (`id_forn_fk`),
  KEY `id_fun_fk` (`id_fun_fk`),
  KEY `fk_compra_despesa` (`id_desp_fk`),
  CONSTRAINT `compras_ibfk_1` FOREIGN KEY (`id_prod_fk`) REFERENCES `produto` (`id_prod`),
  CONSTRAINT `compras_ibfk_2` FOREIGN KEY (`id_forn_fk`) REFERENCES `fornecedor` (`id_forn`),
  CONSTRAINT `compras_ibfk_3` FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`),
  CONSTRAINT `fk_compra_despesa` FOREIGN KEY (`id_desp_fk`) REFERENCES `despesa` (`id_desp`) ON DELETE SET NULL
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `despesa` (
  `id_desp` int NOT NULL AUTO_INCREMENT,
  `data_desp` date NOT NULL,
  `descricao` varchar(200) NOT NULL,
  `categoria` varchar(40) NOT NULL DEFAULT 'OUTROS',
  `valor` decimal(10,2) NOT NULL,
  `forma_pagamento` varchar(20) NOT NULL DEFAULT 'DINHEIRO',
  `pago` tinyint(1) NOT NULL DEFAULT '1',
  `id_forn_fk` int DEFAULT NULL,
  `id_caixa_fk` int DEFAULT NULL,
  `observacao` varchar(300) DEFAULT NULL,
  PRIMARY KEY (`id_desp`),
  KEY `fk_despesa_fornecedor` (`id_forn_fk`),
  KEY `idx_despesa_data` (`data_desp`),
  KEY `idx_despesa_caixa` (`id_caixa_fk`),
  CONSTRAINT `fk_despesa_caixa` FOREIGN KEY (`id_caixa_fk`) REFERENCES `caixa` (`id_caixa`),
  CONSTRAINT `fk_despesa_fornecedor` FOREIGN KEY (`id_forn_fk`) REFERENCES `fornecedor` (`id_forn`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `folha_pagamento` (
  `id_folha` int NOT NULL AUTO_INCREMENT,
  `id_fun_fk` int NOT NULL,
  `competencia` char(7) NOT NULL,
  `salario_fixo` decimal(10,2) NOT NULL DEFAULT '0.00',
  `comissoes` decimal(10,2) NOT NULL DEFAULT '0.00',
  `total` decimal(10,2) NOT NULL DEFAULT '0.00',
  `servicos` int NOT NULL DEFAULT '0',
  `valor_servicos` decimal(10,2) NOT NULL DEFAULT '0.00',
  `produtos` int NOT NULL DEFAULT '0',
  `valor_produtos` decimal(10,2) NOT NULL DEFAULT '0.00',
  `data_pgto` datetime NOT NULL,
  `id_desp_fk` int DEFAULT NULL,
  `observacao` varchar(300) DEFAULT NULL,
  `usuario` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`id_folha`),
  UNIQUE KEY `uk_folha_competencia` (`id_fun_fk`,`competencia`),
  KEY `fk_folha_despesa` (`id_desp_fk`),
  CONSTRAINT `fk_folha_despesa` FOREIGN KEY (`id_desp_fk`) REFERENCES `despesa` (`id_desp`) ON DELETE SET NULL,
  CONSTRAINT `fk_folha_funcionario` FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `fornecedor` (
  `id_forn` int NOT NULL AUTO_INCREMENT,
  `nome_forn` varchar(200) DEFAULT NULL,
  `email_forn` varchar(200) DEFAULT NULL,
  `telefone_forn` varchar(14) DEFAULT NULL,
  `tipo_prod_forn` varchar(300) DEFAULT NULL,
  PRIMARY KEY (`id_forn`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `funcionario` (
  `id_fun` int NOT NULL AUTO_INCREMENT,
  `nome_fun` varchar(100) DEFAULT NULL,
  `telefone_fun` varchar(14) DEFAULT NULL,
  `cpf_fun` varchar(14) DEFAULT NULL,
  `data_nasc_fun` date DEFAULT NULL,
  `ctps_fun` varchar(12) DEFAULT NULL,
  `rg_fun` varchar(7) DEFAULT NULL,
  `email_fun` varchar(100) DEFAULT NULL,
  `estado_fun` varchar(200) DEFAULT NULL,
  `cidade_fun` varchar(200) DEFAULT NULL,
  `bairro_fun` varchar(200) DEFAULT NULL,
  `rua_fun` varchar(200) DEFAULT NULL,
  `numero_fun` varchar(200) DEFAULT NULL,
  `salario_fixo` decimal(10,2) NOT NULL DEFAULT '0.00',
  PRIMARY KEY (`id_fun`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `movimento_caixa` (
  `id_mov` int NOT NULL AUTO_INCREMENT,
  `id_caixa_fk` int NOT NULL,
  `tipo` varchar(12) NOT NULL,
  `valor` decimal(10,2) NOT NULL,
  `descricao` varchar(200) DEFAULT NULL,
  `data_hora` datetime NOT NULL,
  `usuario` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`id_mov`),
  KEY `idx_mov_caixa` (`id_caixa_fk`),
  CONSTRAINT `fk_mov_caixa` FOREIGN KEY (`id_caixa_fk`) REFERENCES `caixa` (`id_caixa`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `plano` (
  `id_plan` int NOT NULL AUTO_INCREMENT,
  `nome_plan` varchar(200) DEFAULT NULL,
  `descricao_plan` varchar(200) DEFAULT NULL,
  `valor_plan` float DEFAULT NULL,
  PRIMARY KEY (`id_plan`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `plano_item` (
  `id_plan_item` int NOT NULL AUTO_INCREMENT,
  `id_plan_fk` int NOT NULL,
  `tipo_item` varchar(10) NOT NULL,
  `id_ref` int NOT NULL,
  `quantidade` int NOT NULL DEFAULT '1',
  PRIMARY KEY (`id_plan_item`),
  KEY `idx_plano_item_plano` (`id_plan_fk`),
  CONSTRAINT `fk_plano_item_plano` FOREIGN KEY (`id_plan_fk`) REFERENCES `plano` (`id_plan`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=7 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `produto` (
  `id_prod` int NOT NULL AUTO_INCREMENT,
  `nome_prod` varchar(200) DEFAULT NULL,
  `quantidade_prod` int DEFAULT NULL,
  `valor_prod` float DEFAULT NULL,
  `descricao_prod` varchar(200) DEFAULT NULL,
  `marca_prod` varchar(200) DEFAULT NULL,
  `id_forn_fk` int DEFAULT NULL,
  PRIMARY KEY (`id_prod`),
  KEY `id_forn_fk` (`id_forn_fk`),
  CONSTRAINT `produto_ibfk_1` FOREIGN KEY (`id_forn_fk`) REFERENCES `fornecedor` (`id_forn`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `servico` (
  `id_serv` int NOT NULL AUTO_INCREMENT,
  `nome_serv` varchar(100) DEFAULT NULL,
  `preco_serv` float DEFAULT NULL,
  `duracao_min` int DEFAULT NULL,
  `comis_funcionario_cli` float DEFAULT NULL,
  PRIMARY KEY (`id_serv`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `usuario` (
  `id_usr` int NOT NULL AUTO_INCREMENT,
  `nome_usr` varchar(100) DEFAULT NULL,
  `email_usr` varchar(100) DEFAULT NULL,
  `senha_usr` varchar(255) DEFAULT NULL,
  `ativo_usr` tinyint(1) DEFAULT '1',
  `data_criacao_usr` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id_usr`),
  UNIQUE KEY `email_usr` (`email_usr`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `venda` (
  `id_vend` int NOT NULL AUTO_INCREMENT,
  `valor_vend` decimal(10,2) DEFAULT '0.00',
  `data_vend` datetime DEFAULT NULL,
  `quantidade_prod_vend` int DEFAULT NULL,
  `quantidade_serv_vend` int DEFAULT NULL,
  `forma_pagamento_vend` varchar(200) DEFAULT NULL,
  `desconto_vend` decimal(10,2) DEFAULT '0.00',
  `quant_parcela_vend` int DEFAULT NULL,
  `descricao_vend` varchar(200) DEFAULT NULL,
  `status_vend` varchar(200) DEFAULT NULL,
  `id_serv_fk` int DEFAULT NULL,
  `id_prod_fk` int DEFAULT NULL,
  `id_fun_fk` int DEFAULT NULL,
  `id_cli_fk` int DEFAULT NULL,
  `id_caixa_fk` int DEFAULT NULL,
  `data_cancelamento` datetime DEFAULT NULL,
  `motivo_cancelamento` varchar(300) DEFAULT NULL,
  `usuario_cancelamento` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`id_vend`),
  KEY `id_serv_fk` (`id_serv_fk`),
  KEY `id_prod_fk` (`id_prod_fk`),
  KEY `id_fun_fk` (`id_fun_fk`),
  KEY `id_cli_fk` (`id_cli_fk`),
  KEY `idx_venda_data` (`data_vend`),
  KEY `idx_venda_caixa` (`id_caixa_fk`),
  CONSTRAINT `venda_ibfk_1` FOREIGN KEY (`id_serv_fk`) REFERENCES `servico` (`id_serv`),
  CONSTRAINT `venda_ibfk_2` FOREIGN KEY (`id_prod_fk`) REFERENCES `produto` (`id_prod`),
  CONSTRAINT `venda_ibfk_3` FOREIGN KEY (`id_fun_fk`) REFERENCES `funcionario` (`id_fun`),
  CONSTRAINT `venda_ibfk_4` FOREIGN KEY (`id_cli_fk`) REFERENCES `cliente` (`id_cli`)
) ENGINE=InnoDB AUTO_INCREMENT=14 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `venda_item` (
  `id_vend_item` int NOT NULL AUTO_INCREMENT,
  `id_vend_fk` int NOT NULL,
  `tipo_item` varchar(10) NOT NULL,
  `id_ref` int DEFAULT NULL,
  `descricao` varchar(200) NOT NULL,
  `quantidade` int NOT NULL DEFAULT '1',
  `valor_unit` decimal(10,2) NOT NULL DEFAULT '0.00',
  `valor_total` decimal(10,2) NOT NULL DEFAULT '0.00',
  `comissao` decimal(10,2) NOT NULL DEFAULT '0.00',
  `coberto_plano` tinyint(1) NOT NULL DEFAULT '0',
  `id_saldo_fk` int DEFAULT NULL,
  PRIMARY KEY (`id_vend_item`),
  KEY `idx_venda_item_venda` (`id_vend_fk`),
  CONSTRAINT `fk_venda_item_venda` FOREIGN KEY (`id_vend_fk`) REFERENCES `venda` (`id_vend`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=17 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;


-- ---------------------------------------------------------------
-- Usuario de acesso
--
-- Nao vai versionado de proposito: senha, mesmo em hash, nao entra em
-- repositorio publico. A senha e guardada como SHA-256 em hexadecimal,
-- sem sal (veja UsuarioDAO.HashSenha).
--
-- Troque SUA_SENHA e rode:
--
-- INSERT INTO usuario (nome_usr, email_usr, senha_usr, ativo_usr, data_criacao_usr)
-- VALUES ('admin', 'voce@exemplo.com', SHA2('SUA_SENHA', 256), 1, NOW());
-- ---------------------------------------------------------------
