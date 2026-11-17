# Nota de Atualização - 1.0

**Organizar Arquivos, do jeito novo.**

- **Funciona de cara.** Imagens vão para Imagens, vídeos para Vídeos, áudios para Músicas e documentos para Documentos. Não precisa cadastrar extensão nenhuma: o CubeBin pergunta ao Windows (ou ao Linux) onde ficam essas pastas e, se alguma não existir, cria e avisa.
- **Sem pasta temporária.** Cada arquivo vai direto para o lugar certo.
- **Você confere antes.** O "Organizar agora" mostra uma prévia do que vai mudar de lugar e do que fica onde está (e por quê), pede sua confirmação, mostra o andamento e termina com um resumo.
- **Sempre dá para voltar.** Antes de mexer em cada arquivo, o CubeBin anota de onde ele saiu (um registro em JSON que aguenta acentos, vírgulas e até o computador desligando no meio). O botão **Desfazer** devolve tudo ao lugar.
- **Relatório para abrir no navegador.** A cada organização, um arquivo como `Relatório 2026-11-17.html`, com o resumo, a tabela de cada arquivo (de onde saiu, para onde foi, tamanho), os problemas em vermelho e uma busca pelo nome — tudo sem internet.
- **Organiza sozinho quando você está longe** (desligado no começo: é só ligar na tela inicial). Depois de alguns minutos sem você mexer no computador, o CubeBin avisa e dá 60 segundos para cancelar. No fim, avisa o que deu certo e o que não pôde ser movido.
- **Proteções:** nunca mexe dentro de pastas de projeto (Construct, Unity, Visual Studio, Node, Git), nunca move atalhos nem programas, pula arquivos abertos, downloads em andamento e arquivos alterados há poucos minutos. Arquivos com o mesmo nome ganham o nome da pasta de origem na frente (`Doc-Texto.pdf`); nada é sobrescrito.
- Tipos de arquivo sem pasta ficam onde estão e aparecem para você decidir depois.
