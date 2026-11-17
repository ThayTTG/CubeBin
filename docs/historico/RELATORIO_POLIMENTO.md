# Relatório de Polimento — CubeBin

Auditoria completa do projeto (tela por tela e arquivo por arquivo), feita a pedido, cobrindo
consistência visual, qualidade de código e validação dos fluxos críticos de comportamento.
Este documento registra o que foi **encontrado**, o que foi **corrigido**, e o que fica em
**aberto para decisão**.

---

## 1. Fluxos de comportamento (prioridade mais alta)

Os três fluxos apontados foram conferidos linha a linha contra a implementação real:

| Fluxo | Onde | Resultado |
|---|---|---|
| Backup em CSV antes de deletar | `BtnExcluirDefinitivamente_Click`, `BtnExcluirAprovadas_Click` (Deletar/Sugestões) | ✅ Confirmado: backup é gerado (com fallback de confirmação se falhar) antes de qualquer exclusão real |
| Confirmação antes de exclusão irreversível | Mesmos dois métodos + `EnviarArquivosParaNuvemEExcluirAsync` | ✅ Confirmado: `MessageBox` com `MessageBoxResult.No` como padrão em todos os três pontos de exclusão do app |
| Não deletar local antes de confirmar upload | `BackupNuvemService.EnviarEDeletarAsync` | ✅ Confirmado: a exclusão local só entra no código depois do `await EnviarArquivoComLinkAsync` retornar com sucesso; qualquer exceção antes disso interrompe o método sem tocar em nada local |

Nenhum gap encontrado nos três — a lógica bate exatamente com o que foi especificado nas rodadas
anteriores. Um detalhe pequeno de **precisão de relatório** (não de segurança) foi corrigido no
meio do caminho: em `BackupNuvemService`, um arquivo que já não existisse mais no disco no momento
da exclusão local (removido por outra coisa nesse meio-tempo) estava sendo contado como "deletado
com sucesso" mesmo sem nada ter sido de fato apagado. Agora esse caso não conta nem como sucesso
nem como falha.

---

## 2. Parte 1 — Borda branca residual (DWM)

`TemaJanelaEscura.cs` reescrito: além do `DWMWA_USE_IMMERSIVE_DARK_MODE` já existente, agora também
aplica `DWMWA_BORDER_COLOR` (34), `DWMWA_CAPTION_COLOR` (35) e `DWMWA_TEXT_COLOR` (36), todos com a
cor de fundo/texto do tema (`#0D0D0D` / `#EDEDED`). As chamadas continuam condicionadas à existência
do HWND (via `SourceInitialized` quando a janela ainda não foi criada), evitando que sejam
ignoradas silenciosamente — exatamente como pedido. Os três atributos novos são "melhor esforço":
falham silenciosamente (sem exceção) em builds do Windows que não os suportam, e isso é esperado.

---

## 3. O que foi encontrado e corrigido, tela por tela

### Organizar
Sem achados — as duas sub-abas (Regras de extensão, Pendências) já estavam consistentes entre si.
Margem interna padronizada de `12` para `16` (ver seção 4).

### Limpar
Sem achados de lógica. Margem padronizada de `8` para `16`.

### Deletar
- **Estado vazio fora do padrão estabelecido**: tinha só uma linha de texto (`FontSize="13"`,
  opacidade 0.9), enquanto Sugestões/Cubos/Histórico seguem "título curto em negrito + linha de
  contexto". Corrigido para o mesmo padrão: título "Arraste arquivos aqui" + subtítulo explicando
  a alternativa dos botões.
- Botões `Excluir definitivamente` e `Enviar à nuvem e excluir` tinham `Padding="10,6"`, destoando
  do padrão do resto do app. Padronizado para `8,4`.
- Margem padronizada de `8` para `16`.

### Sugestões
Já era a referência de padrão visual (título+contexto+botão) — usada como base para corrigir as
outras telas. Único ajuste: o botão do estado vazio (`BtnAnalisarAgoraVazio`) tinha `Padding="10,6"`
diferente do botão equivalente no topo da tela; padronizado.

### Cubos
Já era referência de padrão visual, junto com Sugestões. Sem achados. Margem padronizada de `8`
para `16`.

### Histórico (absorve o Recall)
- Botões `Cancelar`, `Desfazer` e `Salvar` (herdados do antigo Recall) tinham `Padding="10,4"`,
  fora do padrão. Corrigido para `8,4`.
- Margem padronizada de `8` para `16`.
- Comportamento conferido: o filtro "Ver itens desta execução" (correlação por horário entre o CSV
  selecionado e os itens do Recall) funciona como documentado — ver item em aberto na seção 5.

### Configurações
Sem achados de lógica (herdou o conteúdo já testado da antiga sub-aba "Configurações gerais" sem
mudanças). Margem já estava em `16`, serviu de referência para padronizar as outras.

