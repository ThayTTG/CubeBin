# CubeBin

Aplicativo Windows (C#/.NET, WPF) organizado em 3 módulos — **Organizar**, **Limpar** e
**Deletar** — mais as abas de apoio **Sugestões**, **Cubos**, **Histórico** e **Login**. Roda em
segundo plano e organiza arquivos automaticamente apenas quando o usuário está ausente; os
módulos Limpar, Deletar e o agente de Sugestões são sempre acionados manualmente ou, no caso do
agente, opcionalmente agendados.

- **Organizar**: classifica arquivos por extensão e distribui em pastas locais ou no Google Drive,
  conforme regras configuráveis (padrão ou por pasta). Agendamento automático + Pendências + Recall.
- **Limpar**: gerencia as pastas monitoradas — limpa/restaura pastas padrão do Windows (nunca as
  deleta) e cria/exclui pastas personalizadas livremente.
- **Deletar**: seleção manual de arquivos/pastas de qualquer lugar do PC. Duas saídas possíveis:
  exclusão direta e definitiva (com backup CSV antes), ou envio a um pacote de backup na nuvem
  antes de excluir localmente (só apaga depois de confirmar que o upload terminou).
- **Sugestões**: agente opcional (**desativado por padrão**) que analisa as pastas monitoradas em
  segundo plano e sugere arquivos duplicados, temporários antigos ou grandes parados — usando só
  heurísticas locais (hash, data de acesso, tamanho), sem IA externa nem internet. Mesmas duas
  saídas do Deletar (excluir direto ou mandar para a nuvem antes).
- **Cubos**: cada pacote de backup enviado à nuvem vira um cubo visual — quanto maior o pacote,
  mais sólido; passe o mouse para ver a composição por categoria, clique para ver detalhes, abrir
  no Google Drive ou restaurar.

A especificação completa (fluxo, schema do config.json, regras de negócio, changelog versão por
versão) está em `docs/spec.md`.

## Estrutura

```
CubeBin.sln
src/
  CubeBin.Core/     # Lógica de negócio, sem dependência de UI
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

  CubeBin.App/      # WPF: UI, bandeja do sistema, agendador
    App.xaml(.cs)               # Bootstrap: monta os services e inicia o agendador
    MainWindow.xaml(.cs)        # UI mínima com 5 abas (pastas, regras, config, pendências, histórico)
    TrayIconManager.cs          # Ícone de bandeja (processo residente)
    BackgroundScheduler.cs      # Timer que dispara ciclos do OrganizadorService
    Services/
      ToastNotificationService.cs  # Implementação de INotificationService com toast nativo
```

## Como abrir

1. Instale o .NET 8 SDK com workload de desenvolvimento desktop (Visual Studio 2022 ou `dotnet` CLI).
2. Abra `CubeBin.sln` no Visual Studio, **ou**:
   ```
   dotnet restore
   dotnet build
   dotnet run --project src/CubeBin.App
   ```
3. Na primeira execução, o `ConfigService` cria automaticamente um `config.json` padrão em
   `%LOCALAPPDATA%\CubeBin\config.json`, com Documentos, Área de Trabalho e Downloads
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
- UI WPF completa nas 8 abas atuais: Organizar (Regras de extensão + Pendências), Limpar, Deletar,
  Sugestões (agente opcional), Cubos (pacotes de backup na nuvem), Histórico (absorve o Recall),
  Configurações (agendamento, diretórios, iniciar com Windows) e Login. Ver "Estrutura" acima e
  `docs/spec.md` para o detalhamento de cada uma.
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

O projeto inclui um perfil de publicação (`src/CubeBin.App/Properties/PublishProfiles/SingleFile-win-x64.pubxml`)
que gera um único `.exe` autocontido — o usuário final não precisa instalar o .NET Desktop Runtime
separadamente, só rodar o arquivo.

**Via linha de comando:**
```
dotnet publish src/CubeBin.App/CubeBin.App.csproj -c Release -p:PublishProfile=SingleFile-win-x64
```

O executável final fica em:
```
src/CubeBin.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/CubeBin.App.exe
```

**Via Visual Studio:** botão direito no projeto `CubeBin.App` → Publicar → escolher o
perfil "SingleFile-win-x64" (já vem configurado) → Publicar.

Esse `.exe` fica relativamente grande (na faixa de 100-150 MB), porque embute o runtime do .NET
inteiro — é a troca normal para não depender de nada instalado na máquina do usuário.

**Bônus:** publicado dessa forma, `Environment.ProcessPath` (usado pela funcionalidade "Iniciar
com o Windows") aponta corretamente para o `.exe` real, em vez de `dotnet.exe` como acontece
quando se roda via `dotnet run` — a ressalva mencionada nas notas de atualização anteriores deixa
de se aplicar a partir de um executável publicado dessa forma.

## Configurar a integração com o Google Drive (necessário antes de usar)

As credenciais ficam num arquivo separado que **não é versionado no Git** (`src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs`, ignorado via `.gitignore`) — é um passo único, feito por quem desenvolve o app. Depois de preenchido e recompilado, qualquer pessoa que rodar o app só precisa clicar em "Conectar ao Google Drive" e fazer login com a própria conta, sem tocar em Client ID/Secret nenhum.

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/), crie um projeto novo (ou use um existente).
2. No menu, vá em **"APIs e serviços" → "Biblioteca"**, procure por **"Google Drive API"** e clique em **Ativar**.
3. Vá em **"APIs e serviços" → "Tela de consentimento OAuth"**, escolha **"Externo"**, preencha nome do app e e-mail de contato (não precisa publicar — pode deixar em modo "Teste" e adicionar seu(s) próprio(s) e-mail(s) como usuário de teste; até ~100 contas de teste podem logar sem passar pela revisão do Google).
4. Vá em **"APIs e serviços" → "Credenciais" → "Criar credenciais" → "ID do cliente OAuth"**, tipo de aplicativo **"Aplicativo para computador"** (Desktop app). Dê um nome e crie.
5. Copie o **Client ID** e o **Client Secret** gerados.
6. Copie `src/CubeBin.Core/GoogleDriveCredentials.Secrets.cs.example` para `GoogleDriveCredentials.Secrets.cs` (mesma pasta, sem o `.example`) e substitua os dois placeholders pelos valores reais. Esse arquivo copiado nunca é commitado — já está no `.gitignore`.
7. Recompile o app. A partir daí, o botão "Conectar ao Google Drive" na aba Login já funciona para qualquer pessoa que rodar esse build — sem precisar repetir os passos acima.

## Próximos passos sugeridos

1. Adicionar testes unitários para `ClassificadorService`, `ConfigService`, `BackupService` e
   `BackgroundScheduler.ObterHorariosDoDia` (são as peças com regras de negócio mais sensíveis).
2. Validar em Visual Studio: como este ambiente não tem o .NET SDK instalado, o código não pôde
   ser compilado aqui — rode `dotnet build` após aplicar cada atualização.
3. **Atenção ao testar "Iniciar com o Windows":** `Environment.ProcessPath` aponta para
   `dotnet.exe` quando o app roda via `dotnet run`. Para essa funcionalidade registrar o caminho
   certo no registro do Windows, teste a partir do `.exe` gerado em `bin/Debug/...` (ou de uma
   versão publicada com `dotnet publish`), não via `dotnet run`.
4. Ao testar o novo agendamento por vezes/dia, um `config.json` de uma versão anterior a 1.9 não
   terá os campos `execucoesPorDia`/`horarioInicio`/`iniciarComWindows` — eles assumem os valores
   padrão da classe automaticamente (4 vezes/dia a partir das 08:00, inicialização desligada), sem
   precisar apagar o arquivo manualmente.
