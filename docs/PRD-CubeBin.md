# PRD — CubeBin

## 1. Visão geral

O CubeBin é um aplicativo para Windows e Linux, feito em C#/.NET, que organiza automaticamente os arquivos do usuário por tipo, com segurança (registro antes de qualquer mudança) e sem atrapalhar o uso do computador (roda em segundo plano quando o usuário está ausente). Ele tem dois módulos: **Organizar Arquivos** e **Liberar Espaço**, que reúne os antigos Limpar e Deletar.

## 2. Problema

Pastas como Downloads, Documentos e Área de Trabalho acumulam arquivos misturados. Organizar à mão é cansativo, e ferramentas automáticas costumam dar medo de perder arquivos. O CubeBin resolve as duas coisas: organiza sozinho e sempre deixa um caminho de volta.

## 3. Objetivos

- Organizar arquivos por tipo de forma automática e configurável.
- Nunca perder um arquivo: backup do estado anterior e regras rígidas para pastas padrão.
- Ser discreto: rodar só quando o usuário estiver ausente e sempre avisar antes.
- Ajudar a liberar espaço (Limpar/Deletar), com nuvem como alternativa à exclusão definitiva.

## 4. Público-alvo

Usuários de Windows que acumulam arquivos e querem organização sem esforço, incluindo uso pessoal e computadores de laboratório.

## 5. Funcionalidades

### 5.1 Organizar Arquivos

- Já vem com regras prontas, sem precisar definir extensões: imagens vão para Imagens, vídeos para Vídeos, áudios para Músicas e documentos para Documentos. O app pergunta ao sistema onde ficam essas pastas, no Windows e no Linux (padrão XDG). Editar o JSON fica como opção avançada. Monitora diretórios padrão do sistema (Documentos, Área de Trabalho, Downloads etc.) e pastas personalizadas, definidos em um arquivo `.json`.
- Os arquivos são movidos direto para a pasta de destino conforme a extensão, sem pasta temporária; o registro em JSON guarda a origem de cada um para restaurar.
- Organização de subpastas é opcional por pasta monitorada, com aviso de risco de desorganização.
- Conflito de nomes: prefixo com o diretório de origem (ex.: `Doc-Texto`, `Desk-Texto`).
- Arquivos com extensão não mapeada são listados para o usuário decidir na próxima interação.

#### Tela de regras: arrastar e soltar

Cada extensão vira um bloquinho num cartão grande, e o usuário arrasta cada bloquinho até o bloco da pasta onde ela deve ficar, sem editar o JSON à mão.

&#91;embedded content: tela de regras · arrastar extensões para pastas\]

Soltar um bloquinho em "Nova pasta" cria uma pasta personalizada com aquela regra.

#### Fluxo do clique em "Organizar agora"

O clique manual passa por quatro telas, e o Desfazer usa o registro JSON para devolver tudo ao lugar.

&#91;embedded content: fluxo do Organizar agora · 4 telas\]

Os números são só exemplos. Fluxo aprovado: a prévia aparece sempre no clique manual.

### 5.2 Liberar Espaço: escolher arquivos

- Exclusão de arquivos escolhidos pelo usuário.
- Com nuvem ativada: em vez de apagar direto, gera um pacote de backup compactado, envia para a nuvem e só apaga os arquivos locais após confirmação do envio, com relatório final.

### 5.3 Liberar Espaço: esvaziar uma pasta

- Esvazia o conteúdo de pastas padrão e permite restaurá-lo depois.
- Pastas padrão podem ser limpas e restauradas, mas **nunca deletadas**; pastas personalizadas podem ser criadas e excluídas livremente.

#### Fluxo do Liberar Espaço

Os dois botões levam à mesma tela de destino, com a nuvem como opção recomendada, em destaque à direita, e Lixeira e Apagar de vez como opções extras à esquerda. Layout preferido: blocos em colunas, ocupando um espaço mais quadrado que horizontal.

&#91;embedded content: fluxo do Liberar Espaço · 2 entradas, 1 destino, 4 etapas\]

Os números e nomes de arquivo são só exemplos.

Sem Google Drive conectado, o bloco da nuvem aparece apagado, com o aviso e o link "Conectar conta" no canto inferior esquerdo, e o botão à direita vira "Enviar para a Lixeira".

&#91;embedded content: tela de destino sem conta conectada\]

### 5.4 Execução em segundo plano

- Roda periodicamente quando o Windows detecta ausência do usuário por um tempo T (configurável).
- Notificação de início com 60 segundos para cancelar.
- Notificação de fim informando sucesso ou listando arquivos que não puderam ser movidos.

### 5.5 Agente de IA (opcional)

- Item extra que acompanha o Liberar Espaço, como agente de limpeza: roda em segundo plano, estilo antivírus, e só sugere limpezas e redução de espaço. Quem esvazia as pastas é o Liberar Espaço. Nunca age sem confirmação.

