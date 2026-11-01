# ADR-0005 — Produto offline e ingestão passiva de `Power.log`

- **Status:** accepted (2026-10-31). O item 1 (produto offline primeiro) foi substituído pelo [ADR-0007](adr-0007-recomendador-ao-vivo-classic.md), que adota o modo ao vivo. Os itens 2 e 3 continuam valendo.
- **Origem:** ARCH-0001 §3.5, decisão D5, pergunta 2 do PO

## Decisão

1. O produto começa como **laboratório offline / analisador de cenários e pós-jogo**. Não há recomendação durante partidas ao vivo.
2. A ingestão é **só leitura passiva de `Power.log`**. Leitura ou injeção de memória e automação de input estão proibidas.
3. A `Game State Machine` passa por um **sanitizador estrito** que descarta toda informação oculta ao jogador humano. Um teste de não-vazamento é obrigatório.

## Consequências

- (+) Sem risco de ToS na fase inicial e sem pressão de latência.
- (−) Uma recomendação ao vivo, se for desejada no futuro, exige uma nova decisão.
