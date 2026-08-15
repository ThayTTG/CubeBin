# Organizador de Arquivos

Aplicativo Windows (C#/.NET, WPF) que organiza arquivos por extensão em pastas configuráveis,
rodando em segundo plano e agindo apenas quando o usuário está ausente.

A especificação completa (fluxo, schema do config.json, regras de negócio) está no documento
`spec-organizador-arquivos.md` (versão 1.1) compartilhado junto com este projeto.

## Estrutura

```
OrganizadorArquivos.sln
src/
  OrganizadorArquivos.Core/     # Lógica de negócio, sem dependência de UI
    Models/                     # AppConfig, PastaMonitorada, RegraExtensao, ArquivoPendente, ResultadoExecucao
    Services/
      ConfigService.cs          # Ler/gravar config.json; limpar/restaurar pastas padrão (nunca deletar)
      BackupService.cs          # Snapshot em CSV, ordem alfabética, com retenção
      ClassificadorService.cs   # Extensão -> pasta de destino; resolução de conflito de nome
      PendenciaService.cs       # Fila de arquivos sem regra mapeada
      IdleDetectionService.cs   # Detecção de ausência via GetLastInputInfo
      OrganizadorService.cs     # Orquestra o pipeline completo de uma execução
      INotificationService.cs   # Abstração de notificações (implementada no App)
    Interop/
      NativeMethods.cs          # P/Invoke do Win32 (GetLastInputInfo)

  OrganizadorArquivos.App/      # WPF: UI, bandeja do sistema, agendador
    App.xaml(.cs)               # Bootstrap: monta os services e inicia o agendador
    MainWindow.xaml(.cs)        # UI mínima com 5 abas (pastas, regras, config, pendências, histórico)
    TrayIconManager.cs          # Ícone de bandeja (processo residente)
    BackgroundScheduler.cs      # Timer que dispara ciclos do OrganizadorService
    Services/
      ToastNotificationService.cs  # Implementação de INotificationService com toast nativo
```

## Como abrir

1. Instale o .NET 8 SDK com workload de desenvolvimento desktop (Visual Studio 2022 ou `dotnet` CLI).
2. Abra `OrganizadorArquivos.sln` no Visual Studio, **ou**:
   ```
   dotnet restore
   dotnet build
   dotnet run --project src/OrganizadorArquivos.App
   ```
3. Na primeira execução, o `ConfigService` cria automaticamente um `config.json` padrão em
   `%LOCALAPPDATA%\OrganizadorArquivos\config.json`, com Documentos, Área de Trabalho e Downloads
   já cadastrados como pastas padrão.

## O que já está implementado (esqueleto funcional)

- Modelos completos alinhados ao schema do `config.json` da spec.
- `ConfigService` com a regra de negócio "pastas padrão nunca são deletadas, só limpas/restauradas".
- Pipeline completo em `OrganizadorService` (backup CSV → temporário → classificação → destino →
  fila de pendências → notificação final), incluindo checagem de ausência e janela de cancelamento de 60s.
- Resolução de conflito de nome por prefixo de pasta de origem.
- Detecção de ausência via Win32 API.
- Ícone de bandeja + agendador em background.
- Toast nativo com botão de cancelar.
- UI WPF completa e funcional nas 5 telas: Pastas monitoradas (adicionar/remover/limpar/restaurar),
  Regras de extensão (CRUD completo + subpastas por extensão), Configurações gerais (agendamento
  por N vezes/dia + iniciar com Windows), Pendências (listar/ignorar/resolver com menu flutuante)
  e Histórico de backups (abrir com duplo clique).
- Confirmação de risco (`INotificationService.ConfirmarRiscoSubpastas`) disparada ao salvar uma
  pasta com "Organizar subpastas" recém-ativado.
- Execução manual sob demanda (menu da bandeja + botão na UI), com indicador visual de progresso.
- Subcategorias por extensão dentro de uma regra (ex: `Compactados\WinRAR`).
- Agendamento por "N vezes ao dia a partir de um horário", em vez de intervalo fixo em minutos.
- Registro/remoção automática na inicialização do Windows.
- Recall de arquivos: todo arquivo movido pelo pipeline principal (ou resolvido a partir de uma
  pendência) é registrado em `recall.json`. Botão direito num item da aba "Recall" devolve o
  arquivo ao local original. O backup em CSV continua existindo sem alterações, como registro
  legado independente.
- UX das Regras de extensão revista: 1 clique seleciona, 2 cliques edita o texto da célula
  (comportamento padrão do DataGrid, sem mais interceptação nossa), botão direito mostra um menu
  contextual com "Editar &lt;coluna clicada&gt;", "Editar subpastas por extensão" e "Remover regra".
- Perfil de publicação single-file self-contained para distribuir como um único `.exe`.

## Distribuição como .exe único (sem instalar dependências)

O projeto inclui um perfil de publicação (`src/OrganizadorArquivos.App/Properties/PublishProfiles/SingleFile-win-x64.pubxml`)
que gera um único `.exe` autocontido — o usuário final não precisa instalar o .NET Desktop Runtime
separadamente, só rodar o arquivo.

**Via linha de comando:**
```
dotnet publish src/OrganizadorArquivos.App/OrganizadorArquivos.App.csproj -c Release -p:PublishProfile=SingleFile-win-x64
```

O executável final fica em:
```
src/OrganizadorArquivos.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/OrganizadorArquivos.App.exe
```

**Via Visual Studio:** botão direito no projeto `OrganizadorArquivos.App` → Publicar → escolher o
perfil "SingleFile-win-x64" (já vem configurado) → Publicar.

Esse `.exe` fica relativamente grande (na faixa de 100-150 MB), porque embute o runtime do .NET
inteiro — é a troca normal para não depender de nada instalado na máquina do usuário.

**Bônus:** publicado dessa forma, `Environment.ProcessPath` (usado pela funcionalidade "Iniciar
com o Windows") aponta corretamente para o `.exe` real, em vez de `dotnet.exe` como acontece
quando se roda via `dotnet run` — a ressalva mencionada nas notas de atualização anteriores deixa
de se aplicar a partir de um executável publicado dessa forma.

## Configurar a integração com o Google Drive (necessário antes de usar)

As credenciais ficam num arquivo separado que **não é versionado no Git** (`src/OrganizadorArquivos.Core/GoogleDriveCredentials.Secrets.cs`, ignorado via `.gitignore`) — é um passo único, feito por quem desenvolve o app. Depois de preenchido e recompilado, qualquer pessoa que rodar o app só precisa clicar em "Conectar ao Google Drive" e fazer login com a própria conta, sem tocar em Client ID/Secret nenhum.

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/), crie um projeto novo (ou use um existente).
2. No menu, vá em **"APIs e serviços" → "Biblioteca"**, procure por **"Google Drive API"** e clique em **Ativar**.
3. Vá em **"APIs e serviços" → "Tela de consentimento OAuth"**, escolha **"Externo"**, preencha nome do app e e-mail de contato (não precisa publicar — pode deixar em modo "Teste" e adicionar seu(s) próprio(s) e-mail(s) como usuário de teste; até ~100 contas de teste podem logar sem passar pela revisão do Google).
4. Vá em **"APIs e serviços" → "Credenciais" → "Criar credenciais" → "ID do cliente OAuth"**, tipo de aplicativo **"Aplicativo para computador"** (Desktop app). Dê um nome e crie.
5. Copie o **Client ID** e o **Client Secret** gerados.
6. Copie `src/OrganizadorArquivos.Core/GoogleDriveCredentials.Secrets.cs.example` para `GoogleDriveCredentials.Secrets.cs` (mesma pasta, sem o `.example`) e substitua os dois placeholders pelos valores reais. Esse arquivo copiado nunca é commitado — já está no `.gitignore`.
7. Recompile o app. A partir daí, o botão "Conectar ao Google Drive" nas Configurações gerais já funciona para qualquer pessoa que rodar esse build — sem precisar repetir os passos acima.

## Próximos passos sugeridos

1. Adicionar testes unitários para `ClassificadorService`, `ConfigService`, `BackupService` e
   `BackgroundScheduler.ObterHorariosDoDia` (são as peças com regras de negócio mais sensíveis).
2. Substituir o ícone padrão do `NotifyIcon`/`ApplicationIcon` por um ícone próprio quando o
   design padrão do produto for definido.
3. Validar em Visual Studio: como este ambiente não tem o .NET SDK instalado, o código não pôde
   ser compilado aqui — rode `dotnet build` após aplicar cada atualização.
4. **Atenção ao testar "Iniciar com o Windows":** `Environment.ProcessPath` aponta para
   `dotnet.exe` quando o app roda via `dotnet run`. Para essa funcionalidade registrar o caminho
   certo no registro do Windows, teste a partir do `.exe` gerado em `bin/Debug/...` (ou de uma
   versão publicada com `dotnet publish`), não via `dotnet run`.
5. Ao testar o novo agendamento por vezes/dia, um `config.json` de uma versão anterior a 1.9 não
   terá os campos `execucoesPorDia`/`horarioInicio`/`iniciarComWindows` — eles assumem os valores
   padrão da classe automaticamente (4 vezes/dia a partir das 08:00, inicialização desligada), sem
   precisar apagar o arquivo manualmente.