### 5.6 Interface

- Tela de configuração em C#/.NET, design simples por enquanto.
- Tela inicial com os "Cubos" organizados ou enviados à nuvem; ao passar o mouse, preview da quantidade de arquivos por tipo.

&#91;embedded content: rascunho da tela inicial · status, 3 ações, Cubos, relatório\]

Duas ações: Organizar Arquivos, em destaque, e Liberar Espaço, que oferece esvaziar uma pasta ou escolher arquivos; o agente aparece só como sugestão dentro dele.

#### Tela de Configurações

Duas colunas: menu com seis seções à esquerda e o conteúdo à direita; cada mudança salva sozinha.

&#91;embedded content: Configurações · seção Pastas\]

Pastas padrão não têm "Remover", só as personalizadas. Os valores mostrados são exemplos.

&#91;embedded content: Configurações · Regras e Automação\]

&#91;embedded content: Configurações · Nuvem e Agente\]

&#91;embedded content: Configurações · Proteções\]

#### Tela de Cubos

Aberta pelo "Ver todos" da tela inicial, com um botão para voltar ao início; filtros e resumo à esquerda, Cubos por data à direita.

&#91;embedded content: Tela de Cubos · aberta pelo Ver todos\]

Restaurar devolve cada arquivo ao lugar de origem usando o registro JSON do Cubo. Datas e números são exemplos.

## 6. Requisitos de segurança

- Registro do estado anterior em JSON antes de cada execução, usado pelo app para restaurar os arquivos (substitui o CSV, que quebrava com vírgulas e acentos nos nomes).
- Nenhuma exclusão definitiva sem confirmação (ou sem envio confirmado à nuvem).
- Pastas padrão do sistema jamais são removidas.

### 6.1 Relatório em HTML

A cada execução, o app preenche um modelo HTML embutido e salva um arquivo como `Relatório 2026-11-17.html`, que abre com dois cliques em qualquer navegador, offline.

- Resumo no topo: data, quantidade de arquivos movidos e erros.
- Tabela com arquivo, pasta de origem, pasta de destino e tamanho.
- Falhas destacadas em vermelho.
- Caixa de busca para filtrar pelo nome, sem internet.

### 6.2 Modo online (Google Drive)

- O app cria a pasta `CubeBin` na raiz do Drive e só mexe dentro dela.
- `CubeBin/Cubos`: um pacote compactado por envio, nomeado pela data de envio (o filtro por tipo fica com o Drive ou o Explorer).
- `CubeBin/Relatórios`: o relatório HTML e o registro JSON de cada envio, para restaurar tudo no lugar certo.
- Arquivo de índice lido pelo app para montar a tela de Cubos com preview, sem baixar os pacotes.

### 6.3 Riscos e proteções

| Risco | Proteção |
| --- | --- |
| Mover arquivos de projetos (Construct, Unity) ou de programas e quebrá-los | Agir só nas pastas do usuário; pular pastas que parecem projetos |
| Mover .exe portáteis e atalhos da Área de Trabalho | Não mover atalhos nem executáveis por padrão |
| Arquivos em uso ou downloads incompletos | Pular arquivos abertos, temporários de download e modificados nos últimos minutos |
| App travar no meio e o registro ficar incompleto | Gravar o JSON arquivo por arquivo, durante a execução |
| Arquivo novo no local de origem na hora de restaurar | Regra de conflito: renomear com prefixo, nunca sobrescrever |
| Apagar local sem o arquivo ter chegado inteiro ao Drive | Conferir integridade (tamanho e hash) antes de apagar |
| Sem internet, Drive cheio ou autorização do Google expirada | Adiar o envio, avisar o usuário e manter os arquivos locais |
| Documentos sincronizado com OneDrive | Detectar e avisar antes de mover grandes volumes |
| Pastas XDG ausentes ou com nome em outro idioma no Linux | Ler o arquivo XDG; se faltar, perguntar ou criar a pasta |

## 7. Requisitos não funcionais

- Baixo uso de CPU e disco durante a execução.
- Configuração legível e editável à mão (JSON).
- Registro (log) de cada execução.

## 8. Fora do escopo da versão 1.0 (sugestão)

- Agente de IA, integração com nuvem e o modo de organizar dentro da própria pasta.
- Design visual definitivo.

## 9. Roadmap sugerido

- **1.0:** Organizar + backup CSV + notificações + execução por ausência.
- **1.1:** Liberar Espaço (esvaziar pasta e escolher arquivos) com restauração.
- **1.2:** Tela de Cubos com preview.
- **2.0:** Integração com nuvem e agente de IA.

## 10. Métricas de sucesso

- Zero perda de arquivos reportada.
- Porcentagem de arquivos organizados automaticamente por execução.
- Quantidade de execuções canceladas (indica se o momento escolhido incomoda).

## 11. Notas de atualização

Cada versão publica um changelog no formato "Nota de Atualização - 1.X".
