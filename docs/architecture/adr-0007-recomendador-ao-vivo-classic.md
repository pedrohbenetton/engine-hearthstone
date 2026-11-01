# ADR-0007 — Recomendador ao vivo, formato Classic primeiro

- **Status:** accepted (2026-10-31), decisão do PO; **item 2 e R7 substituídos pelo [ADR-0008](adr-0008-padrao-com-proxy-de-cartas.md)** (o formato Classic foi removido do jogo)
- **Substitui:** o item 1 do [ADR-0005](adr-0005-modo-offline-e-ingestao.md) (produto offline primeiro)
- **Complementa:** o [ADR-0003](adr-0003-motor-de-regras-sabberstone.md) (pool de cartas)

## Contexto

O produto é um **recomendador de jogadas durante a partida**: conhece o deck do jogador, infere o deck provável do oponente a partir das cartas reveladas (com base de decks do meta), acompanha o estado da partida e diz como jogar o turno. O PO aceitou o risco de ToS do uso ao vivo.

O simulador (SabberStone) só implementa cartas até 2020. O formato **Classic** do Hearthstone usa o conjunto original de 2014, que o SabberStone já implementa.

## Decisão

1. **Modo ao vivo** é o alvo. Os limites do ADR-0005 continuam valendo: leitura **passiva** do `Power.log`, nada de leitura/injeção de memória, nada de automação de input. O programa recomenda e quem joga é o humano.
2. **Formato Classic primeiro.** O primeiro recomendador de ponta a ponta funciona em partidas Classic. Standard e Wild exigem implementar cartas no fork (ADR-0003, R1) e ficam para depois.
3. **Base de decks do oponente:** listas públicas por código de deck, decodificadas pelo formato deckstring. A inferência é P(deck | cartas reveladas), conforme o ADR-0006.
4. **A arena (ISMCTS × greedy) continua sendo o teste de qualidade** do recomendador, e não um modo de jogo.

## Consequências

- (+) Chega-se cedo a um recomendador real, sem implementar cartas.
- (−) O formato Classic tem um público menor que o Standard.
- (−) Uso ao vivo traz risco à conta, assumido pelo PO.
- O sanitizador de informação oculta (ADR-0005, item 3) passa a ser crítico: o recomendador não pode usar o que o jogador não vê.
- **Risco R7: IDs e versões do formato Classic.** No jogo, as cartas Classic usam IDs `VAN_*` (o formato existe desde 2021), e o CardDefs do SabberStone (jan/2021) não tem nenhuma. O SabberStone implementa as versões **pós-nerf** das cartas originais; por exemplo, o Leeroy Jenkins (`EX1_116`) está com custo 5, enquanto o Classic usa o custo 4 de 2014. Mitigação: um mapa `VAN_X → X` mais uma tabela de correções para as cartas alteradas desde 2014, conferida contra o HearthstoneJSON. A maioria das correções é de custo e atributos (tags); poucas mudam comportamento (ex.: Warsong Commander) e exigem código no fork.
- Fixtures de teste exigem logs de partidas Classic reais. Os logs locais existentes são de Battlegrounds e servem só para o leitor de baixo nível.
