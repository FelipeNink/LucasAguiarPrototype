# Fase 0 — Equipe, Acessos e Permissões

Relatório de descoberta do sistema de destino (Barbearia Lucas Aguiar),
matriz de cobertura, plano em etapas, riscos e as decisões que dependem
de você.

**Nada foi implementado.** Esta fase é somente leitura, como o prompt pede.
A única exceção: li a tabela `usuario` do banco de produção (um `SELECT`,
sem escrita) para poder dizer quantos usuários existem e como migrá-los.

---

## 1. Relatório de descoberta

### 1.1 Pilha e convenções

| Item | O que é |
|---|---|
| Linguagem / framework | C# 13, .NET 9, **Blazor Server** (`AddInteractiveServerComponents`, `Program.cs:38`) |
| Banco | MySQL 8, driver **MySqlConnector 2.4.0** (`LucasAguiar.csproj`) — **sem ORM**, SQL escrito à mão |
| Camada de dados | 17 DAOs em `LucasAguiar/Data/`, injetados como `Scoped` (`Program.cs:43-61`) |
| Front | Não existe front separado. Os componentes `.razor` **rodam no servidor**; o navegador recebe só o HTML e troca eventos pelo circuito SignalR |
| Build / deploy | `Dockerfile` na raiz, Railway (`railway.json`, `numReplicas: 1`, healthcheck `/health`) |
| Estilo | `wwwroot/app.css` (~1.100 linhas) com camada de tokens em `:root` e `[data-tema="escuro"]`; CSS escopado por componente (`*.razor.css`) |
| Tema | claro/escuro por `data-tema` no `<html>`, persistido em `localStorage` (`App.razor:23-33`, `wwwroot/js/ui.js:28-45`) |
| Fontes | Barlow (corpo) + Barlow Condensed (títulos e números) |
| Idioma | Interface 100% pt-BR; código e comentários em português sem acento |
| Componentes de UI prontos | `Janela` (modal), `ConfirmarModal`, `Seletor<T>` (dropdown próprio, substituiu todo `<select>` nativo), `AcoesLinha` (ver/editar/excluir em ícone), `Marca`, `CadastroRapido` |
| Padrão de tabela | `<div class="tabela fixa"><table class="table table-striped">`, coluna `.col-id`, colunas secundárias com `class="opcional"` (somem abaixo de 760px), `.col-acoes` |
| Padrão de barra de ações | `<div class="barra-acoes">` com `.btn-novo` + `.btn-pdf` |

### 1.2 Autenticação atual

| Pergunta | Resposta | Onde |
|---|---|---|
| Existe login? | Sim, por **nome de usuário** (não e-mail) | `Login.razor:1-118` |
| Como a senha é guardada? | **SHA-256 hex, sem sal, uma única passada** | `UsuarioDAO.cs:18-25` |
| Sessão | Cookie de sessão do ASP.NET Core, `IdleTimeout` de **30 min**, armazenamento **em memória** (`AddDistributedMemoryCache`) | `Program.cs:63-69` |
| O que guarda na sessão | Só `UsuarioId` e `UsuarioNome` | `Login.razor:106-107` |
| Expiração absoluta | Não existe — a sessão renova a cada clique, indefinidamente | — |
| Logout | `Session.Clear()`, sem nada no servidor para invalidar | `Logout.razor:17`, `AuthenticationService.cs:42-49` |
| Bloqueio por tentativas | **Não existe** | — |
| Tabela de usuários | `usuario` (6 colunas: id, nome, email, senha, ativo, data_criacao) | `banco_dados.sql:286-297` |
| Tabela de papéis / permissões | **Não existem** | — |
| Tabela de logs / auditoria | **Não existe** | — |
| Primeiro admin | Criado de `ADMIN_USUARIO` / `ADMIN_SENHA` só com a tabela vazia — **já está correto**, nada de `admin/admin` | `BancoInicializador.cs:125-157` |
| Perfil exibido na tela | A lateral escreve **"Administrador" fixo no HTML** para qualquer usuário | `NavMenu.razor:199` |

