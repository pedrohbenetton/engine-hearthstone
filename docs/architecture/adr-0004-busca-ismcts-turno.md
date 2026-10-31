# ADR-0004 — Busca: Lethal Solver exato + SO-ISMCTS por turno

- **Status:** accepted (2026-10-31), com o ajuste do revisor
- **Origem:** ARCH-0001 §3.3, decisão D4, parecer do Antigravity, item 3

## Decisão

1. **Lethal Solver** roda primeiro: DFS exato com poda e transposition table. Se encontrar letal, a busca termina ali.
2. **SO-ISMCTS por turno:** a árvore é indexada por ações do nosso turno e cada iteração usa uma determinização do range. O turno do oponente é simulado por uma política leve, seguido de avaliação estática.
3. **Ajuste do revisor (ordenação por informação):** o gerador de ações ordena e expande primeiro as **ações geradoras de informação** (compra de carta, discover, efeitos que revelam cartas) antes dos ramos que dependem de variáveis ainda ocultas. Assim, decisões que dependem do resultado de uma compra não são fixadas antes dela na árvore.
4. Efeitos aleatórios viram nós de chance amostrados, não expandidos.
5. Paralelismo por *root parallelization*: cada thread usa suas próprias determinizações.

## Consequências

- (+) Lida com informação oculta sem *strategy fusion* e com aleatoriedade intra-turno.
- (−) A ordenação por informação exige classificar as ações por tipo de efeito, com metadados vindos do SabberStone e do HearthDb.

## Alternativas rejeitadas

- **Minimax/alpha-beta como núcleo:** não lida com chance nem com informação oculta. Fica restrito ao Lethal Solver.
- **PIMC puro:** sofre de *strategy fusion*.
