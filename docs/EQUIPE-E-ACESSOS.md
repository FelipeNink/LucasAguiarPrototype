# Equipe, Acessos e Permissões

Como o controle de acesso do sistema funciona, e o que fazer quando
precisar mexer nele. O relatório da descoberta que originou este módulo
está em [FASE-0-EQUIPE-E-ACESSOS.md](FASE-0-EQUIPE-E-ACESSOS.md).

---

## 1. As peças

| Arquivo | O que faz |
|---|---|
| `Configs/Catalogo.cs` | **Fonte única** das telas e ações. Quem lê: a matriz, o menu, as guardas de página, os endpoints de download e os testes |
| `Configs/SegurancaSenha.cs` | PBKDF2, política de senha e token de sessão |
| `Data/SessaoDAO.cs` | Sessões no banco: criar, validar, encerrar |
| `Data/UsuarioDAO.cs` | Entrar no sistema e trocar a própria senha |
| `Data/EquipeDAO.cs` | Operações de quem administra, com as quatro proteções |
| `Data/PermissaoDAO.cs` | Ler e gravar a matriz |
| `Data/FuncaoDAO.cs` | Cargos |
| `Data/LogDAO.cs` | Auditoria |
| `Data/TentativaLoginDAO.cs` | Freio de força bruta |
| `Services/AutenticacaoPorSessao.cs` | O esquema de autenticação por token |
| `Services/Autorizacao.cs` | O que a pessoa logada pode fazer |
| `Services/PoliticasDeTela.cs` | As políticas `tela:chave:Acao` e a exigência por rota |

---

## 2. Como criar o primeiro acesso com segurança

Em um banco vazio, o `BancoInicializador` cria o primeiro usuário a
partir de duas variáveis de ambiente, e **só** se a tabela estiver sem
ninguém:

```
ADMIN_USUARIO=nome.do.dono
ADMIN_SENHA=uma senha forte, trocada no primeiro acesso
ADMIN_EMAIL=opcional
```

Esse acesso é promovido a administrador e a super-administrador
automaticamente, porque sem nenhum admin o sistema não teria como
conceder permissão a ninguém.

Não existe usuário padrão. Se as variáveis não forem definidas, a
aplicação sobe, avisa no log e ninguém entra — que é melhor do que
subir com `admin/admin`.

---

## 3. Como adicionar uma tela nova ao catálogo

Três passos, nessa ordem:

**1. Declare a tela** em `Configs/Catalogo.cs`:

```csharp
new("estoque", "Estoque", Cadastros,
    Acao.Ver | Acao.Criar | Acao.Editar | Acao.Exportar,
    new[] { "/estoque", "/estoque/novo" },
    new[] { "/relatorio/estoque" }),
```

Declare só as ações que a tela realmente tem: a matriz mostra "—" nas
outras, e o servidor descarta o que não está declarado.

**2. Crie a constante da política** em `Services/PoliticasDeTela.cs`:

```csharp
public const string Estoque = "tela:estoque:Ver";
```

**3. Marque a página**:

```razor
@attribute [Authorize(Policy = Politicas.Estoque)]
```

Pronto. Na próxima subida, a sincronização cria a linha **negada** de
`estoque` para todos os usuários comuns; o menu, a matriz e a guarda da
rota já sabem da tela nova; e o endpoint `/relatorio/estoque` passa a
exigir `estoque:Exportar` sem nenhum código a mais.

Esquecer o passo 2 ou 3 faz o teste
`Toda_pagina_exige_a_politica_da_propria_tela` falhar.

---

## 4. Sessões

- O cookie `barbearia_sessao` leva só um token de 32 bytes. O banco
  guarda o **hash** dele.
- Quem é o dono do token é resolvido no banco **a cada requisição**,
  junto com a conferência de que a conta continua ativa. Por isso
  desativar alguém corta o acesso na requisição seguinte, e não no
  próximo login.
- Validade absoluta de 8 horas, sem renovação.
- Encerram a sessão: sair, desativar, excluir, redefinir a senha do
  usuário, e trocar a própria senha (que preserva a sessão de quem
  trocou).
