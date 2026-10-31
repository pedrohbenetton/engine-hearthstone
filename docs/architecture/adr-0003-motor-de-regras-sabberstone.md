# ADR-0003 — Motor de regras: fork do SabberStone, pool Classic/Core

- **Status:** accepted (2026-10-31)
- **Origem:** ARCH-0001 §1.4 (risco R1), decisão D3, pergunta 1 do PO

## Contexto

SabberStone e Fireplace não cobrem o Standard atual. HearthDb e HearthstoneJSON trazem metadados das cartas, não o comportamento.

## Decisão

1. O motor de regras é um **fork do SabberStone**, incluído como submódulo git em `external/SabberStone`.
2. O MVP fica restrito ao pool **Classic/Core** que o SabberStone já implementa e testa.
3. A engine só acessa o SabberStone pela camada `HsEngine.Simulation`, para isolar a dependência.
4. Expansões futuras são feitas **sob demanda**: só as cartas-chave dos arquétipos tier 1 do meta, implementadas no fork.

## Achados da integração (2026-10-31)

- **Upstream parado:** o último commit do `HearthSim/SabberStone` é de 2021-01-15 (`1db6e271`), o que confirma o R1. Hoje o submódulo aponta para o upstream; antes da primeira alteração em carta, trocar o remote para um fork próprio.
- **Licença AGPL-3.0:** a engine linka o SabberStone, então qualquer distribuição da engine ou oferta dela como serviço de rede herda as obrigações da AGPL (código-fonte aberto). Não há impacto para uso local ou acadêmico. Precisa de decisão antes de qualquer distribuição.
- **Compilação no .NET 10:** com C# 14, as conversões implícitas de `Span` mudam a resolução de LINQ e quebram `SpecificTask.cs` (CS0029). O fix, sem patch no submódulo, é `external/Directory.Build.targets`, que fixa `LangVersion=13`.
- **Opções inválidas:** `Controller.Options()` pode listar tarefas que `Game.Process` rejeita (visto em um ataque do Warsong Commander). Todo consumidor de `Options()` deve tratar `Process == false` como ramo morto.

## Consequências

- (+) A matemática do motor é validada sem custo de implementar cartas.
- (−) O produto não serve para o Standard atual até a expansão.
- (−) Toda carta nova implementada no fork exige testes próprios.