**Reaproveitável:** a tabela `usuario`, o `AuthenticationService`, o middleware
de bloqueio, o `Routes.razor`, o `Seletor`, o `ConfirmarModal`, o `Janela`,
o `AcoesLinha`, a camada de tokens do CSS e o `BancoInicializador` (que já
tem o lugar certo para migrações idempotentes).

**A trocar:** o hash de senha, o armazenamento de sessão, o
`ValidarLogin` e o "Administrador" fixo da lateral.

### 1.3 Falhas de acesso encontradas no destino

Estas são as do **destino**, não as do sistema de referência:

1. **`nome_usr` não tem índice único.** O login é por `nome_usr`
   (`UsuarioDAO.cs:38`), mas a única restrição da tabela é em `email_usr`
   (`banco_dados.sql:295`). Dois usuários podem nascer com o mesmo login.
2. **Desativar um usuário não derruba a sessão dele.** `ativo_usr` só é
   conferido no `WHERE` do login (`UsuarioDAO.cs:39`); o middleware olha
   apenas se existe `UsuarioId` na sessão (`Program.cs:113`). Quem já
   estava dentro continua dentro até a sessão expirar.
3. **Navegação interna do Blazor não passa pelo middleware.** O middleware
   de `Program.cs:100-122` só roda na requisição HTTP. Depois que o circuito
   liga, trocar de página acontece **dentro do circuito** — nenhum
   middleware é consultado. Qualquer regra de acesso precisa, portanto,
   existir também na própria página. (Detalhe central do plano; ver §2.)
4. **`Path.HasExtension(caminho)` libera a rota.** A linha 111 de
   `Program.cs` trata como pública qualquer URL com ponto no último
   segmento. Serve para os arquivos estáticos, mas é largo demais como
   regra de autenticação.
5. **Os 19 endpoints de download não checam nada além de estar logado.**
   12 em `RelatorioEndpoints.cs` (`/relatorio/*`) e 7 em `ExcelEndpoints.cs`
   (`/excel/*`). São a única superfície HTTP de dados do sistema e hoje
   entregam a planilha de vendas, comissões, despesas e clientes para
   qualquer sessão válida.
6. **Sessão em memória.** `AddDistributedMemoryCache` significa que todo
   deploy desloga todos, e que a sessão não existe fora daquela instância.
   Com `numReplicas: 1` funciona hoje; com duas réplicas, quebra.
7. **Senha sem sal e com uma só iteração de SHA-256.** Uma lista de hashes
   vazada é quebrada por tabela arco-íris em minutos.
8. **Nenhum evento é auditado.** Não há registro de login, de alteração de
   cadastro, de cancelamento de venda nem de pagamento de folha — e as
   telas gravam o *nome* do usuário em texto em alguns lugares
   (`CaixaDAO.Abrir(..., string? usuario)`, `VendaDAO.Cancelar(..., usuario)`,
   `folha_pagamento.usuario`), o que ajuda, mas não substitui um log.

Pontos onde o destino **já está melhor** que o sistema de referência:
a mensagem de erro do login é genérica (`Login.razor:95`), o primeiro admin
vem de variável de ambiente, e não existe usuário padrão conhecido.

### 1.4 Catálogo de telas e ações do destino

Este é o catálogo real, lido do `NavMenu.razor` e das diretivas `@page`.
É ele que vira a matriz de permissões.