- O ticket cifrado padrão do ASP.NET Core **não** é usado de propósito:
  as chaves do Data Protection morrem a cada deploy num container, e
  era isso que deslogava todo mundo a cada publicação.

---

## 5. Bloqueio por tentativas

Cinco falhas em quinze minutos bloqueiam por quinze, contadas **por
usuário e por IP** separadamente. A contagem fica na tabela
`tentativa_login`, e não em memória: reiniciar a aplicação não pode
servir de escapatória.

Acertar a senha zera a contagem. As linhas vencidas são apagadas na
subida.

Para destravar alguém imediatamente:

```sql
DELETE FROM tentativa_login WHERE chave = 'usr:nome.da.pessoa';
```

---

## 6. As quatro proteções

Conferidas no servidor, em `EquipeDAO`, e não escondendo botão:

1. Ninguém se desativa nem se exclui.
2. Ninguém desativa, exclui nem rebaixa a raiz.
3. O sistema nunca fica sem ao menos um administrador ativo.
4. Só a raiz concede ou retira o perfil de administrador.

A leitura do alvo usa `FOR UPDATE`: duas pessoas desativando o mesmo
acesso ao mesmo tempo não podem ler o mesmo estado e ambas concluírem
que ainda sobra administrador.

---

## 7. Senhas

- Formato atual: `pbkdf2$210000$sal$chave`, PBKDF2-SHA256.
- Senhas antigas (`sha256`) continuam valendo e são **regravadas no
  formato novo na primeira entrada**, sem a pessoa perceber. Quando não
  houver mais nenhuma linha em `sha256`, o ramo legado pode sair de
  `SegurancaSenha.Confere`.
- Política: mínimo 8 caracteres, com ao menos uma letra e um número.
- Senha provisória definida pelo administrador força a troca na entrada
  seguinte, e um middleware impede contornar a troca digitando outro
  endereço.

---

## 8. Rodar os testes

Os testes de catálogo, política e senha rodam sem banco:

```bash
dotnet test LucasAguiar.Tests
```

Os de sessão, bloqueio, login e Equipe precisam de um MySQL
**descartável** — nunca o de produção, porque criam e apagam usuários:

```bash
LUCASAGUIAR_TESTE_CONEXAO="Server=localhost;Database=barbearia_teste;User=root;Password=...;Port=3306" \
  dotnet test LucasAguiar.Tests
```

Sem a variável eles são ignorados com a explicação no relatório, e o
`dotnet test` continua passando numa máquina sem MySQL.

---

## 9. O que ficou de fora, e por quê

| Item | Decisão |
|---|---|
| Foto do membro | Fora. O disco do container é efêmero: a foto sumiria no deploy seguinte. A inicial colorida pelo nome resolve numa equipe pequena. Se for fazer, é por volume do Railway |
| Cartões no celular na lista de Equipe | Fora. O sistema inteiro usa colunas `.opcional` que somem abaixo de 760px; uma lista em cartão no meio de catorze listas em tabela destoaria |
| Modelos de permissão nomeados | Fora da primeira entrega. Com dezessete telas e uma barbearia, os atalhos de acesso total / somente leitura / limpar resolvem. Entram depois sem mexer no esquema |
| "Esqueci minha senha" por e-mail | Fora. Não há servidor de e-mail configurado, e o administrador redefine na hora |
| E2E com Playwright | Fora. A verificação medida no navegador cobre o essencial com muito menos infraestrutura |

---

## 10. Matriz de cobertura — o que foi entregue

Servidor · Tela · Teste · Auditoria. "—" significa que não se aplica.

