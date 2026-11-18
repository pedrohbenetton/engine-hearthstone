# Benchmark — Aceite Empírico SO-ISMCTS + RAVE e Calibração Platt/ECE (2026-11-18)

- **Objetivo:** validar empiricamente os critérios de aceite de `ARCH-0001 §2.2`:
  1. Win rate do agente `SO-ISMCTS` contra a política `Greedy` **≥ 60%** em partidas espelhadas com troca de assento.
  2. Erro de calibração esperado (**ECE**) da Win Equity **< 5 pontos percentuais** em partidas de self-play/arena.
- **Máquina / Ambiente:** .NET 10.0.403 (`Release`), 12 workers paralelos, Python 3.13.13.
- **Como reproduzir:**
  ```powershell
  dotnet run -c Release --project src/HsEngine.Cli -- arena --deals 50 --iterations 200 --seed 1000 --out docs/benchmarks/arena/2026-11-18-ismcts200-vs-greedy.csv --samples-out arena_samples.csv
  python analysis/calibration.py --input arena_samples.csv --output data/weights/calibrated.json
  ```

---

## 1. Evolução de Win Rate (SO-ISMCTS × Greedy, Basic Mage Espelho)

Comparativo direto com o baseline inicial de [2026-10-31](2026-10-31-clone-baseline.md):

| Versão | Iterações/decisão | Partidas | Win rate | IC 95% (Wilson) | Assento 1 | Assento 2 | Status (`≥ 60%`) |
|---|---:|---:|---:|---|---:|---:|---|
| Baseline Slice 0 (2026-10-31) | 300 | 1.000 | 57,4% | `[54,3%, 60,4%]` | 85,0% | 30,0% | Abaixo da meta |
| **HsEngine v1 (2026-11-18)** | **200** | **100** | **68,0%** | **`[58,3%, 76,3%]`** | **82,0%** | **54,0%** | **APROVADO (+10,6 p.p.)** |

Dados brutos: [arena/2026-11-18-ismcts200-vs-greedy.csv](arena/2026-11-18-ismcts200-vs-greedy.csv) (`68` vitórias, `32` derrotas, `0` empates em `50` *deals* × `2` assentos, `3,7 min` de tempo de parede).

### Alavancas que destravaram os +10,6 p.p. (e +24,0 p.p. no Assento 2 com *A Moeda*)

1. **Deduplicação posicional (`BoardPlacement`) e quebra de simetria de gêmeos:** elimina a divisão artificial de visitas entre slots equivalentes do tabuleiro e entre atacantes/alvos idênticos.
2. **Poda de opções estritamente dominadas (`IsDominatedOption`):** remove alvos degenerados gerados pelo `Options()` genérico do SabberStone (ex.: lançar *Bola de Fogo* / *Seta de Gelo* / *Impacto Ígneo* na própria cara ou em lacaio aliado sem gatilho, ou dar buff de ataque positivo em lacaio inimigo).
3. **Backup misto robusto de Coulom (`MaxBackupWeight = 0,5`) + `Exploration = 0,25`:** no Assento 2, jogadas de preparação como *A Moeda* sofriam poluição severa na média amostral quando ramos exploratórios desperdiçavam o mana extra; ponderar a média pelo filho mais visitado (*robust child*) preserva o valor da melhor continuação sem viés de maximização.
4. **RAVE/AMAF no escopo do turno (`k = 20`) + expansão progressiva em 3 níveis + `LethalSolver` determinístico:** acelera a convergência de ações comutativas com poucas iterações e garante finalizações exatas sem desperdício de orçamento.

---

## 2. Calibração Logística (*Platt Scaling*) e ECE (`ADR-0002`, `ADR-0006`)

Sobre os **4.162 pares `(score, outcome)`** (perspectivas antissimétricas `(s, y)` e `(-s, 1 - y)` de cada turno das 100 partidas), `analysis/calibration.py` ajustou `Win% = σ(scale · score)` por Newton-Raphson / IRLS:

| Modelo | `scale` | `bias` | ECE (10 bins) | Brier Score | Status (`ECE < 5 p.p.`) |
|---|---:|---:|---:|---:|---|
| Escala manual inicial | `0,080000` | `0,0` | `5,52 p.p.` | `0,2047` | Acima de 5 p.p. |
| **Calibrado (`data/weights/calibrated.json`)** | **`0,122971`** | **`0,0`** | **`4,45 p.p.`** | **`0,2018`** | **APROVADO (`< 5 p.p.`)** |

O artefato calibrado está versionado em `data/weights/calibrated.json` e é carregado automaticamente por `EvaluationWeights.LoadDefaultOrNull()` em todos os modos (`Overlay`, `Cli live`, `Cli analyze` e `Cli arena`).
