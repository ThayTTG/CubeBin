# CubeBin

Organizador de arquivos da NokyLabs para **Windows e Linux** (C#/.NET 8, interface em Avalonia com o
Kit NokyLabs). Organiza os arquivos por tipo, ajuda a liberar espaço e **sempre deixa um caminho de
volta**: antes de mexer em qualquer arquivo, o CubeBin anota de onde ele saiu.

Dois módulos:

- **Organizar Arquivos** — imagens para Imagens, vídeos para Vídeos, áudios para Músicas e
  documentos para Documentos, sem cadastrar nada. Prévia antes, Desfazer depois, relatório em HTML e
  modo automático quando você está longe do computador (com 60 s para cancelar).
- **Liberar Espaço** — esvaziar uma pasta ou escolher arquivos e mandar para a nuvem (Google Drive,
  recomendado), para a Lixeira ou apagar de vez. Com um agente opcional que só sugere o que limpar.

A fonte da verdade é o PRD (`docs/PRD-CubeBin.md`). As decisões tomadas onde o PRD não definia o
detalhe estão em `docs/DECISOES.md`, e as notas de cada etapa do roadmap em `docs/notas/`.

## Estrutura

```
CubeBin.sln
src/
  CubeBin.Core/        Toda a lógica, sem tela (net8.0, Windows e Linux)
    Plataforma/        Pastas do sistema (Known Folders / XDG), ausência, Lixeira, notificações Linux, autostart
    Configuracao/      configuracoes.json (regras padrão, pastas, automação, proteções)
    Organizar/         Prévia (Planejador), proteções, execução (Organizador), conflito de nomes, modo automático
    Registros/         Registro JSON Lines gravado arquivo por arquivo
    Relatorios/        Relatório HTML (modelo embutido)
    LiberarEspaco/     Esvaziar pasta / escolher arquivos, Lixeira e Apagar de vez
    Cubos/             O índice dos Cubos e a restauração
    Nuvem/             Google Drive, envio com conferência de integridade, índice na nuvem
    Agente/            Sugestões de limpeza (regras locais)
  CubeBin.App/         Telas (Avalonia + Kit NokyLabs), bandeja, avisos e agendador
    Telas/             Início, Organizar, Liberar Espaço, Cubos, Configurações (6 seções), Sobre
    Interface/         Estilos, ícones, diálogos e peças de tela
    Servicos/          Serviços do app, toasts (Windows), avisos (Linux), agendador por ausência
tests/CubeBin.Tests/   83 testes — todos em pastas temporárias, nunca nas pastas reais
lib/NokyLabs.UI/       Submódulo Git: o Kit NokyLabs (o mesmo do launcher)
docs/                  PRD, decisões, notas de atualização e documentos antigos (docs/historico)
```

## Como abrir e compilar

```bash
git clone --recurse-submodules https://github.com/ThayTTG/CubeBin.git
```

(Se já clonou sem o Kit: `git submodule update --init`.)

1. Instale o .NET 8 SDK.
2. Copie `src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs.example` para
   `GoogleDriveCredentials.Secrets.cs` (mesma pasta). Sem credenciais reais, tudo compila e só a
   nuvem fica desligada.
3. Compile, teste e rode:

```bash
dotnet build CubeBin.sln
```

```bash
dotnet test CubeBin.sln
```

```bash
dotnet run --project src/CubeBin.App
```

Aberto assim, o CubeBin mostra a janela. Fechar a janela só esconde (ele continua perto do relógio
para o modo automático); "Sair" fica no menu do ícone. Só um CubeBin roda por vez.

### Publicar

```bash
dotnet publish src/CubeBin.App/CubeBin.App.csproj -c Release -p:PublishProfile=SingleFile-win-x64
```

```bash
dotnet publish src/CubeBin.App/CubeBin.App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish-linux
```

O lançamento automático (`.github/workflows/release.yml`) gera os dois pacotes quando algo é enviado
ao branch `release`. A versão vem do `<Version>` em `src/CubeBin.App/CubeBin.App.csproj` (3.0.0; ver
`docs/DECISOES.md`, item 1.5).

## Onde o CubeBin guarda as coisas

`%LOCALAPPDATA%\CubeBin` (Windows) ou `~/.local/share/CubeBin` (Linux):

| Arquivo/pasta | O que é |
|---|---|
| `configuracoes.json` | Todas as configurações, legíveis e editáveis à mão (Configurações › "Abrir o arquivo de configurações") |
| `Registros/*.jsonl` | Um registro por execução: de onde cada arquivo saiu e para onde foi (usado pelo Desfazer/Restaurar) |
| `Relatórios/Relatório AAAA-MM-DD.html` | O relatório de cada execução, para abrir no navegador |
| `cubos.json` | O índice dos Cubos (tela inicial e tela de Cubos) |
| `sugestoes.json` | As últimas sugestões do agente |
| `Log/` | Um log simples de cada execução |

## Como testar cada módulo

> Dica para testar sem tocar nas suas pastas de verdade (Linux): rode com uma "home" de teste.
> ```bash
> HOME=/tmp/casa-teste XDG_CONFIG_HOME=/tmp/casa-teste/.config XDG_DATA_HOME=/tmp/casa-teste/.local/share dotnet run --project src/CubeBin.App
> ```
> e crie algumas pastas e arquivos de exemplo (Downloads, Documentos…) dentro de `/tmp/casa-teste`.

**Organizar Arquivos**
1. Coloque arquivos variados em Downloads (uma foto, um PDF, um MP3, um `.zip`, um `.exe`, um atalho).
2. Início › **Organizar agora**. A **Prévia** mostra o que vai para cada pasta e o que fica (o `.zip` fica
   "sem pasta", o `.exe` e o atalho ficam protegidos, arquivos mexidos há menos de 5 min ficam para depois).
