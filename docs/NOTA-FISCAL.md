# Nota fiscal (NFS-e Nacional)

Emissão de NFS-e pelas vendas, via API da [Focus NFe](https://doc.focusnfe.com.br/reference/nfse-nacional.md).
Fase 1: só serviço e plano. Produto (NFC-e) fica para a fase 2.

## Como funciona

1. Ao confirmar uma venda, se a emissão estiver ligada e houver valor tributável, a nota entra na fila
   (`nota_fiscal`, status `PENDENTE`) **na mesma transação da venda**. Nada é enviado nesse momento.
2. O `EmissorNotasFiscais` (serviço em segundo plano, ciclo de 20 s) envia a DPS para a Focus,
   consulta até sair a autorização e guarda número, link de consulta e PDF.
3. Se a prefeitura recusar, a nota fica em `ERRO` com o motivo. Depois de corrigir o cadastro,
   use **Reenviar** em *Notas fiscais*. Falha de rede ou da Focus só adia o envio.
4. Ao cancelar a venda, a nota é cancelada na prefeitura. Se ainda não tiver saído, ela é descartada.

### O que entra na nota

| Item da venda | NFS-e |
|---|---|
| Serviço avulso | Entra pelo valor cobrado |
| Plano vendido | Entra pelo valor pago (a nota sai no pagamento) |
| Serviço coberto por plano | Não entra (já foi tributado na venda do plano) |
| Produto | Não entra (fase 2, NFC-e) |

O desconto geral da venda é dividido proporcionalmente entre os itens e enviado como `desconto_incondicionado`.
O tomador só é identificado quando o cliente tem CPF válido; sem CPF, a nota sai sem tomador.

### Edição de venda

Se a venda tem uma nota na fila, em processamento ou autorizada, a edição fica **restrita** (só cliente e
profissional), e a nota não muda. Para corrigir valores, cancele a venda e lance outra.
Se a nota estiver em `ERRO`, a edição continua completa e a nota volta para a fila com os valores novos.

## Ativação

1. Crie a conta na Focus NFe e cadastre a empresa no painel dela, com o **certificado digital A1**.
2. No Railway, defina as variáveis de ambiente:
   - `Fiscal__TokenHomologacao`
   - `Fiscal__TokenProducao`
3. No sistema, abra *Financeiro > Notas fiscais > Configuração* e preencha CNPJ, código IBGE do município,
   regime do Simples e código de tributação (padrão `060101`, barbearia). Deixe em **Homologação**, ligue a emissão e faça algumas vendas de teste.
4. Com o contador, confirme alíquota, regime, códigos e o grupo IBS/CBS. Depois troque para **Produção**.

## Pendências

- **Salão-parceiro (Lei 13.352/2016):** o cadastro do funcionário já tem "Profissional-parceiro" e o CNPJ,
  mas a nota ainda sai inteira pelo salão. A divisão por cota-parte depende de confirmação do contador e da regra do município.
- **NFC-e de produtos:** fase 2.
- **IBS/CBS (reforma tributária):** só vai na nota quando CST e classificação estiverem preenchidos na configuração.
