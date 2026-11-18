from __future__ import annotations

import json
import random
from pathlib import Path

from calibration import calibrate, expected_calibration_error, sigmoid, write_weights_json


def test_antisymmetric_platt_scaling_recovers_scale_and_achieves_low_ece(tmp_path: Path) -> None:
    rng = random.Random(42)
    true_scale = 0.095

    scores: list[float] = []
    outcomes: list[float] = []
    for _ in range(2500):
        s = rng.gauss(0.0, 14.0)
        p = sigmoid(true_scale * s)
        y = 1.0 if rng.random() < p else 0.0
        # Each game yields both players' perspectives: (s, y) and (-s, 1 - y).
        scores.extend((s, -s))
        outcomes.extend((y, 1.0 - y))

    # Bad uncalibrated scale has high ECE (> 10 p.p.).
    bad_probs = [sigmoid(0.35 * s + 0.4) for s in scores]
    assert expected_calibration_error(bad_probs, outcomes) > 0.10

    result = calibrate(scores, outcomes)
    assert abs(result.scale - true_scale) < 0.015
    assert result.bias == 0.0
    # ARCH-0001 §2.2 acceptance criterion #3: ECE < 5 percentage points.
    assert result.ece < 0.05

    out = tmp_path / "weights.json"
    write_weights_json(result, out)
    loaded = json.loads(out.read_text(encoding="utf-8"))
    assert loaded["schemaVersion"] == 1
    assert abs(loaded["scale"] - result.scale) < 1e-5
    assert loaded["bias"] == 0.0


def test_two_parameter_platt_scaling_recovers_initiative_bias() -> None:
    rng = random.Random(7)
    true_scale = 0.085
    true_bias = 0.30

    scores: list[float] = []
    outcomes: list[float] = []
    for _ in range(4000):
        s = rng.gauss(0.0, 12.0)
        p = sigmoid(true_scale * s + true_bias)
        scores.append(s)
        outcomes.append(1.0 if rng.random() < p else 0.0)

    result = calibrate(scores, outcomes, fit_bias=True)
    assert abs(result.scale - true_scale) < 0.015
    assert abs(result.bias - true_bias) < 0.08
    assert result.ece < 0.05
