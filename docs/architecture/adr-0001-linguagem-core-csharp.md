# ADR-0001 — Core Engine em C#/.NET

- **Status:** accepted (2026-10-31)
- **Origem:** ARCH-0001 §1, decisão D1

## Contexto

A engine precisa de um motor de regras existente (requisito: não codificar cartas à mão) e de alto throughput de simulação para o MCTS, dentro de um budget de 5–15 s.

## Decisão

Todo código que roda em tempo de decisão (simulação, busca, avaliação, ingestão) é escrito em **C#**, com target **`net10.0`**.

- `net10.0` é a LTS atual e cumpre o requisito ".NET 8+". O .NET 8 sai de suporte em 2026-11-10.
- O código quente usa `struct`, `Span<T>` e pooling. Otimizações de performance acontecem dentro do C#, sem trocar de linguagem.

## Consequências

- (+) Uso direto do SabberStone, do HearthDb e do parser do HDT.
- (+) Um plugin futuro para o HDT fica nativo.
- (−) Throughput 2–5x abaixo de C++ otimizado. Aceito, porque a qualidade da recomendação depende mais da avaliação e da determinização.

## Alternativas rejeitadas

- **Rust/C++:** não há motor de regras maduro; seria preciso reimplementar o jogo.
- **Python no core:** 1–2 ordens de grandeza mais lento, inviável no budget.