### Login
- Botão "Conectar ao Google Drive" tinha `Padding="10,6"`, corrigido para `8,4`.
- Margem padronizada de `20` para `16`.
- Sem achados de lógica — chip de conta conectada e ícone de nuvem já implementados corretamente
  na rodada anterior.

### Janelas auxiliares (SubpastasWindow, DetalhesPacoteWindow)
Ambas tinham botões com padding fora do padrão (`12,4` e `10,5` respectivamente) e margens de
container diferentes (`12` e já `16`). Todas padronizadas para `8,4` / `16`.

---

## 4. Consistência visual — resumo do que foi padronizado

- **Margem de container raiz**: todas as 9 abas da `MainWindow` + as 2 janelas auxiliares agora
  usam `Margin="16"` (antes havia uma mistura de `8`, `12`, `16` e `20` sem critério aparente).
- **Padding de botões**: todo botão do app (primário ou secundário) agora usa `Padding="8,4"`, que
  já era o padrão dominante (28 de ~34 ocorrências antes da correção). Os outliers (`10,4`, `10,6`,
  `12,4`, `10,5`) foram todos alinhados.
- **Hover/estado ativo da sidebar**: conferido que nenhuma das 8 abas principais tem `Style`
  explícito que sobrescreva o estilo implícito do `TabItem` — o hover (`SidebarHoverBrush`) e o
  estado ativo (barra vermelha + `SidebarActiveBrush`) se aplicam de forma idêntica em todas.
- **Ícones da sidebar**: tamanhos variam levemente (16×18 a 20×14) porque cada silhueta tem uma
  proporção natural diferente (nuvem é mais larga/baixa, relógio é mais quadrado) — isso é comum em
  sistemas de ícones reais e não foi considerado um problema. A margem entre ícone e texto (`10px`)
  é idêntica nas 8 abas.
- **Padrão de estado vazio** (título curto + linha de contexto + botão quando fizer sentido): agora
  aplicado de forma idêntica em Sugestões, Cubos, Histórico e Deletar.

---

## 5. Qualidade de código — o que foi encontrado e corrigido

### Achado sério: JSON corrompido derrubava o app inteiro na inicialização
`PendenciaService`, `RecallService`, `SugestaoService` e `PacoteBackupService` liam seus respectivos
arquivos (`pendencias.json`, `recall.json`, `sugestoes.json`, `pacotes.json`) sem nenhum tratamento
de erro. Como os quatro são carregados no construtor da `MainWindow`, um arquivo corrompido em
qualquer um deles (ex.: gravação interrompida por queda de energia ou o processo encerrado no meio
de uma escrita) derrubava o app **antes mesmo dele abrir**, sem nenhuma forma de recuperação pela
UI.

**Corrigido**: novo `ArquivoJsonUtils.CarregarListaResiliente<T>` (Core), usado pelos quatro
serviços. Se o JSON não puder ser lido/desserializado, o arquivo problemático é renomeado com um
sufixo `.corrompido-{timestamp}` (não perde o rastro dele, dá pra investigar depois) e o app segue
com uma lista vazia em vez de propagar a exceção.

### Achado: duplicação de código no `GoogleDriveService`
`EnviarArquivoAsync` e `EnviarArquivoComLinkAsync` tinham corpos quase idênticos (só mudava o tipo
MIME e os campos pedidos à API do Drive) — resultado de uma decisão consciente em uma rodada
anterior de não alterar o método já em uso, mas que deixou ~30 linhas duplicadas.

**Corrigido**: consolidados num método privado compartilhado (`EnviarArquivoInternoAsync`), mantendo
as duas assinaturas públicas exatamente como estavam (nenhum código que já as chama precisou mudar).

### Verificado e considerado adequado (sem mudanças)
- **Código morto**: varredura em todo o projeto (campos e métodos privados com uso único) não
  encontrou nada real — os "possíveis órfãos" identificados por uma checagem automática grosseira
  eram todos handlers ligados via `Click="..."` no XAML (que naturalmente não são "chamados" de
  dentro do próprio `.cs`), já validados como corretos em rodadas anteriores.
- **Comentários `TODO`/`FIXME`/`HACK`**: nenhum encontrado em todo o projeto.
- **Tratamento de erro em operações de arquivo**: todos os pontos de `File.Delete`/`Directory.Delete`
  usados nas exclusões (Deletar, Sugestões, backup na nuvem) já tinham `try/catch` por item, com
  relatório de falhas parciais ao usuário — isso já estava correto antes desta rodada.