| Seção | Chave | Tela | Rotas | Ações que fazem sentido |
|---|---|---|---|---|
| Operação | `painel` | Painel | `/` | Acessar, **Ver valores** |
| Operação | `vendas` | Vendas | `/vendas`, `/venda/nova` | Acessar, Criar, Editar¹, **Estornar**², **Exportar** |
| Operação | `agendamentos` | Agendamentos | `/agendamentos`, `/agendamento/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Operação | `caixa` | Caixa | `/caixa` | Acessar, **Liquidar**³, **Exportar** |
| Financeiro | `despesas` | Despesas | `/despesas` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Financeiro | `comissoes` | Comissões | `/comissoes` | Acessar, **Exportar** |
| Financeiro | `folha` | Folha de pagamento | `/folha` | Acessar, **Liquidar**⁴, **Exportar** |
| Financeiro | `compras` | Compras | `/compra`, `/compra/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `clientes` | Clientes | `/cliente`, `/cliente/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `servicos` | Serviços | `/servico`, `/servico/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `produtos` | Produtos | `/produto`, `/produto/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `planos` | Planos | `/plano`, `/plano/novo`, `/plano/{id}/componentes` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `funcionarios` | Funcionários | `/funcionario`, `/funcionario/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Cadastros | `fornecedores` | Fornecedores | `/fornecedores`, `/fornecedores/novo` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Sistema | `equipe` | Equipe *(nova)* | `/equipe`, `/equipe/novo`, `/equipe/{id}` | Acessar, Criar, Editar, Excluir, **Exportar** |
| Sistema | `funcoes` | Funções *(nova)* | `/funcoes` | Acessar, Criar, Editar, Excluir |
| Sistema | `logs` | Logs *(nova)* | `/logs` | Acessar, **Exportar** |

¹ Editar venda hoje é só trocar o profissional (`VendaDAO.TrocarProfissional`).
² Estornar = cancelar venda com motivo (`VendaDAO.Cancelar`).
³ Liquidar em Caixa = abrir, fechar, sangria e suprimento (`CaixaDAO.Abrir/Fechar/RegistrarMovimento`).
⁴ Liquidar em Folha = pagar o profissional, que gera despesa e movimento de caixa (`FolhaDAO.Pagar`).

Resultado: **17 telas** e **8 colunas** de ação — `Acessar`, `Criar`,
`Editar`, `Excluir` + 4 especiais (`Exportar`, `Estornar`, `Liquidar`,
`Ver valores`). Onde a ação não se aplica, a célula mostra "—".

### 1.5 Multi-empresa

Não. O sistema é de **uma** barbearia: não há coluna de empresa, unidade
ou tenant em nenhuma das 17 tabelas. Permissões são **globais**, sem
recorte por unidade.

### 1.6 Rotas HTTP existentes e como estão protegidas

| Superfície | Quantidade | Proteção hoje |
|---|---|---|
| Páginas Razor | 25 rotas | Middleware exige sessão **na primeira requisição**; navegação interna não passa por ele |
| `/relatorio/*` | 12 GET | Só sessão. **Sem checagem de permissão** |
| `/excel/*` | 7 GET | Só sessão. **Sem checagem de permissão** |
| `/health` | 1 GET | Pública (de propósito, é a sonda do Railway) |
| `/login`, `/logout`, `/Error` | 3 | Públicas (corretas) |
| `/_blazor`, `/_framework`, `/_content` | — | Públicas (infraestrutura) |

Não existe nenhuma API REST de negócio: as páginas chamam os DAOs
direto, em processo. Isso é importante para o plano — ver §2.

### 1.7 Dados existentes (produção)

Consultado em 16/10/2026 no banco MySQL do Railway (somente leitura):

```
id_usr  nome_usr      email_usr            ativo_usr  data_criacao_usr
2       AdminBarber   AdminBarber@local    1          2026-10-08 19:59:05
```

**Um único usuário**, ativo, criado pelo `BancoInicializador`. Nenhum
perfil, nenhuma permissão, nenhuma função. Para referência: 4 funcionários
e 14 vendas no mesmo banco.

**Migração sem perda:** `AdminBarber` recebe `perfil_usr = 'ADMIN'` e
`raiz_usr = 1` (é o super-administrador). A matriz é ignorada para admin,
então ele não precisa de linha em `permissao`. A senha dele **continua
funcionando**: ver o mecanismo de atualização transparente em §2.2. Nenhum
registro de negócio é tocado.

---

