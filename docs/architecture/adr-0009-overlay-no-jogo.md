# ADR-0009 — Overlay da recomendação dentro do jogo

- **Status:** accepted (2026-11-01), decisão do PO
- **Complementa:** o [ADR-0005](adr-0005-modo-offline-e-ingestao.md) (limites de leitura) e o [ADR-0007](adr-0007-recomendador-ao-vivo-classic.md) (modo ao vivo)

## Contexto

O comando `live` funciona numa partida real, mas a saída no terminal é difícil de ler durante o jogo: mostra as descrições cruas do SabberStone, em inglês, com IDs e índices, e obriga o jogador a olhar para outra janela. O PO pediu que a recomendação apareça dentro do jogo.

## Decisão

1. **Uma janela WPF transparente**, sempre por cima e que deixa o clique passar (`WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, preservando o `WS_EX_LAYERED` gerenciado pelo WPF). Ela se alinha à área cliente da janela do Hearthstone e acompanha a posição dela.
   - O jogo é localizado só pela API de janelas do Windows (`FindWindow` com fallback por `Process.MainWindowHandle`, `GetClientRect`, `ClientToScreen`). O posicionamento usa `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)` direto em pixels físicos com manifesto `PerMonitorV2` (`GetDpiForWindow` para converter métricas internas em DIPs), mantendo `Height - 1` quando o jogo cobre o monitor inteiro para evitar ocultação por *independent flip* do DWM.
   - O foco aceita processos companheiros de overlay (`Hearthstone Deck Tracker`, `Firestone`) e a flag opcional `--always-visible` permite inspecionar o painel mesmo fora de foco ou sem o jogo aberto.
   - Continua valendo o ADR-0005: nada de leitura ou injeção de memória, nada de automação de input. A janela não recebe cliques e não envia nada ao jogo.
2. **Fase 1, painel:** passos numerados em português, com o nome pt-BR das cartas, a % de vitória e um aviso de cobertura (quantas cartas são proxy).
   - Os passos vêm da melhor linha da ISMCTS, reaplicada numa cópia do jogo. Assim cada passo traz a carta de origem, o alvo e a posição reais naquele momento.
3. **Fase 2, setas e destaques (`BoardGeometry`):** anéis luminosos e setas curvas Bézier sobre as cartas da mão, lacaios do board, retratos de herói, poder heroico e botão de encerrar turno, mostrando visualmente o próximo passo recomendado e avançando automaticamente a cada ação detectada no `Power.log`.
   - As posições na tela são calculadas no referencial 4:3 centralizado proporcional à altura cliente (funciona em 16:9, 16:10 e 21:9 ultrawide), calibradas com capturas reais de tela.
4. **Nomes pt-BR:** vêm de um mapa `id → nome` gerado do `cards.json` ptBR do HearthstoneJSON, do mesmo build do snapshot (ADR-0008).

## Consequências

- (+) A recomendação fica legível sem sair do jogo.
- (+) O mesmo plano estruturado serve ao painel, às setas e ao terminal.
- (−) Só funciona no Windows, num projeto `net10.0-windows`. O núcleo da engine continua multiplataforma.
- (−) As setas dependem do layout do jogo e podem precisar de recalibração a cada patch de interface.
- O overlay é do mesmo tipo dos deck trackers populares. O risco de ToS do modo ao vivo continua o que o PO aceitou no ADR-0007.
