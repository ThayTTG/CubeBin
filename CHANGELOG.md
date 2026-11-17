# Histórico de versões

Tudo o que muda no CubeBin, versão por versão. O texto de cada versão vira as notas do lançamento no GitHub e aparece em **Novidades**, no NokyLabs Launcher e no site.

**Como usar:** anote as mudanças em `## [Não lançado]
<!-- 3.0.0 — a reconstrução a partir do PRD. As notas detalhadas de cada etapa estão em docs/notas/. -->
- **Um CubeBin só, com dois módulos:** Organizar Arquivos e Liberar Espaço. O app volta a ser um (o Organizador separado deixou de existir).
- **Organizar Arquivos funciona de cara:** imagens para Imagens, vídeos para Vídeos, áudios para Músicas e documentos para Documentos, nas pastas que o Windows (ou o Linux) informa. Sem pasta temporária: cada arquivo vai direto para o lugar certo.
- **Sempre dá para voltar:** antes de mexer em cada arquivo, o CubeBin anota de onde ele saiu (registro em JSON, no lugar do CSV). Desfazer e Restaurar devolvem tudo ao lugar.
- **Você confere antes:** o "Organizar agora" mostra a prévia, pede confirmação, mostra o andamento e termina com o resumo e um relatório para abrir no navegador.
- **Organiza sozinho quando você está longe** (desligado no começo), sempre com 60 segundos para cancelar.
- **Liberar Espaço:** esvazie uma pasta ou escolha arquivos e decida para onde vão: nuvem (recomendado), Lixeira ou apagar de vez.
- **Nuvem (Google Drive):** pacote compactado na pasta CubeBin do Drive; só apaga do computador depois de conferir que chegou inteiro. Sem internet, adia e mantém tudo.
- **Agente de sugestões** (opcional): acha cópias repetidas, temporários esquecidos e arquivos grandes parados — só sugere.
- **Telas novas:** Início, regras com arrastar e soltar, Configurações em seis seções e a tela de Cubos.
- **Proteções:** nunca mexe em pastas de projeto (Construct, Unity, código), atalhos, programas, arquivos abertos ou recém-alterados; nada é sobrescrito; pastas padrão nunca são apagadas.
- Configurações novas em `configuracoes.json` (a configuração da 2.x não é lida).

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