## 2. Como fica no destino — as três decisões de arquitetura

### 2.1 "Servidor é a fonte da verdade" em Blazor Server

O prompt separa *front* e *backend* porque o sistema de referência é uma
SPA com API REST. **Aqui não existe essa separação**: o componente `.razor`
é código C# que executa no servidor; o navegador só recebe HTML e devolve
eventos. Uma checagem dentro da página **é** uma checagem de servidor —
não há como o navegador contorná-la, porque o `if` nunca sai da máquina.

A imposição fica, então, em quatro camadas:

1. **Middleware HTTP** (`Program.cs`) — valida o token de sessão contra o
   banco, confere `ativo_usr` a cada requisição e aplica `ver` na rota
   pedida. Pega o acesso direto por URL e o F5.
2. **Guarda na página** — cada página confere `ver` no `OnInitialized`.
   Obrigatória, porque a navegação interativa do Blazor **não passa** pelo
   middleware (§1.3 item 3).
3. **Guarda no DAO** — os métodos que escrevem (`Inserir`, `Atualizar`,
   `Excluir`, `Cancelar`, `Pagar`, `Fechar`...) exigem a ação
   correspondente antes de tocar o banco. É a camada que torna impossível
   um botão esquecido virar brecha.
4. **Endpoints de download** — `/relatorio/*` e `/excel/*` ganham
   `exportar` da tela correspondente. É a única superfície HTTP de dados
   e hoje está aberta.

