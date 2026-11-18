# ADR-0006 — Range bayesiano e avaliação heurística calibrada

- **Status:** accepted (2026-10-31)
- **Origem:** ARCH-0001 §3.2–3.4, perguntas 3 e 4 do PO

## Decisão

**Range:**
- Os priors vêm de listas públicas do meta (Vicious Syndicate, HSReplay free).
- O posterior é bayesiano: P(arquétipo | cartas reveladas, classe) e depois P(mão | arquétipo, cartas vistas, tempo de retenção, mulligan).
- No Slice 0 o deck do oponente é conhecido. O range se reduz à ordem do deck e à mão.

**Avaliação (Slice 0):**
- É heurística, com termos de board, tempo, vida, lethal threat e card advantage.
- A Win Equity é calculada como σ(a·score + b), com a calibração feita em Python ([ADR-0002](adr-0002-pipeline-offline-python.md)).
- O critério de aceite é ECE < 5 p.p.

**Hardware de referência:** desktop x64 moderno com 8 a 16 threads, sem GPU (CPU-bound).

## Implementação (2026-11-17)

- **Histórico observável sem vazamento (`Observation`):** rastreia por slot de mão `TurnEnteredHand`, `KeptInMulligan` (cartas mantidas antes de `BEGIN_MULLIGAN`) e `IsCreated`, além de separar `KnownDeckCards` (cartas do deck já vistas para inferência de lista/arquétipo) de `SpentDeckCards` (cartas já fora do `DECK` para subtração exata do restante do baralho).
- **Fontes de deck local (`DeckLibrary`):** codec Base64 varint de deckstrings oficiais (`DeckStringCodec`), leitura de `Decks.log` e de `PlayerDecks.xml` do HDT, com casamento automático da lista ativa (`DeckMatcher`).
- **Range bayesiano (`BayesianRangeEstimator` + `RangeDeterminizer`):** calcula `P(arquétipo | classe, KnownDeckCards)` em espaço logarítmico com suavização de Laplace e pondera cada slot oculto da mão do oponente por retenção de mulligan e idade da carta na mão.

## Consequências

- (+) Avaliação interpretável e rápida.
- (−) O teto de qualidade fica limitado à heurística. Um modelo aprendido é uma decisão futura.
