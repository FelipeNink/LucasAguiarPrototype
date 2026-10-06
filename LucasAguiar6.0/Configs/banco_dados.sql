-- Adiciona os planos
create table plano(
id_plan int primary key auto_increment,
nome_plan varchar(200),
descricao_plan varchar(200),
valor_plan float
);

-- Adiciona os funcionarios 

create table funcionario(
id_fun int primary key auto_increment,
nome_fun varchar(100),
telefone_fun varchar(14),
cpf_fun varchar(14),
data_nasc_fun date,
ctps_fun varchar(12),
rg_fun varchar(7),
email_fun varchar(100),
estado_fun varchar(200),
cidade_fun varchar(200),
bairro_fun varchar(200),
rua_fun varchar(200),
numero_fun varchar(200)
);

-- Adiciona os serviços que a barbearia oferece (corte, sobrancelha, barba, luzes, etc...)

create table servico (
  id_serv int primary key auto_increment,
  nome_serv varchar(100),
  preco_serv float,
  duracao_min int,
  comis_funcionario_cli float
);

-- Adiciona Fornecedor

create table fornecedor(
id_forn int primary key auto_increment,
nome_forn varchar(200),
email_forn varchar(200),
telefone_forn varchar(14),
tipo_prod_forn varchar(300)
);

-- Adiciona Produtos

create table produto(
id_prod int primary key auto_increment,
nome_prod varchar(200),
quantidade_prod int,
valor_prod float,
descricao_prod varchar(200),
marca_prod varchar(200),
id_forn_fk int,
foreign key (id_forn_fk) references fornecedor (id_forn)
);

-- Adiciona os clientes

create table cliente(
id_cli int primary key auto_increment,
nome_cli varchar(100),
telefone_cli varchar(14),
cpf_cli varchar(14),
data_nasc_cli date,
rg_cli varchar(7),
estado_cli varchar(200),
cidade_cli varchar(200),
bairro_cli varchar(200),
rua_cli varchar(200),
numero_cli varchar(200),
id_plan_fk int,
foreign key (id_plan_fk) references plano (id_plan)
);

-- Adicionar vendas

create table venda(
id_vend int primary key auto_increment,
valor_vend float,
data_vend date,
quantidade_prod_vend int,
quantidade_serv_vend int,
forma_pagamento_vend varchar(200),
desconto_vend float,
quant_parcela_vend int,
descricao_vend varchar(200),
status_vend varchar(200),
id_serv_fk int,
id_prod_fk int,
id_fun_fk int,
id_cli_fk int,
foreign key (id_serv_fk) references servico(id_serv),
foreign key (id_prod_fk) references produto(id_prod),
foreign key (id_fun_fk) references funcionario(id_fun),
foreign key (id_cli_fk) references cliente(id_cli)
);

-- Adicionar compras

create table compras(
id_comp int primary key auto_increment,
data_comp date,
valor_comp varchar(200),
item_comp varchar(200),
quantidade varchar(200),
id_prod_fk int,
id_forn_fk int,
id_fun_fk int,
foreign key (id_prod_fk) references produto(id_prod),
foreign key (id_forn_fk) references fornecedor(id_forn),
foreign key (id_fun_fk)  references funcionario(id_fun)
);




