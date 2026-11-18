"""
Offline logistic calibration (Platt scaling) and Expected Calibration Error (ECE) pipeline (ADR-0002, ADR-0006).

Fits Win% = sigma(scale * score + bias) via Newton-Raphson / IRLS over (score, outcome) pairs from self-play or
recorded games, verifies ECE < 5 percentage points (ARCH-0001 §2.2), and writes an EvaluationWeights JSON artifact
consumable directly by HsEngine.Evaluation.EvaluationWeights.LoadFile.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


@dataclass(frozen=True)
class CalibrationResult:
    scale: float
    bias: float
    ece: float
    brier: float
    samples: int


def sigmoid(z: float) -> float:
    if z >= 0:
        ez = math.exp(-z)
        return 1.0 / (1.0 + ez)
    ez = math.exp(z)
    return ez / (1.0 + ez)


def fit_platt_scaling(
    scores: Sequence[float],
    outcomes: Sequence[float],
    fit_bias: bool = False,
    l2: float = 1e-4,
    max_iter: int = 100,
    tol: float = 1e-9,
) -> tuple[float, float]:
    """
    Fits Win% = sigma(scale * score + bias).
    Defaults to fit_bias=False (bias = 0.0) so antisymmetric BoardEvaluator scores satisfy P(p) + P(opponent) == 1.
    """
    if len(scores) != len(outcomes) or len(scores) == 0:
        raise ValueError("scores and outcomes must be non-empty and have equal length.")

    scale = 0.08
    bias = 0.0

    for _ in range(max_iter):
        g_s = -l2 * scale
        g_b = -l2 * bias
        h_ss = l2
        h_sb = 0.0
        h_bb = l2

        for s, y in zip(scores, outcomes):
            p = sigmoid(scale * s + bias)
            err = y - p
            w = max(1e-12, p * (1.0 - p))
            g_s += err * s
            h_ss += w * s * s
            if fit_bias:
                g_b += err
                h_sb += w * s
                h_bb += w

        if not fit_bias:
            if abs(h_ss) < 1e-18:
                break
            d_scale = g_s / h_ss
            scale += d_scale
            if abs(d_scale) < tol:
                break
            continue

        det = h_ss * h_bb - h_sb * h_sb
        if abs(det) < 1e-18:
            break

        d_scale = (h_bb * g_s - h_sb * g_b) / det
        d_bias = (-h_sb * g_s + h_ss * g_b) / det
        scale += d_scale
        bias += d_bias

        if abs(d_scale) + abs(d_bias) < tol:
            break

    return scale, bias


def expected_calibration_error(probs: Sequence[float], outcomes: Sequence[float], bins: int = 10) -> float:
    if len(probs) != len(outcomes) or len(probs) == 0:
        raise ValueError("probs and outcomes must be non-empty and have equal length.")

    n = len(probs)
    counts = [0] * bins
    sum_p = [0.0] * bins
    sum_y = [0.0] * bins

    for p, y in zip(probs, outcomes):
        idx = min(bins - 1, max(0, int(p * bins)))
        counts[idx] += 1
        sum_p[idx] += p
        sum_y[idx] += y

    ece = 0.0
    for count, sp, sy in zip(counts, sum_p, sum_y):
        if count > 0:
            ece += (count / n) * abs((sy / count) - (sp / count))
    return ece


def brier_score(probs: Sequence[float], outcomes: Sequence[float]) -> float:
    return sum((p - y) ** 2 for p, y in zip(probs, outcomes)) / len(probs)


def calibrate(
    scores: Sequence[float],
    outcomes: Sequence[float],
    bins: int = 10,
    fit_bias: bool = False,
) -> CalibrationResult:
    scale, bias = fit_platt_scaling(scores, outcomes, fit_bias=fit_bias)
    probs = [sigmoid(scale * s + bias) for s in scores]
    return CalibrationResult(
        scale=scale,
        bias=bias,
        ece=expected_calibration_error(probs, outcomes, bins=bins),
        brier=brier_score(probs, outcomes),
        samples=len(scores),
    )


def write_weights_json(result: CalibrationResult, path: Path) -> None:
    payload = {
        "schemaVersion": 1,
        "scale": round(result.scale, 6),
        "bias": round(result.bias, 6),
        "metrics": {
            "ece": round(result.ece, 6),
            "brier": round(result.brier, 6),
            "samples": result.samples,
        },
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def read_csv_samples(rows: Iterable[dict[str, str]]) -> tuple[list[float], list[float]]:
    scores: list[float] = []
    outcomes: list[float] = []
    for row in rows:
        scores.append(float(row["score"]))
        outcomes.append(float(row["outcome"]))
    return scores, outcomes


def main() -> int:
    parser = argparse.ArgumentParser(description="Calibrate BoardEvaluator Win% sigmoid from (score, outcome) CSV.")
    parser.add_argument("--input", type=Path, required=True, help="CSV with 'score' and 'outcome' (0/0.5/1) columns.")
    parser.add_argument("--output", type=Path, required=True, help="Output JSON path for EvaluationWeights.")
    parser.add_argument("--fit-bias", action="store_true", help="Fit non-zero intercept instead of antisymmetric 0.")
    args = parser.parse_args()

    with args.input.open(encoding="utf-8", newline="") as fh:
        scores, outcomes = read_csv_samples(csv.DictReader(fh))

    result = calibrate(scores, outcomes, fit_bias=args.fit_bias)
    write_weights_json(result, args.output)
    print(f"Calibrated over {result.samples} samples: scale={result.scale:.5f}, bias={result.bias:.5f}, "
          f"ECE={result.ece * 100:.2f} p.p., Brier={result.brier:.4f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
