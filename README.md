# CubeBin (e o organizador da NokyLabs)

Este repositório guarda **dois apps** da NokyLabs para Windows e Linux (C#/.NET 8, interface em Avalonia com
o Kit NokyLabs), que até a versão 2.x eram um app só:

- **CubeBin** (3.x) — liberar espaço sem perder nada. **Sugestões** (um agente opcional, desligado
  por padrão, que procura duplicados, temporários antigos e arquivos grandes parados, só com
  heurísticas locais, sem internet), **Deletar** (apagar arquivos e pastas com registro em CSV
  antes), **Cubos** (pacotes de backup no Google Drive: só apaga do computador depois de confirmar
  o envio), **Ajustes** e **Login**.
- **Organizador** (nome provisório) — a antiga parte "Organizar" do CubeBin: distribui os arquivos
  das pastas acompanhadas em pastas por tipo, sozinho, quando você está longe do computador.
  Tudo o que ele move pode voltar para onde estava (Histórico). Só mexe dentro do computador: não
  fala com a nuvem nem apaga nada.

Por que separar: eram duas promessas diferentes num app só (manter tudo arrumado / liberar espaço),
o que deixava o CubeBin confuso para quem não é da área. O plano completo da repaginada está no
Projeto NokyLabs ("plano-cubebin-repaginado").

A especificação original (fluxo, regras de negócio, changelog antigo) está em `docs/spec.md` e
continua valendo para a organização, que agora mora no Organizador.

## Estrutura

```
CubeBin.sln
src/
  Comum/              # O que os dois apps dividem, sem tela: mover arquivos com segurança, registro
                      # em CSV, detecção de ausência, abrir com o Windows, conta dos horários e a
                      # leitura do config da 2.x (MigracaoDaVersao2)
  Comum.UI/           # O que os dois dividem na tela: o diálogo no visual da NokyLabs, a barra de
                      # título escura, o ícone da bandeja, ícones (Recursos.axaml) e estilos (Estilos.axaml)

  Organizador.Core/   # Regras por extensão, pastas acompanhadas, o passo a passo de uma organização,
                      # pendências e o "devolver ao lugar" (Recall)
  Organizador.App/    # Telas: Organizar, Pastas, Histórico, Configurações, Sobre. Organizador.exe

  CubeBin.Core/       # Agente de sugestões, apagar com registro, Google Drive e os Cubos
  CubeBin.App/        # Telas: Sugestões, Deletar, Cubos, Ajustes, Login, Sobre. CubeBin.exe
lib/
  NokyLabs.UI/        # Submódulo Git: o Kit NokyLabs (cores, botões, animações, créditos)
tests/
  Organizador.Tests/  # Agendamento, configuração, classificação e a migração da 2.x
  CubeBin.Tests/      # Agendamento do agente, configuração e a migração da 2.x
```

Quando o organizador ganhar nome, a ideia é ele ir para um repositório próprio, levando `Comum` e
`Comum.UI` como submódulo (do mesmo jeito que o Kit NokyLabs). Para trocar o nome, os lugares são
`src/Organizador.App/InfoApp.cs` (nome, pasta de dados, nome na inicialização do Windows, links) e o
`.csproj` dele (`AssemblyName`, `Product`). A cor provisória é o rosa `#F472B6` (`App.axaml`) e o
ícone provisório é `src/Organizador.App/app.ico`.

### Onde cada app guarda os arquivos

