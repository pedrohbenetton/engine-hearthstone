# ADR-0002 — Pipeline offline de dados em Python

- **Status:** accepted (2026-10-31)
- **Origem:** ARCH-0001 §1.3, decisão D2

## Decisão

O modelo de range, a calibração da Win Equity e as análises ficam em **Python**, no diretório `/analysis`.

A fronteira com o core é feita **só por artefatos serializados** (JSON para priors e pesos, ONNX se houver modelo aprendido). Não há chamadas Python ↔ C# em tempo de decisão.

## Consequências

- (+) Iteração rápida em modelagem estatística, sem afetar a latência do core.
- (−) Dois toolchains. Cada artefato precisa de um schema versionado.