| # | Funcionalidade | Srv | Tela | Teste | Aud. | Onde |
|---|---|:--:|:--:|:--:|:--:|---|
| 1 | Login / logout / sessão / expiração | ✔ | ✔ | ✔ | ✔ | `SessaoDAO`, 8 h absolutas |
| 2 | Bloqueio por tentativas | ✔ | ✔ | ✔ | ✔ | 5 falhas/15 min, por usuário e IP, no banco |
| 3 | Deep link e volta ao destino | ✔ | ✔ | — | — | `?voltar=`, validado como caminho interno |
| 4 | Conta desativada perde acesso na hora | ✔ | ✔ | ✔ | ✔ | Conferido na mesma consulta da sessão |
| 5 | Troca da própria senha | ✔ | ✔ | ✔ | ✔ | Encerra as outras sessões |
| 6 | Redefinição por admin + troca obrigatória | ✔ | ✔ | ✔ | ✔ | Middleware impede contornar |
| 7 | Criar membro atômico com permissões | ✔ | ✔ | ✔ | ✔ | Uma transação |
| 8 | Editar sem reativar; concorrência | ✔ | ✔ | ✔ | ✔ | `ativo_usr` fora do UPDATE |
| 9 | Ativar / desativar | ✔ | ✔ | ✔ | ✔ | Encerra sessões na mesma transação |
| 10 | Excluir com proteções | ✔ | ✔ | ✔ | ✔ | Histórico de negócio preservado |
| 11 | Lista: busca, ordenação, estados | ✔ | ✔ | — | — | Busca ignora acento e caixa |
| 12 | Lista no celular | — | ✔ | medido | — | Colunas `.opcional`, sem rolagem lateral |
| 13 | Exportação da equipe | ✔ | ✔ | ✔ | — | `/relatorio/equipe`, `/excel/equipe` |
| 14 | Admin vs comum; raiz | ✔ | ✔ | ✔ | ✔ | Cartão de admin só para a raiz |
| 15 | Catálogo único de telas e ações | ✔ | ✔ | ✔ | — | `Catalogo.cs` |
| 16 | Matriz (UI + regra do "Acessar") | ✔ | ✔ | ✔ | ✔ | Regra refeita no servidor ao gravar |
| 17 | Ações sensíveis desligadas | ✔ | ✔ | ✔ | — | Fora do "Acesso total" |
| 18 | Atalhos da matriz | — | ✔ | — | — | Total, leitura, limpar |
| 19 | Imposição em todas as rotas | ✔ | — | ✔ | ✔ | Política por página + exigência por rota |
| 20 | Menu e botões escondidos; URL bloqueada | ✔ | ✔ | medido | ✔ | Lateral, botões de criar, painel sem valores |
| 21 | Limpeza de estado ao trocar de usuário | ✔ | ✔ | — | — | Saída com `forceLoad` encerra o circuito |
| 22 | Último admin / auto-rebaixamento / auto-desativação | ✔ | ✔ | ✔ | ✔ | Três portas fechadas, com `FOR UPDATE` |
| 23 | Funções / Cargos | ✔ | ✔ | ✔ | ✔ | Vínculo por id; excluir em uso é barrado |
| 24 | Conta: senha, tema, sessões, sair | ✔ | ✔ | — | ✔ | Foto ficou de fora (ver §9) |
| 25 | Log de todos os eventos da seção J | ✔ | — | ✔ | ✔ | Sem senha, hash nem token |
| 26 | Tela de Logs | ✔ | ✔ | — | — | Filtros, paginação, detalhes |
| 27 | Segurança (seção K) | ✔ | — | ✔ | ✔ | PBKDF2, política, rate limit, sem enumeração |
| 28 | Mensagens em pt-BR | — | ✔ | — | — | Convenção do sistema |
| 29 | 1280 px e 375 px sem cortes | — | ✔ | medido | — | Controles lado a lado em 44px |
| 30 | Migração sem perda | ✔ | — | ✔ | — | Confirmada em produção: senha regravada |

**Falhas do sistema de referência que o prompt mandou não repetir:** as
dezessete estão fechadas. As que existiam também aqui — downloads
abertos a qualquer logado, desativar sem derrubar sessão, senha sem
sal, login sem freio, `nome_usr` sem índice único, zero auditoria —
estão descritas no relatório da Fase 0 com o arquivo e a linha de cada
uma.
