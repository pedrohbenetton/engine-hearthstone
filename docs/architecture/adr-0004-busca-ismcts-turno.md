# ADR-0004 — Busca: Lethal Solver exato + SO-ISMCTS por turno

- **Status:** accepted (2026-10-31), com o ajuste do revisor
- **Origem:** ARCH-0001 §3.3, decisão D4, parecer do Antigravity, item 3

## Decisão

1. **Lethal Solver** roda primeiro: DFS exato com poda e transposition table. Se encontrar letal, a busca termina ali.
2. **SO-ISMCTS por turno:** a árvore é indexada por ações do nosso turno e cada iteração usa uma determinização do range. O turno do oponente é simulado por uma política leve, seguido de avaliação estática.
3. **Ajuste do revisor (ordenação por informação):** o gerador de ações ordena e expande primeiro as **ações geradoras de informação** (compra de carta, discover, efeitos que revelam cartas) antes dos ramos que dependem de variáveis ainda ocultas. Assim, decisões que dependem do resultado de uma compra não são fixadas antes dela na árvore.
4. Efeitos aleatórios viram nós de chance amostrados, não expandidos.
5. Paralelismo por *root parallelization*: cada thread usa suas próprias determinizações.

## Implementação (2026-10-31, evoluída em 2026-11-18)

- **Identidade de ação (`ActionKey`), deduplicação posicional e quebra de simetria (`BoardPlacement`):** a chave é semântica (tipo, carta de origem, posição no board, alvo codificado, posicionamento, escolha), não o índice de `Options()`. Assim as estatísticas valem entre determinizações. Cartas iguais na mão são intercambiáveis.
  - Para lacaios jogados da mão, `ZonePosition` só entra na chave quando há ao menos uma carta posicional observável em jogo ou na nossa mão (`ADJACENT_BUFF`, `MODULAR`/Magnético ou texto posicional como *adjacent*, *next to*, *left/right-most*); caso contrário usa-se apenas o slot canônico à direita (`BoardZone.Count`, `Placement = -1`).
  - Lacaios gêmeos indistinguíveis atacam da esquerda para a direita (colapsando `k!` permutações em 1) e alvos gêmeos colapsam no mais à esquerda.
  - Opções estritamente dominadas (`IsDominatedOption`) — dano puro no próprio herói ou em lacaio aliado sem Último Suspiro/gatilho de dano, e buffs positivos puros em lacaios inimigos — são podadas da árvore.
- **Lethal Solver determinístico + probabilidade estocástica de letal:** `LethalSolver` valida determinismo em múltiplas seeds (`RandomHappenedEvent` e ausência de compras incertas do deck) antes de curto-circuitar a busca. Quando o letal depende de RNG ou topdeck (ex.: *Mísseis Arcanos* ou compras), `Ismcts` contabiliza a taxa empírica de vitórias intra-turno (`LethalProbability`) e a expõe no painel e no terminal.
- **Seleção com RAVE/AMAF e backup robusto de Coulom:** UCB com contagem de disponibilidade (SO-ISMCTS, `C = 0,25` calibrado para recompensas densas em `[0, 1]`), combinado com RAVE/AMAF no escopo do turno (`β = k / (k + N)`, `k = 20`) para compartilhar valor entre ações comutativas com poucas visitas, e backup misto robusto de Coulom (`MaxBackupWeight = 0,5` sobre o filho mais visitado com `≥ 4` visitas) para evitar que jogadas de preparação (como *A Moeda*) sejam penalizadas por explorações subótimas.
- **Expansão em 3 níveis:** (1) ações geradoras de informação (`InformationActions`: compra, *discover*, geração aleatória na mão e trocas letais de Último Suspiro de compra), (2) ações ativas do turno, (3) `END_TURN`.
- **Determinização:** `HiddenInformationDeterminizer` em jogos de lista conhecida (Arena/Slice 0) e `RangeDeterminizer` bayesiano em partidas reais (ADR-0006).

## Consequências

- (+) Lida com informação oculta sem *strategy fusion* e com aleatoriedade intra-turno.
- (−) A ordenação por informação exige classificar as ações por tipo de efeito, com metadados vindos do SabberStone e do HearthDb.

## Alternativas rejeitadas

- **Minimax/alpha-beta como núcleo:** não lida com chance nem com informação oculta. Fica restrito ao Lethal Solver.
- **PIMC puro:** sofre de *strategy fusion*.