Com as quatro, o item K.1 do prompt ("autorização no servidor em 100% das
rotas") fica cumprido de verdade, e não só escondendo botões.

### 2.2 Senha: migrar sem deslogar ninguém

Trocar SHA-256 por PBKDF2 de uma vez invalidaria a senha do `AdminBarber`
— ninguém mais entraria no sistema em produção. O caminho sem perda:

1. `usuario` ganha a coluna `algo_senha_usr`, com default `'sha256'`.
2. O login tenta validar pelo algoritmo gravado na linha.
3. Se validou e o algoritmo era `sha256`, **o próprio login regrava** a
   senha como PBKDF2-SHA256 (210.000 iterações, sal de 16 bytes) e muda a
   coluna para `pbkdf2`. A pessoa não percebe nada.
4. Toda senha nova nasce `pbkdf2`. O ramo `sha256` existe só para a
   primeira entrada de cada usuário antigo.

Comparação em tempo constante com `CryptographicOperations.FixedTimeEquals`.

### 2.3 Sessão revogável

O cookie do ASP.NET Core continua sendo o transporte, mas o que ele
carrega passa a ser um **token de 32 bytes** cujo SHA-256 vive na tabela
`sessao`. Em cada requisição o middleware busca a linha, confere
`expira_em` e `ativo_usr`. Com isso:

- desativar, excluir ou redefinir a senha de alguém **derruba a sessão na
  hora** (basta marcar `encerrada_em`);
- trocar a própria senha derruba as outras sessões;
- existe uma tela "Minhas sessões" possível, com IP e navegador;
- deploy não desloga mais ninguém (a sessão está no banco, não na memória);
- expiração absoluta de 8 h passa a existir.

---

## 3. Modelo de dados proposto (aditivo)

Nomes seguindo a convenção do destino (snake_case, sufixo por tabela).

**`usuario` — só `ADD COLUMN`, nenhuma coluna existente muda:**

| Coluna | Tipo | Default |
|---|---|---|
| `perfil_usr` | `varchar(10)` | `'COMUM'` — `ADMIN` ou `COMUM` |
| `raiz_usr` | `tinyint(1)` | `0` — super-administrador |
| `trocar_senha_usr` | `tinyint(1)` | `0` |
| `algo_senha_usr` | `varchar(12)` | `'sha256'` |
| `id_funcao_fk` | `int NULL` | — |
| `avatar_usr` | `varchar(255) NULL` | caminho do arquivo, **não** base64 |
| `ultimo_login_usr` | `datetime NULL` | — |
| `atualizado_usr` | `datetime` | `CURRENT_TIMESTAMP ON UPDATE` — detecta edição concorrente |

Mais um `UNIQUE KEY` em `nome_usr` (corrige §1.3 item 1).

**Tabelas novas:**

| Tabela | Colunas |
|---|---|
| `funcao` | `id_funcao`, `nome_funcao` (único, sem diferenciar maiúsculas) |
| `sessao` | `id_sessao`, `token_hash` (único), `id_usr_fk`, `criado_em`, `expira_em`, `encerrada_em`, `ip`, `agente` |
| `permissao` | `id_usr_fk`, `tela`, `ver`, `criar`, `editar`, `excluir`, `exportar`, `estornar`, `liquidar`, `ver_valores` — PK `(id_usr_fk, tela)` |
| `log_sistema` | `id_log`, `quando`, `tipo`, `nivel`, `acao`, `descricao`, `id_usr_fk`, `nome_gravado`, `ip`, `alvo`, `meta` (JSON) |
| `tentativa_login` | `chave` (PK — usuário ou IP), `contagem`, `bloqueado_ate`, `atualizado_em` |

Tudo com `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` dentro
do `BancoInicializador`, que já roda a cada subida e já é o lugar dessas
migrações (`BancoInicializador.cs:91-118`).

**Sincronização automática:** na subida, para cada usuário `COMUM` e cada
tela do catálogo sem linha em `permissao`, insere a linha **negada**. Uma
tela nova nunca nasce acessível por esquecimento, e a matriz nunca aparece
incompleta.

---

## 4. Matriz de cobertura — como será no destino

| # | Funcionalidade | Servidor | Tela | Teste | Auditoria |
|---|---|---|---|---|---|
| 1 | Login / logout / sessão / expiração | Token em `sessao`, 8 h absolutas, validado a cada requisição | Vitrine de login atual, sem mudança visual | Unitário: token válido/expirado/encerrado | `login`, `logout` |
| 2 | Bloqueio por tentativas + 429 | `tentativa_login` no banco: 5 falhas/15 min por usuário **e** por IP | "Muitas tentativas. Tente de novo em N minutos." | Unitário: 6ª tentativa bloqueia; acerto zera | `login_falhou`, `login_bloqueado` |
| 3 | Deep link e volta ao destino | Middleware grava a URL pedida e redireciona a `/login` | Após entrar, volta ao endereço pretendido | Unitário do middleware | — |
| 4 | Conta desativada perde acesso na hora | `ativo_usr` conferido a cada requisição + `encerrada_em` nas sessões | Cai no login com aviso próprio | Unitário | `usuario_desativado`, `sessoes_encerradas` |
| 5 | Troca da própria senha | Exige senha atual, política, encerra as outras sessões | Seção recolhível em "Minha conta" | Unitário | `senha_alterada` |
| 6 | Redefinição por admin + troca obrigatória | `trocar_senha_usr = 1`, sessões do alvo encerradas | Campo no formulário do membro | Unitário | `senha_redefinida` |
| 7 | Criar membro (atômico com permissões) | **Uma transação**: usuário + 17 linhas de permissão | Formulário em duas colunas | Unitário: falha na matriz não deixa usuário órfão | `usuario_criado` + diff |
| 8 | Editar sem reativar; concorrência | `ativo_usr` **não** entra no UPDATE; `atualizado_usr` comparado | Aviso "alterado por outra pessoa" em vez de sobrescrever | Unitário | `usuario_editado` (campos) |
| 9 | Ativar / desativar | Ação própria, com confirmação | Botão por linha | Unitário | `usuario_ativado` / `_desativado` |
| 10 | Excluir com proteções | Barra a si mesmo, o raiz e o último admin ativo | Confirmação forte | Unitário dos 3 casos | `usuario_excluido` (nome antes de apagar) |
| 11 | Lista: busca, ordenação, estados | Busca sem diferenciar acento/maiúscula; ordenação no servidor | Cabeçalhos ordenáveis com seta; vazio/erro/carregando | Manual medido | — |
| 12 | Lista no celular | — | Colunas `.opcional` somem a 760px, como as outras listagens do sistema (ver pergunta 10) | Medido a 375px | — |
| 13 | Exportação da equipe | `/relatorio/equipe` + `/excel/equipe` com `equipe:exportar` | Botão "Relatório PDF" no padrão `.btn-pdf` | Unitário: 403 sem permissão | `exportou_equipe` |
| 14 | Admin vs comum; raiz | Matriz ignorada para admin; só o raiz muda perfil | Cartão "Administrador" só visível ao raiz | Unitário | `perfil_alterado` |
| 15 | Catálogo único de telas/ações | `CatalogoTelas.cs` — **uma** fonte usada por matriz, middleware, DAOs, menu e testes | Menu e matriz leem dele | Unitário: toda rota tem entrada | — |
| 16 | Matriz de permissões (UI + "Acessar") | Salva em transação com diff | Interruptores agrupados por seção, seção recolhível | Manual medido | `permissoes_atualizadas` (diff) |
| 17 | Ações especiais desligadas | `Estornar`, `Liquidar`, `Ver valores`, `Exportar` nascem `0` | — | Unitário | — |
| 18 | Atalhos da matriz | — | "Acesso total", "Somente leitura", "Limpar" | — | — |
| 19 | Imposição em todas as rotas | As 4 camadas de §2.1, inclusive os 19 GET de download | — | Unitário por rota: sem permissão → recusa | `acesso_negado` (`warn`) |
| 20 | Menu/botões escondidos e URL bloqueada | — | Itens da lateral, `.btn-novo`, `.btn-pdf` e `AcoesLinha` condicionados | Manual medido | — |
| 21 | Limpeza de estado ao trocar de usuário | Logout encerra o circuito com `forceLoad` | Nada de cache entre usuários | Manual | — |
| 22 | Último admin / auto-rebaixamento / auto-desativação | Conferido **no servidor**, não só escondendo botão | Mensagem clara da recusa | Unitário dos 3 | `acesso_negado` |
| 23 | Funções / Cargos | CRUD; renomear propaga por `id_funcao_fk` (vínculo por id, não texto); excluir em uso é barrado com a contagem | Tela `/funcoes` + criar inline no formulário | Unitário | `funcao_criada/renomeada/excluida` |
| 24 | Minha conta | Foto em arquivo, senha, tema, sair | Engrenagem no bloco do usuário da lateral | Manual | `avatar_alterado` |
| 25 | Log de todos os eventos da seção J | `log_sistema` | — | Unitário: cada ação grava | — |
| 26 | Tela de Logs | Filtros Tipo/Nível/Busca, paginação, permissão própria | Badges, "Ver detalhes" expansível | Manual | — |
| 27 | Segurança (seção K) | PBKDF2, política de senha, rate limit, sem enumeração, `UseForwardedHeaders` já configurado (`Program.cs:30-35`), HSTS já ativo (`Program.cs:91`) | — | Unitário | — |
| 28 | Mensagens em pt-BR | — | Já é a convenção do sistema | — | — |
| 29 | 1280 px e 375 px sem corte | — | Mesma altura de controle (`--altura-controle: 44px`) | **Medido** no navegador e relatado | — |
| 30 | Migração sem perda | `IF NOT EXISTS` + senha com atualização transparente | — | Testado contra cópia do banco | — |

As linhas 11, 12, 16, 20, 21, 24, 26 e 29 são verificação **medida no
navegador** (altura computada, `scrollWidth` vs `clientWidth`), não
"olhei e parece bom" — como combinado nas alterações anteriores.

---

## 5. Plano em etapas

Uma etapa por commit, verificada antes da seguinte.

| Etapa | O que entra | Por que nessa ordem |
|---|---|---|
| 1 | `CatalogoTelas.cs` (fonte única) + migrações aditivas + sincronização de permissões | Sem o catálogo, nada mais tem contra o que conferir |
| 2 | `SegurancaSenha` (PBKDF2 + atualização transparente) + tabela `sessao` + login/logout por token + bloqueio por tentativas + redirecionamento com deep link | O alicerce. Depois disto o sistema já está mais seguro que hoje, mesmo sem a tela de Equipe |
| 3 | Serviço `Autorizacao` + as 4 camadas de imposição (middleware, páginas, DAOs, endpoints de download) | A regra passa a valer antes de existir UI para configurá-la — ordem que impede janela de brecha |
| 4 | DAO de usuários: criar/editar/ativar/desativar/excluir/senha/permissões, tudo atômico, com as proteções (último admin, si mesmo, raiz) | — |
| 5 | `log_sistema` + registro de todos os eventos da seção J | Antes das telas, para que as telas já nasçam auditadas |
| 6 | Tela `/equipe`: lista, formulário, matriz, ativar/desativar/excluir | — |
| 7 | `/conta` (minha conta) e `/funcoes` | — |
| 8 | `/logs` | — |
| 9 | Acabamento: 1280 px e 375 px medidos, foco visível, `aria-label`, textos | Como nas alterações anteriores: conferência de estética e fontes antes de dar por pronto |

Etapas 1 a 3 mexem em `Program.cs`, nos 17 DAOs e nas 25 páginas. É a
parte que colide com o trabalho do lucasolliver — ver risco 3.

---

## 6. Riscos

1. **Senha do `AdminBarber`.** Se a troca de hash for feita sem a
   atualização transparente de §2.2, ninguém entra em produção e só uma
   escrita manual no banco resolve. Mitigado pelo desenho; será o primeiro
   teste da etapa 2.
2. **Um deslogamento único.** Ao passar a sessão para a tabela `sessao`, o
   cookie antigo deixa de valer. Com um usuário só, é um login a mais.
3. **Conflito com o lucasolliver.** As etapas 1-3 tocam `Program.cs`,
   todos os DAOs e todas as páginas. Se ele estiver mexendo em
   Agendamentos (foi o que ele subiu por último), o merge dói. Mitigação:
   branch própria e merge por etapa — ver pergunta 1.
4. **25 páginas com guarda a mais.** É mudança mecânica, mas é em 25
   arquivos: cada um é uma chance de errar. Mitigação: um componente
   `<Protegido Tela="vendas">` que embrulha o conteúdo, em vez de código
   solto em cada `OnInitialized`.
5. **Esconder botões em todas as telas** é a parte mais fácil de ficar
   incompleta. O catálogo único e o teste por rota são a rede.
6. **Sem projeto de testes.** Hoje não existe nenhum. O prompt pede teste
   automatizado por rota — ver pergunta 3.
7. **Upload de foto.** O container do Railway tem disco efêmero: foto
   gravada em arquivo desaparece no deploy seguinte. Ou vai para um volume
   do Railway, ou volta para o banco (como blob, carregado sob demanda —
   não base64 lido a cada requisição, que é a falha 14 do referência).
   Ver pergunta 9.

---

## 7. Perguntas — com a minha recomendação em cada

As quatro primeiras travam o início. Se você só responder "aprovado",
adoto todas as recomendações marcadas.

**1. Branch própria ou direto na `main`?**
O prompt pede branch própria; sua instrução permanente é subir cada
alteração para a `main`. *Recomendação: branch `equipe-acessos`, com push
a cada etapa, e merge na `main` quando a etapa 6 estiver de pé — assim
você acompanha pelo GitHub e a `main` não fica meio protegida.*

**2. Onde desenvolver e testar?**
O prompt pede banco local/descartável; você pediu para eu parar de rodar
o sistema local e manter os dados no banco definitivo. *Recomendação:
desenvolver contra um MySQL local descartável (cópia do esquema, dados de
teste), subir para o Railway ao fim de cada etapa e conferir lá. Os dados
de verdade continuam só em produção. Preciso da sua confirmação porque
isso significa rodar o sistema local durante o desenvolvimento.*

**3. Quanto teste automatizado?**
Não existe projeto de testes hoje. *Recomendação: criar
`LucasAguiar.Tests` (xUnit) cobrindo o motor de permissão, as proteções
(último admin, auto-exclusão, raiz, usuário duplicado, política de senha,
bloqueio por tentativas, encerramento de sessão) e uma asserção de que
toda rota do catálogo tem imposição. E2E com Playwright eu deixaria de
fora: é mais infraestrutura que valor neste projeto, e a verificação
medida no navegador cobre o essencial.*

**4. Modelo de permissão.**
*Recomendação: tela × ação, como no referência, com os atalhos "Acesso
total" / "Somente leitura" / "Limpar". Sem modelos nomeados reutilizáveis
na primeira entrega — com 17 telas e uma barbearia, o atalho resolve; se
depois virar incômodo, modelos entram sem mexer no esquema.*

**5. Login por usuário ou e-mail?**
*Recomendação: manter **usuário** (é o que existe e o que `AdminBarber`
usa), com índice único novo. `email_usr` continua opcional e serve só
para contato. Sem SSO.*

**6. Política de senha e bloqueio.**
*Recomendação: mínimo 8 caracteres com ao menos uma letra e um número,
sem expiração obrigatória; bloqueio de 15 min após 5 falhas em 15 min,
por usuário e por IP.*

**7. Senha provisória.**
*Recomendação: o admin define a senha e "exigir troca no primeiro acesso"
vem **marcado** por padrão. Sem "esqueci minha senha" por e-mail — o
sistema não tem servidor de e-mail configurado e o admin redefine na hora.*

**8. Excluir ou desativar?**
*Recomendação: **desativar** é o padrão; excluir existe mas é bloqueado
para si mesmo, para o raiz e para o último admin ativo. Registros de
negócio nunca são apagados em cascata — a venda continua com o nome
gravado.*

**9. Foto do membro.**
*Recomendação: deixar de fora da primeira entrega e usar a inicial
colorida determinística (a lateral já faz isso em `NavMenu.razor:195`).
Evita a questão do disco efêmero e não custa nada em usabilidade numa
equipe de 4 pessoas. Se você quiser foto, eu faria por volume do Railway.*

**10. Lista de Equipe no celular: cartões ou colunas que somem?**
O prompt pede cartões; o sistema inteiro usa `.opcional` escondendo
colunas abaixo de 760 px. *Recomendação: seguir a convenção do destino
(colunas `.opcional`), porque o prompt manda manter o design system
daqui — e uma lista em cartão no meio de 14 listas em tabela fica
estranha.*

**11. Função/Cargo: onde vive?**
`funcionario` (o profissional da barbearia) e `usuario` (quem tem login)
são tabelas diferentes hoje, e isso corresponde ao par Pessoas × Equipe
do prompt. *Recomendação: `funcao` nova, referenciada por `usuario`; os
funcionários continuam como estão. Mas vale perguntar: **um barbeiro deve
poder entrar no sistema?** Se sim, faz sentido ligar `usuario` a
`funcionario` por id, para que a comissão e o login sejam a mesma pessoa.
Minha recomendação é ligar — campo opcional "este acesso pertence ao
profissional X" — porque já existe tela de Comissões por profissional.*

**12. Quem é o raiz?**
*Recomendação: `AdminBarber` (único usuário existente), por flag
`raiz_usr` no banco, nunca por nome fixo no código.*

---

## 8. O que eu não vou fazer sem você pedir

- Nada em produção: nem banco, nem variáveis, nem deploy.
- Nenhum `DROP`, `TRUNCATE` ou `DELETE` sem `WHERE`.
- Nenhuma senha, token ou URL de banco em arquivo versionado.

**Fim da Fase 0. Aguardo sua aprovação do plano e as respostas das
perguntas 1 a 4 para começar a Fase 1.**
