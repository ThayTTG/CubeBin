# Decisões tomadas durante a reconstrução

Este arquivo registra o que foi decidido sem consulta, quando o PRD (`docs/PRD-CubeBin.md`) não
definia o detalhe. A regra usada em todas: **a opção mais segura e mais simples**. Cada decisão diz
o porquê, para ser fácil de rever.

---

## Etapa 1 — Base

### 1.1 O que havia no repositório

O repositório tinha **dois apps** (o "CubeBin 3.x", de faxina + nuvem, e o "Organizador", de
organização), cinco bibliotecas (`Comum`, `Comum.UI`, `Organizador.Core`, `CubeBin.Core` e o Kit
`NokyLabs.UI` como submódulo) e testes dos dois. Os problemas citados no pedido estavam lá:

- O organizador exigia **regras de extensão** cadastradas antes de funcionar e criava as pastas de
  destino **dentro da pasta de origem** (ex.: `Downloads/Imagens`).
- Ele passava todos os arquivos por uma **pasta temporária** antes do destino.
- O "backup" era um **CSV**, que quebrava com vírgulas e acentos.
- Limpar, Deletar, Sugestões e Cubos eram ferramentas parecidas espalhadas em dois apps.

### 1.2 Um app só, com dois módulos

O PRD descreve **um** app com dois módulos (Organizar Arquivos e Liberar Espaço). A divisão em dois
apps foi desfeita. Estrutura nova:

```
src/CubeBin.Core   lógica sem tela (net8.0, Windows e Linux), testável
src/CubeBin.App    telas em Avalonia + Kit NokyLabs (executável CubeBin)
tests/CubeBin.Tests
lib/NokyLabs.UI    submódulo (o mesmo Kit do launcher NokyLabs)
```

Dentro do Core, uma pasta (e um namespace) por assunto: `Plataforma`, `Configuracao`, `Organizar`,
`Registros`, `Relatorios`, `LiberarEspaco`, `Cubos`, `Nuvem` e `Agente`. Os namespaces estão no
plural ou com nomes diferentes das classes (ex.: namespace `Registros`, classe `RegistroExecucao`)
para não repetir o erro **CS0104** (nome ambíguo). Na parte de tela, `System.IO.Path` é sempre usado
pelo apelido `IOPath`, porque o Avalonia também tem um `Path` (a forma desenhada).

### 1.3 O que foi reaproveitado (com `git mv`, para manter o histórico)

| Peça antiga | Onde está agora | Por quê |
|---|---|---|
| `KnownFolders` (Windows) + `PastasXdg` (Linux) | `Core/Plataforma/PastasDoSistema.cs`, `PastasXdg.cs` | Já liam as pastas reais; agora cobrem as seis do PRD (inclui Imagens, Vídeos e Músicas) |
| `IdleDetectionService`, `OciosidadeLinux`, `NativeMethods` | `Core/Plataforma/Ociosidade*.cs` | Detectar ausência no Windows (GetLastInputInfo) e no Linux (GNOME, KDE, X11) já funcionava |
| `InicializacaoService` | `Core/Plataforma/Inicializacao.cs` | Abrir junto com o sistema (registro / autostart XDG) |
| `ComandoExterno`, `NotificacaoLinux`, `AvisoDeErroLinux` | `Core/Plataforma/` | Notificações e avisos no Linux |
| `GoogleDriveService`, `DpapiDataStore`, `LinuxSecretDataStore`, credenciais | `Core/Nuvem/` e `Core/GoogleDriveCredentials*.cs` | Login OAuth com escopo `drive.file` e token guardado com segurança |
| `Dialogo`/`JanelaDialogo`, `TemaJanelaEscura`, `TrayIconManager` (agora `Bandeja`), `Recursos.axaml`, `Estilos.axaml` | `App/Interface/` | O visual e os diálogos no padrão NokyLabs |
| `JanelaContagem` | `App/` | O aviso de 60 s com "Cancelar" no Linux |
| Heurísticas do `AgenteSugestaoService` | `Core/Agente/` (versão 2.0) | Duplicados, temporários antigos e arquivos grandes parados, sem internet |

