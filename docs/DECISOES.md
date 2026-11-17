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
