# Benchmark — baseline de clone do SabberStone (2026-10-31)

- **Objetivo:** medir o risco R2 (throughput de clone) antes de construir o ISMCTS.
- **Máquina:** Intel i5-10400F (6 núcleos / 12 threads), .NET 10.0.403, Release, BenchmarkDotNet `--job short`.
- **Como reproduzir:**
  ```bash
  dotnet run -c Release --project bench/HsEngine.Benchmarks -- --filter '*' --job short
  ```
- **Estado medido:** `GameFactory.CreateMidgame(seed: 7, Actions)`, ou seja, Mage × Warrior com N ações aleatórias jogadas.

## Resultados (média por operação, 1 thread)

| Operação | 0 ações | 40 ações | 80 ações | Alocação (80 ações) |
|---|---:|---:|---:|---:|
| `Clone` | 5,5 µs | 6,3 µs | 6,3 µs | 34 KB |
| `Clone` + aplicar 1 ação | 6,1 µs | 7,1 µs | 7,1 µs | 35 KB |
| `Options()` | 0,2 µs | 0,1 µs | 0,2 µs | 0,6 KB |
| `Hash()` | 47 µs | 70 µs | 89 µs | 276 KB |

## Conclusões

1. **R2 baixo.** O conjunto clone + ação custa cerca de 7 µs, o que dá cerca de 140 mil expansões/s por thread. Em 8–12 threads com *root parallelization* o teto fica em cerca de 1M+/s, de sobra para o budget de 5–15 s.
2. **Novo gargalo: `Hash()`.** Ele é baseado em string e custa de 8 a 14x o clone, crescendo com o tamanho do jogo. O `LethalSolver` o usa na transposition table, então em busca larga o hash domina o custo. Ação proposta: um hash estrutural incremental (estilo Zobrist), calculado só sobre as tags relevantes, antes do ISMCTS.
3. **Pressão de GC:** cerca de 35 KB alocados por clone. Monitorar quando houver paralelismo (Server GC, pooling).

## Atualização: `StateHasher` (hash estrutural)

| Operação | 0 ações | 40 ações | 80 ações |
|---|---:|---:|---:|
| `Game.Hash()` (string) | 46 µs | 68 µs | 85 µs |
| `StateHasher.Compute` | 4,4 µs | 5,3 µs | 6,1 µs |

Ganho de ~10–14x; o `LethalSolver` já usa o `StateHasher`.

## Custo de uma iteração do ISMCTS (1 thread, Basic Mage espelho, turno ~8)

| Componente | Média |
|---|---:|
| Determinização (`HiddenInformationDeterminizer`) | 9 µs |
| Avaliação (`BoardEvaluator`) | 0,2 µs |
| Um turno greedy (rollout) | 565 µs |
| Iteração completa | 860 µs |

O rollout greedy domina, porque clona e avalia cada opção a cada passo. Na arena, com 12 partidas em paralelo, uma decisão de 100 iterações custava ~650 ms com GC de workstation e **~320 ms com Server GC**.

## Aceite do Slice 0 (ISMCTS × greedy, Basic Mage espelho)

| Iterações/decisão | Partidas | Win rate | IC 95% (Wilson) | Assento 1 | Assento 2 | Status |
|---|---:|---:|---|---:|---:|---|
| 100 | 485 (parcial*) | 56,3% | [51,8%, 60,6%] | 81% | 32% | abaixo da meta de 60% |
| 300 | 1000 | **57,4%** | [54,3%, 60,4%] | 85% | 30% | **abaixo da meta de 60%** |

Dados brutos: [arena/2026-10-31-ismcts300-vs-greedy.csv](arena/2026-10-31-ismcts300-vs-greedy.csv). Média de 805 ms por decisão, 39 decisões por partida, 44,7 min de parede com 12 partidas em paralelo.

**Leitura:** o ISMCTS é melhor que o greedy com significância (o IC fica inteiro acima de 50%), mas não chega aos 60%. Triplicar as iterações rendeu só ~1 ponto, então o limite está na **qualidade** da avaliação heurística e do rollout, e não na quantidade de busca. Próximas alavancas: calibrar os pesos da avaliação offline (ADR-0006) e modelar a resposta do oponente dentro da árvore, em vez de usar só a política greedy.

\* A execução foi interrompida no fim da sessão. As partidas terminam fora de ordem (as curtas primeiro), então a amostra parcial pode ter algum viés. Essa rodada também foi anterior à correção de seeds (`Seeds.Derive`).

> `--job short` tem poucas iterações: os números servem como ordem de grandeza, não como medida precisa.