### 1.4 O que foi descartado

- A pasta temporária, o CSV, as regras por pasta e a "fila de pendências" antiga (substituídas pelo
  registro JSON, pelas regras padrão e pela lista de extensões sem pasta).
- A migração da configuração da 2.x (`MigracaoDaVersao2`): a configuração nova é outra e mora em
  outro arquivo (`configuracoes.json`), então nada antigo é sobrescrito. Ver pendências no README.

### 1.5 Versão do executável x numeração do roadmap

O roadmap do PRD fala em 1.0, 1.1, 1.2 e 2.0, mas o CubeBin **já publicou** as versões 1.0.0 a
2.1.0. Se o app reconstruído saísse como "1.0.0", o NokyLabs Launcher não o enxergaria como
atualização. Decisão: os números do roadmap viram os nomes das **Notas de Atualização**
(`docs/notas/Nota de Atualização - 1.X.md`, como pede o PRD), e o `<Version>` do executável continua
**3.0.0**. Se preferir outra numeração, é só trocar o `<Version>` em `src/CubeBin.App/CubeBin.App.csproj`.

### 1.6 Onde o app guarda os dados

`%LOCALAPPDATA%\CubeBin` no Windows e `~/.local/share/CubeBin` no Linux (o que o .NET devolve para
`LocalApplicationData`). Ali ficam `configuracoes.json`, os registros (`Registros/`), os relatórios
(`Relatórios/`), o índice dos Cubos e o login do Google. Os relatórios ficam fora de Documentos de
propósito: se ficassem lá, o próprio CubeBin acabaria organizando-os.

### 1.7 Testes nunca tocam nas pastas reais

`PastasDoSistema.Substituto` permite trocar a descoberta das pastas do sistema. A classe de teste
`PastaDeTeste` aponta as seis pastas para dentro de uma pasta temporária nova e só apaga o que está
dentro dela. Os testes rodam em sequência (`xunit.runner.json`), porque o substituto é estático.

---

## Versão 1.0 — Organizar Arquivos

### 2.1 Quais pastas são organizadas
O PRD diz "Documentos, Área de Trabalho, Downloads etc." como pastas monitoradas, e Imagens, Vídeos,
Músicas e Documentos como destinos. As seis pastas padrão aparecem na configuração, mas só
**Documentos, Área de Trabalho e Downloads** vêm com "organizar" ligado (é onde os arquivos se
acumulam). Imagens, Vídeos e Músicas podem ser ligadas pela pessoa.

### 2.2 Arquivo que já está na pasta certa não se mexe
Um PDF em Documentos (ou em qualquer subpasta de Documentos) fica onde está. Isso também protege
quem já organizou subpastas à mão: com "organizar subpastas" ligado, `Documentos/Trabalho/a.pdf`
**não** é "achatado" para `Documentos/a.pdf`.

### 2.3 Prefixos de conflito
PRD: `Doc-Texto`, `Desk-Texto`. Prefixos usados: Documentos `Doc`, Área de Trabalho `Desk`, Downloads
`Down`, Imagens `Img`, Vídeos `Vid`, Músicas `Mus`; pastas personalizadas usam as 4 primeiras letras
do nome (editável no JSON, campo `prefixo`). Se `Doc-Texto.pdf` também existir, vira
`Doc-Texto (2).pdf`. Na restauração vale a mesma regra, com o prefixo da pasta de onde o arquivo está
saindo (ex.: `Img-foto.png` volta para Downloads se lá já houver outro `foto.png`).