3. **Continuar** › **Confirmação** › **Organizar agora** › **Conclusão**. Abra o relatório e teste a busca.
4. **Desfazer**: tudo volta. Ponha um arquivo novo com o mesmo nome na origem antes de desfazer: o
   devolvido ganha o prefixo (ex.: `Img-foto.png`), nada é sobrescrito.
5. Proteções: crie em Downloads uma pasta com um `package.json` (ou `.sln`, `.c3p`, `.git/`, `Assets/` +
   `ProjectSettings/`) e ligue "Incluir as subpastas" em Configurações › Pastas: ela aparece como projeto e
   não é tocada.

**Modo automático**
1. Configurações › Automação: ligue e ponha 1 minuto de ausência.
2. Não mexa no computador por 1 min: aparece o aviso (toast no Windows, janelinha no Linux) com o tempo
   para cancelar. Teste **Cancelar** e, numa outra vez, deixe seguir. No fim vem o aviso com o resultado.

**Liberar Espaço**
1. Liberar Espaço › escolha uma pasta (ex.: Downloads) › **Esvaziar** — ou arraste arquivos para
   "Escolher arquivos".
2. Em **Para onde vão?**, sem conta conectada a nuvem aparece apagada com "Conectar conta"; escolha
   **Lixeira** › conclusão › **Restaurar**.
3. Teste **Apagar de vez** com um arquivo descartável (há uma confirmação extra).
4. Tente escolher a própria pasta Documentos: ela é recusada (pastas padrão nunca são apagadas).

**Regras, Configurações e Cubos**
- Configurações › Regras: arraste o bloquinho `.zip` até uma pasta, ou até **Nova pasta**, ou clique nele
  e escolha no menu. Organize de novo e veja o `.zip` ir para lá.
- Cubos: filtros à esquerda; clique num Cubo para ver o resumo por tipo e **Restaurar**.

**Nuvem sem conta do Google (modo de teste)**
Rode com `CUBEBIN_NUVEM_DE_TESTE=/tmp/drive-teste`: a "nuvem" vira essa pasta, com o mesmo fluxo
(pacote em `CubeBin/Cubos`, relatórios em `CubeBin/Relatórios`, `CubeBin/indice.json`, conferência,
restauração). Ótimo para conferir tudo antes de cadastrar o Google.

**Nuvem com o Google Drive** — veja a seção abaixo; depois, Configurações › Nuvem › **Conectar conta**.

**Agente**
Configurações › Agente › ligue e **Analisar agora** (ou "Ligar e analisar" no Liberar Espaço). As
sugestões aparecem no Liberar Espaço; marque as que quiser e **Liberar as escolhidas**. Ele nunca tira
nada sozinho.

**Testes automáticos**: `dotnet test CubeBin.sln` (83 testes, todos em pastas temporárias, com a
"nuvem local").

## Configurar a integração com o Google Drive (uma vez, por quem desenvolve)

As credenciais ficam num arquivo que **não vai para o Git**
(`src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs`).

1. No [Google Cloud Console](https://console.cloud.google.com/), crie (ou escolha) um projeto.
2. **APIs e serviços › Biblioteca**: ative a **Google Drive API**.
3. **Tela de consentimento OAuth**: "Externo"; pode ficar em modo de teste com os e-mails de quem vai usar.
4. **Credenciais › Criar credenciais › ID do cliente OAuth**, tipo **App para computador**.
5. Copie o Client ID e o Client Secret para `GoogleDriveCredentials.Secrets.cs` (a partir do `.example`).
6. Recompile. No lançamento automático, cadastre os segredos `CUBEBIN_GDRIVE_CLIENT_ID` e
   `CUBEBIN_GDRIVE_CLIENT_SECRET` em Settings › Secrets and variables › Actions.

O escopo usado é `drive.file`: o CubeBin só enxerga o que ele mesmo criou (a pasta `CubeBin`).

## Diferenças entre Windows e Linux

| | Windows | Linux |
|---|---|---|
| Pastas do usuário | Known Folders (considera o OneDrive) | `~/.config/user-dirs.dirs` (XDG); se faltar, `~/Pictures` etc., criadas quando preciso |
| Ausência | `GetLastInputInfo` | GNOME/KDE (D-Bus) ou X11; sem isso, vale só o intervalo, sempre com o aviso antes |
| Aviso de início | Toast com "Cancelar" | Janelinha com contagem e "Cancelar" |
| Lixeira | Lixeira do Windows (restauração pelo CubeBin) | Lixeira FreeDesktop (`~/.local/share/Trash`) |
| Arquivo em uso | Abertura exclusiva | `/proc/*/fd` do usuário |
| Token do Google | DPAPI | Chaveiro do sistema (libsecret); sem chaveiro, arquivo com permissão 600 |
| Abrir com o sistema | Registro (`HKCU\...\Run`) | `~/.config/autostart/cubebin.desktop` |

## Modo de captura (desenvolvimento)

Com `CUBEBIN_CAPTURA="inicio=/tmp/inicio.png;organizar=/tmp/previa.png"`, o app abre cada página,
salva uma imagem e fecha (dá para encadear cliques: `organizar+clique:BtnAvancar=/tmp/conf.png`).
Serve para conferir as telas sem clicar, inclusive com Xvfb. Sem a variável, não faz nada.

---
Idealizado e aprovado por Thayna Dias · Desenvolvido com IA (Claude, da Anthropic)