| | Pasta | O que tem |
|---|---|---|
| CubeBin | `%LOCALAPPDATA%\CubeBin` | `config.json`, sugestões, cubos, login do Google, `registros\` (CSV do que foi apagado) |
| Organizador | `%LOCALAPPDATA%\NokyLabs\Organizador` | `config.json`, pendências, histórico, `backups\` (CSV de cada organização) |

**Quem vem da 2.x:** o primeiro dos dois apps que abrir guarda uma cópia intacta do config antigo em
`%LOCALAPPDATA%\CubeBin\config-2.x.json`, e cada app lê a parte dele dessa cópia — a ordem não
importa. O organizador continua usando as pendências, o histórico e a pasta temporária de onde já
estavam (dentro de `%LOCALAPPDATA%\CubeBin`), para nada se perder. Regras que mandavam arquivos para
o Google Drive viram regras para uma pasta local de mesmo nome, e o organizador avisa isso uma vez.

### O Kit NokyLabs (submódulo)

A aparência vem da biblioteca [NokyLabs.UI](https://github.com/ThayTTG/NokyLabs.UI), presa em
`lib/NokyLabs.UI` como submódulo. Ao clonar, traga junto:

```
git clone --recurse-submodules https://github.com/ThayTTG/CubeBin.git
```

(Se já clonou sem ele: `git submodule update --init`.) Para trazer uma versão nova do Kit:
`git submodule update --remote lib/NokyLabs.UI`.

## Como abrir

1. Instale o .NET 8 SDK (Visual Studio 2022, Rider ou só o `dotnet` CLI).
2. Copie `src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs.example` para
   `GoogleDriveCredentials.Secrets.cs` (mesma pasta). Sem as credenciais reais, tudo compila e só o
   backup na nuvem fica desligado (veja abaixo).
3. Abra `CubeBin.sln` no Visual Studio, **ou**:
   ```
   dotnet build CubeBin.sln
   dotnet test CubeBin.sln
   dotnet run --project src/CubeBin.App -- --abrir
   dotnet run --project src/Organizador.App -- --abrir
   ```
   Os dois abrem só na bandeja (perto do relógio); o `--abrir` já mostra a janela.

## Distribuição como .exe único (sem instalar dependências)

Cada app tem um perfil de publicação (`Properties/PublishProfiles/SingleFile-win-x64.pubxml`) que gera
um único `.exe` autocontido — quem usa não precisa instalar o .NET:

```
dotnet publish src/CubeBin.App/CubeBin.App.csproj -c Release -p:PublishProfile=SingleFile-win-x64
dotnet publish src/Organizador.App/Organizador.App.csproj -c Release -p:PublishProfile=SingleFile-win-x64
```

O lançamento automático (`.github/workflows/release.yml`) ainda publica só o CubeBin. **Não lance o
CubeBin 3.0 antes do organizador estar publicado também**, senão quem atualizar fica sem a organização.

Observação sobre "Iniciar com o Windows": quando o app roda via `dotnet run`, `Environment.ProcessPath`
aponta para `dotnet.exe`. Para testar essa opção, use o `.exe` compilado ou publicado. Cada app se
registra com o seu nome (`CubeBin` e `NokyLabs Organizador`), então um não desliga o outro.

## Versão Linux

O mesmo código gera a versão Linux (x64) dos dois apps. O alvo é escolhido pelo `-r`: com
`-r linux-x64` os apps compilam em `net8.0` puro, sem os toasts do Windows. Dá para publicar a
partir do próprio Windows:

```
dotnet publish src/CubeBin.App/CubeBin.App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish-linux
dotnet publish src/Organizador.App/Organizador.App.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish-linux-organizador
```

Cada pasta fica com o executável (`CubeBin` ou `Organizador`) e o `icon.png`, que o NokyLabs
Launcher usa no atalho. No lançamento, o GitHub Actions gera o `CubeBin-linux-x64.zip` junto do
pacote Windows (o organizador ainda não tem lançamento próprio).

O que muda no Linux:

| Recurso | Windows | Linux |
|---|---|---|
| Saber se o usuário está ausente | `GetLastInputInfo` | D-Bus do GNOME (Mutter) ou do KDE, ou XScreenSaver no X11. Sem nenhum deles (Sway, Hyprland), organiza no horário agendado, sempre com o aviso de cancelamento antes. |
| Abrir junto com o sistema | Registro (`HKCU\...\Run`) | `~/.config/autostart/<app>.desktop` (ex.: `cubebin.desktop`, `nokylabs-organizador.desktop`) |
| Token do Google Drive (CubeBin) | DPAPI | Chaveiro do sistema (`secret-tool`/libsecret); sem chaveiro, arquivo com permissão 600 |
| Aviso antes de organizar (Organizador) | Toast com "Cancelar" | Janelinha com contagem regressiva e "Cancelar" |
| Resumo e avisos | Toast | `notify-send` (libnotify) |
| Pasta Downloads | Known Folders | `~/.config/user-dirs.dirs` (ex.: `~/Transferências`) |
| Ícone perto do relógio | Bandeja do Windows | StatusNotifierItem. No GNOME precisa da extensão "AppIndicator". |

No Linux, o organizador também ignora arquivos ocultos (nome começando com ponto) e downloads em
andamento (`.part`, `.crdownload`), e a regra padrão "Instaladores" inclui `.deb`, `.rpm`,
`.appimage`, `.flatpakref`, `.run` e `.sh`.

## Configurar a integração com o Google Drive (necessário antes de usar)

As credenciais ficam num arquivo separado que **não é versionado no Git** (`src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs`, ignorado via `.gitignore`) — é um passo único, feito por quem desenvolve o app. Depois de preenchido e recompilado, qualquer pessoa que rodar o app só precisa clicar em "Conectar ao Google Drive" e fazer login com a própria conta, sem tocar em Client ID/Secret nenhum.

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/), crie um projeto novo (ou use um existente).
2. No menu, vá em **"APIs e serviços" → "Biblioteca"**, procure por **"Google Drive API"** e clique em **Ativar**.
3. Vá em **"APIs e serviços" → "Tela de consentimento OAuth"**, escolha **"Externo"**, preencha nome do app e e-mail de contato (não precisa publicar — pode deixar em modo "Teste" e adicionar seu(s) próprio(s) e-mail(s) como usuário de teste; até ~100 contas de teste podem logar sem passar pela revisão do Google).
4. Vá em **"APIs e serviços" → "Credenciais" → "Criar credenciais" → "ID do cliente OAuth"**, tipo de aplicativo **"Aplicativo para computador"** (Desktop app). Dê um nome e crie.
5. Copie o **Client ID** e o **Client Secret** gerados.
6. Copie `src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs.example` para `GoogleDriveCredentials.Secrets.cs` (mesma pasta, sem o `.example`) e substitua os dois placeholders pelos valores reais. Esse arquivo copiado nunca é commitado — já está no `.gitignore`.
7. Recompile o app. A partir daí, o botão "Conectar ao Google Drive" na aba Login do CubeBin já funciona para qualquer pessoa que rodar esse build — sem precisar repetir os passos acima.

## Próximos passos sugeridos

Seguem o plano da repaginada (Projeto NokyLabs, "plano-cubebin-repaginado"):

1. Escolher o nome do organizador (e, com ele, a cor, o ícone e o repositório próprio).
2. Organizador: Linha do tempo com "Desfazer tudo", tela de Início com prévia, automático desligado
   na primeira vez, Pastas e Regras em cartões, Ajustes em frases.
3. CubeBin: tela de Início ("Você pode liberar X GB"), Revisar (Sugestões + Deletar), Login dentro
   de Ajustes.
4. Launcher e site: os dois apps no catálogo, com o aviso da separação para quem tinha o CubeBin 2.x.