### 2.4 Formato do registro: JSON Lines
O PRD pede "JSON gravado arquivo por arquivo". Um JSON único (uma lista) teria de ser regravado
inteiro a cada arquivo — e, se o computador desligasse na hora, o arquivo ficaria quebrado. Por isso
o registro é **JSON Lines** (`.jsonl`): uma linha JSON por evento, gravada direto no disco
(`WriteThrough` + `Flush`). Cada arquivo tem uma linha **antes** de ser mexido (origem → destino) e
outra **depois** (deu certo ou não). O leitor ignora uma última linha cortada. O Desfazer não depende
do "depois": ele procura o arquivo no destino e, se estiver lá, devolve.

### 2.5 Roadmap fala em "backup CSV" na 1.0
O roadmap (seção 9) ainda diz "backup CSV", mas a seção 6 e o pedido dizem que o JSON **substitui**
o CSV. Segui o JSON (não muda o comportamento para a pessoa, só o formato).

### 2.6 Arquivos sem extensão e extensões sem pasta
Arquivos sem extensão ficam onde estão e não entram na lista de "sem pasta" (não dá para criar regra
útil para eles). Extensões sem pasta são anotadas em `extensoesSemPasta` no JSON e aparecem na tela
inicial e na tela de regras até a pessoa decidir.

### 2.7 Executáveis e atalhos
"Executável" é decidido pela **extensão** (`.exe`, `.msi`, `.bat`, `.sh`, `.appimage`...; lista no
JSON). O bit de execução do Linux não é usado: em pendrives e partições NTFS todo arquivo tem esse bit,
e o CubeBin deixaria de organizar tudo.

### 2.8 Arquivo em uso
Windows: o arquivo é aberto sem compartilhamento; se o Windows recusar, alguém está usando. Linux:
não existe essa trava, então o CubeBin olha `/proc/<processo>/fd` (só os processos do próprio
usuário). A conferência é feita na prévia **e de novo** logo antes de mover cada arquivo.

### 2.9 "Alterado nos últimos minutos"
5 minutos por padrão (`minutosSemAlteracao`). Usa a data de modificação e, quando o sistema informa,
a de criação (um download recém-terminado pode ter a data de modificação antiga do servidor).

### 2.10 OneDrive
Uma pasta é considerada do OneDrive pelas variáveis `OneDrive*` do Windows ou por um trecho do
caminho começando com "OneDrive". Com 100 arquivos ou mais para mover (`limiteAvisoOneDriveArquivos`),
a confirmação pede um "Entendi" extra; no modo automático, que não tem prévia, esses arquivos ficam
para quando a pessoa abrir o CubeBin.

### 2.11 Modo automático
- **Desligado na primeira vez** (opção mais segura; a pessoa liga na tela inicial).
- A cada 30 s o CubeBin olha há quanto tempo a pessoa não mexe no computador. Ausente há T minutos
  (padrão 10) → prévia interna → aviso com 60 s para cancelar → organiza → aviso de fim.
- **Uma tentativa por ausência** e no máximo uma a cada 4 h (`horasEntreExecucoes`). Se não houver
  nada para mover, não aparece aviso nenhum.
- Depois dos 60 s, o plano é montado **de novo** (arquivos podem ter sido abertos nesse meio-tempo).
- Onde o sistema não informa a ausência (Linux com Sway/Hyprland), vale só o intervalo — sempre com o
  aviso de 60 s antes.
- Métricas do PRD 10 ficam no JSON: `execucoesAutomaticas` e `execucoesCanceladas`.
- Aviso de início: toast com "Cancelar" no Windows (se o toast falhar, aparece a janelinha de
  contagem); janelinha de contagem no Linux (aparece em qualquer ambiente, ao contrário dos botões
  em notificação).

### 2.12 Relatórios
Ficam em `Relatórios/` dentro da pasta de dados do CubeBin, com o nome do PRD
(`Relatório 2026-11-17.html`); a segunda execução do dia vira `Relatório 2026-11-17 (2).html`. O
modelo HTML vai **dentro do programa** (recurso embutido). Funciona offline, com tema claro/escuro.

