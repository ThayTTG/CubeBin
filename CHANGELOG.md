# Histórico de versões

Tudo o que muda no CubeBin, versão por versão. O texto de cada versão vira as notas do lançamento no GitHub e aparece em **Novidades**, no NokyLabs Launcher e no site.

**Como usar:** anote as mudanças em `## [Não lançado]` enquanto trabalha. Na hora de lançar, é só aumentar o `<Version>` no `.csproj` e enviar para o branch `release`: o lançamento usa essa seção e ela vira a seção da versão sozinha.

## [Não lançado]
<!-- 3.0.0 — NÃO LANÇAR antes do organizador: sem ele publicado junto, quem atualizar fica sem a organização. -->
- O CubeBin agora cuida só de liberar espaço com segurança: **Sugestões**, **Deletar**, **Cubos** e o login do Google Drive. A organização de arquivos virou um app separado da NokyLabs, que você instala pelo NokyLabs Launcher.
- Suas configurações vêm junto: o agente de sugestões, o login do Google, os Cubos e o "abrir junto com o Windows" continuam como estavam. As pastas que eram organizadas viram as pastas que o agente analisa.
- Nova tela **Ajustes**: escolha em quais pastas o agente procura o que limpar, quanto tempo longe do computador ele espera e onde ficam os registros do que foi apagado (com um botão para abrir essa pasta).
- No menu do ícone perto do relógio, "Organizar agora" virou **Procurar o que limpar**: roda o agente na hora e abre as Sugestões.
- Os registros em CSV do que foi apagado agora ficam numa pasta só deles ("registros"), separados do histórico da organização.

## [2.1.0] - 2026-11-14
- **Versão para Linux** (x64), também pelo NokyLabs Launcher. Organiza no horário certo quando você está longe do computador (GNOME, KDE e sessões X11), abre junto com o sistema, guarda o login do Google Drive no chaveiro do sistema e avisa antes de organizar, com uma janelinha e o botão **Cancelar**.
- No Linux, arquivos ocultos e downloads que ainda estão baixando (`.part`, `.crdownload`) não são mexidos, e os instaladores do Linux (`.deb`, `.rpm`, `.appimage`...) vão para **Instaladores**.
- No Windows, tudo continua igual.

## [2.0.0] - 2026-11-09
- Motor gráfico novo (Avalonia), no padrão de todos os apps da NokyLabs: o mesmo visual e as mesmas animações do launcher, do ValReport e do YT-DLP GUI, no vermelho do CubeBin. Tudo continua funcionando do mesmo jeito.
- No canto do menu lateral: "Idealizado e aprovado por Thayna Dias" e "Desenvolvido com IA (Claude, da Anthropic)". A tela **Sobre** ganhou os créditos de quem fez e das ferramentas usadas.
- A tela Deletar já abre mostrando a área para arrastar os arquivos.
- No menu das pendências, nomes com "_" (como Documentos_Office) aparecem certinhos.

## [1.1.0] - 2026-10-15
- Visual novo, no padrão da NokyLabs: cores, cantos arredondados, ícones e menu lateral iguais aos dos outros apps da família. O vermelho continua sendo a cor do CubeBin.
- Botões que apagam arquivos de vez agora aparecem em coral, com ícone de lixeira, para não serem confundidos com os outros.
- As mensagens e confirmações do app ganharam o mesmo visual escuro, com botões que dizem o que vão fazer (por exemplo, “Excluir definitivamente” em vez de “Sim”).
- No Histórico, cada arquivo movido mostra a situação por escrito: Organizado, Será devolvido ou Devolvido.
- Nova tela **Sobre**, com a versão do app e os links do site, do código e para reportar um problema.
- O menu do ícone perto do relógio também ficou escuro, com “Abrir o CubeBin” e “Organizar agora”.

## [1.0.1] - 2026-10-15
- O executável agora se chama `CubeBin.exe`.
- Se o CubeBin abria junto com o Windows, abra-o uma vez depois de atualizar para ele registrar o caminho novo.

## [1.0.0] - 2026-10-15
- Primeira versão pública: organizar, limpar e deletar com backup, sugestões de limpeza e a galeria de Cubos.
