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

> `--job short` tem poucas iterações: os números servem como ordem de grandeza, não como medida precisa.