- **Mensagens de exceção em inglês**: exceções do próprio .NET (`UnauthorizedAccessException` para
  permissão negada, `IOException` para disco cheio, etc.) chegam ao usuário com `ex.Message` no
  idioma original do runtime (inglês), mesmo com o resto da UI em português. Isso é uma decisão
  consciente de manter (traduzir toda a superfície de mensagens do .NET seria um esforço grande
  para um ganho pequeno), não um bug — mas fica registrado aqui como trade-off, não como "resolvido
  silenciosamente".

### Não mexido de propósito (documentado como decisão, não como pendência)
`BtnExcluirDefinitivamente_Click` (Deletar) e `BtnExcluirAprovadas_Click` (Sugestões) têm uma
estrutura muito parecida (confirmar → gerar backup → loop de exclusão com try/catch → relatório).
Poderiam ser consolidados num método compartilhado, como fiz com o fluxo de nuvem
(`EnviarArquivosParaNuvemEExcluirAsync`). Optei por **não mexer agora**: os dois métodos já
diferem em pontos reais (Deletar lida com Arquivo *e* Pasta via `Directory.Delete(recursive:true)`;
Sugestões só lida com arquivo e também atualiza o status da sugestão no JSON), e os dois já estão
testados e funcionando. Juntar os dois agora, no fim de uma auditoria grande, trocaria um ganho
cosmético de DRY por um risco real de mexer em fluxo de exclusão irreversível sem conseguir
compilar/testar aqui. Fica como sugestão pra uma próxima rodada, feita com calma e testada em
seguida.

### Documentação desatualizada
A seção "O que já está implementado" do `README.md` ainda descrevia a estrutura de **5 telas**
anterior à "Camada 1" (Pastas monitoradas, Regras de extensão, Configurações gerais, Pendências,
Histórico) — nunca tinha sido atualizada nas rodadas seguintes. Corrigida para refletir as 8 abas
atuais. Também removido da lista de "Próximos passos sugeridos" o item sobre o ícone do app, já que
isso foi implementado numa rodada anterior.

**Nota sobre `docs/spec.md`**: esse arquivo é um changelog cronológico (cada "Nota de Atualização"
é um registro histórico de uma versão específica). Entradas antigas mencionando a estrutura
anterior (ex.: "Configurações gerais" como sub-aba de Organizar) foram **mantidas como estão** de
propósito — são histórico, não documentação do estado atual. Só o `README.md` (que descreve o
estado presente) precisava de correção.

---

## 6. Itens em aberto — decisões que ficam com você

1. **Cor dos ícones da sidebar é fixa, não dinâmica.** Os 6 ícones monocromáticos (pasta, borracha,
   lixeira, lâmpada, relógio, engrenagem, nuvem) usam `TextPrimaryBrush` fixo, em vez de mudar de
   cor junto com o texto ao selecionar/passar o mouse. Essa foi uma decisão consciente da rodada
   anterior (evitar um binding via `RelativeSource`+`ContentPresenter` não testável sem compilar).
   Continua válida — não mexi nisso agora. Se quiser esse efeito, é uma mudança pequena e pontual.

2. **Correlação "Ver itens desta execução" (Histórico) é uma heurística, não um vínculo exato.**
   Filtra o Recall por proximidade de horário (±5 min) ao backup CSV selecionado, porque não existe
   um ID compartilhado gravado entre os dois arquivos. Funciona bem no caso comum, mas pode
   misturar itens de duas execuções muito próximas uma da outra. Se isso incomodar na prática, a
   correção correta seria gravar um `IdExecucao` compartilhado entre o CSV e as entradas do
   `recall.json` no momento em que ambos são gerados — mudança de schema, não fiz agora.

3. **Consolidação de `BtnExcluirDefinitivamente_Click`/`BtnExcluirAprovadas_Click`** — mencionado
   na seção 5, fica como sugestão de próxima rodada.

---

## 7. Arquivos alterados nesta rodada

**Core:**
- `Services/TemaJanelaEscura.cs` *(App, não Core — ver abaixo)*
- `Services/ArquivoJsonUtils.cs` — novo
- `Services/PendenciaService.cs`, `RecallService.cs`, `SugestaoService.cs`, `PacoteBackupService.cs`
  — leitura resiliente
- `Services/GoogleDriveService.cs` — deduplicação de upload
- `Services/BackupNuvemService.cs` — correção de contagem

**App:**
- `TemaJanelaEscura.cs` — DWMWA_BORDER_COLOR/CAPTION_COLOR/TEXT_COLOR
- `MainWindow.xaml` — padronização de margens/padding, estado vazio de Deletar
- `SubpastasWindow.xaml`, `DetalhesPacoteWindow.xaml` — padronização de margens/padding

**Documentação:**
- `README.md` — seção "O que já está implementado" atualizada, próximos passos revisados
- `docs/spec.md` — changelog desta rodada (Nota de Atualização mais recente)
