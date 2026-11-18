# ADR-0004 — Busca: Lethal Solver exato + SO-ISMCTS por turno

- **Status:** accepted (2026-10-31), com o ajuste do revisor
- **Origem:** ARCH-0001 §3.3, decisão D4, parecer do Antigravity, item 3

## Decisão

1. **Lethal Solver** roda primeiro: DFS exato com poda e transposition table. Se encontrar letal, a busca termina ali.
2. **SO-ISMCTS por turno:** a árvore é indexada por ações do nosso turno e cada iteração usa uma determinização do range. O turno do oponente é simulado por uma política leve, seguido de avaliação estática.
3. **Ajuste do revisor (ordenação por informação):** o gerador de ações ordena e expande primeiro as **ações geradoras de informação** (compra de carta, discover, efeitos que revelam cartas) antes dos ramos que dependem de variáveis ainda ocultas. Assim, decisões que dependem do resultado de uma compra não são fixadas antes dela na árvore.
4. Efeitos aleatórios viram nós de chance amostrados, não expandidos.
5. Paralelismo por *root parallelization*: cada thread usa suas próprias determinizações.

## Implementação (2026-10-31)

- **Identidade de ação (`ActionKey`) e deduplicação posicional (`BoardPlacement`):** a chave é semântica (tipo, carta de origem, posição no board, alvo codificado, posicionamento, escolha), não o índice de `Options()`. Assim as estatísticas valem entre determinizações. Cartas iguais na mão são intercambiáveis. Para lacaios jogados da mão, o slot `ZonePosition` só entra na chave (e na busca/rollout/plano) quando há ao menos uma carta posicional observável em jogo ou na nossa mão (`ADJACENT_BUFF`, `MODULAR`/Magnético ou texto posicional como *adjacent*, *next to*, *left/right-most*); caso contrário usa-se apenas o slot canônico à direita (`BoardZone.Count`, `Placement = -1`), concentrando visitas e eliminando linhas duplicadas por posição.
- **Seleção:** UCB com contagem de disponibilidade (SO-ISMCTS), com recompensa = Win% ∈ [0, 1] e C = 0,7. O ranking final usa o filho mais visitado (*robust child*).
- **Iteração:** uma expansão por iteração. Depois dela, a política greedy de 1-ply termina o nosso turno e joga o turno do oponente, e a avaliação é aplicada no início do nosso turno seguinte.
- **Determinização (`HiddenInformationDeterminizer`):** reembaralha o nosso deck e redistribui de forma uniforme as cartas ocultas do oponente (mão ∪ deck). Cartas geradas (`CREATOR`) e The Coin ficam onde estão. O RNG de cada mundo vem de uma seed (`CloneWithSeed`, por reflexão, porque `Game.Random` é `internal`).
- **Ordenação por informação:** a classificação é por texto da carta (`draw|discover`). É suficiente para o pool Basic e deve virar metadado quando o pool crescer.
- **Média do nó vs. valor da linha:** a Win% reportada é a média de todas as visitas, incluindo exploração. Uma linha de letal certa aparece como ~0,97, não 1,0.
- **Custo:** o rollout greedy domina, com ~5 ms por iteração por thread (clona e avalia todas as opções a cada passo). É o próximo alvo de otimização: política de rollout mais barata ou avaliação incremental.

## Consequências

- (+) Lida com informação oculta sem *strategy fusion* e com aleatoriedade intra-turno.
- (−) A ordenação por informação exige classificar as ações por tipo de efeito, com metadados vindos do SabberStone e do HearthDb.

## Alternativas rejeitadas

- **Minimax/alpha-beta como núcleo:** não lida com chance nem com informação oculta. Fica restrito ao Lethal Solver.
- **PIMC puro:** sofre de *strategy fusion*.