### 2.13 Janela, bandeja e uma instância só
Aberto pela pessoa, o CubeBin mostra a janela; pelo "abrir junto com o sistema" ele começa só na
bandeja (`--bandeja`). Fechar a janela só a esconde (o automático continua). Só um CubeBin roda por
vez (mutex): abrir de novo mostra a janela do que já está aberto. Isso também garante que nunca haja
duas organizações ao mesmo tempo. Dentro do app, uma trava impede que o manual e o automático rodem
juntos.

### 2.14 Configuração editada à mão
Se o JSON não tiver a chave `regras`, valem as regras padrão; uma lista vazia (`"regras": []`) é
respeitada. As seis pastas padrão sempre voltam se forem apagadas do JSON. Se o JSON estiver quebrado,
o CubeBin guarda uma cópia (`configuracoes.json.quebrado-<data>`), volta ao padrão e avisa na tela inicial.

### 2.15 Pasta de dados num Linux novo
Num Linux em que `~/.local/share` ainda não existe, o .NET devolvia um caminho vazio (os dados iriam
para a pasta atual). A pasta agora é pedida com `SpecialFolderOption.Create`, com uma segunda rede de
segurança (bug encontrado nos testes com Xvfb).

---

## Versão 1.1 — Liberar Espaço

### 3.1 Lixeira de verdade, nos dois sistemas
- **Linux:** padrão FreeDesktop (`~/.local/share/Trash/files` + `info/*.trashinfo`), o mesmo do
  Nautilus/Dolphin. O item aparece na Lixeira do sistema e pode ser restaurado por lá também. Itens de
  outro disco vão para a Lixeira da pasta pessoal (o .NET copia e apaga); a pasta `.Trash-<uid>` de
  cada disco, que o padrão também permite, não é usada (mais simples, mesmo resultado).
- **Windows:** envio pela API do Explorer (`SHFileOperation` com "permitir desfazer"). Para restaurar,
  o CubeBin lê os arquivos de controle `$I…` da `$Recycle.Bin` (caminho original + data), acha o
  `$R…` correspondente, move de volta e apaga o `$I`. Não depende de menus do Explorer (que mudam de
  nome conforme o idioma). **Não foi possível testar no Windows real** daqui (só a leitura dos
  arquivos `$I`, com arquivos montados no teste). Ver pendências.

### 3.2 O que "Esvaziar uma pasta" tira
Tudo o que está dentro (arquivos e subpastas), **menos**: arquivos ocultos/do sistema (como
`desktop.ini`), arquivos abertos em outro programa, pastas que parecem projetos e subpastas que
contêm outra pasta padrão. A pasta esvaziada nunca é apagada. Proteções de "alterado há poucos
minutos" e de executáveis **não** valem aqui: a pessoa escolheu esvaziar a pasta e vê a lista antes.

### 3.3 O que "Escolher arquivos" recusa
Uma pasta padrão (pode ser esvaziada, nunca apagada), qualquer pasta que contenha uma pasta padrão
ou a pasta pessoal, a raiz do disco, pastas do sistema/programas e a pasta de dados do CubeBin
(sem ela não há como restaurar). Arquivos dentro de uma pasta também escolhida saem junto com ela.

### 3.4 Pastas personalizadas
"Remover" uma pasta personalizada (Configurações › Pastas) tira a pasta da lista do CubeBin e devolve
as extensões dela para "sem pasta", mas **não apaga nada do disco**. Para apagar o conteúdo, a pessoa
usa o Liberar Espaço, que tem a confirmação e o caminho de volta.

### 3.5 Confirmações
A própria tela "Para onde vão?" é a confirmação para Lixeira (dá para restaurar). "Apagar de vez"
pede uma confirmação extra, com o botão vermelho e a lixeira, como manda o guia NokyLabs. Sem Lixeira
disponível no sistema, o bloco da Lixeira aparece apagado.

### 3.6 Sem conta conectada
Como no PRD: o bloco da nuvem aparece apagado, com o aviso "Nenhuma conta do Google Drive conectada"
e o link "Conectar conta" no canto inferior esquerdo; o botão da direita vira "Enviar para a Lixeira".
