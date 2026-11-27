"""Temporal partitions, calibration, metrics and threshold selection (docs/model_validation.md).

Partitions are chronological: train -> selection -> calibration/threshold -> untouched final test, with optional
gap weeks between them and explicit removal of any TransactionKey already seen in an earlier partition.
Thresholds are chosen on the calibration partition only; the final test is evaluated once.
"""
from __future__ import annotations

import math
from typing import Dict, List, Optional, Sequence

import numpy as np
import pandas as pd
from sklearn.calibration import CalibratedClassifierCV
from sklearn.metrics import average_precision_score, brier_score_loss

from .classifier import ClassifierModel, prepare_matrix

PARTITIONS = ("train", "selection", "calibration", "test")


def temporal_partitions(weeks: Sequence, fractions=(0.55, 0.15, 0.15, 0.15), gap_weeks: int = 1) -> Dict[str, List]:
    weeks = sorted(pd.Timestamp(w) for w in set(weeks))
    n = len(weeks)
    bounds, acc = [], 0.0
    for f in fractions[:-1]:
        acc += f
        bounds.append(int(round(n * acc)))
    edges = [0] + bounds + [n]
    parts = {}
    for i, name in enumerate(PARTITIONS):
        lo = edges[i] + (gap_weeks if i > 0 else 0)
        parts[name] = weeks[lo:edges[i + 1]]
    return parts


def assign_partitions(frame: pd.DataFrame, parts: Dict[str, List]) -> pd.Series:
    """Partition label per row; rows in gap weeks -> None; later rows whose TransactionKey appeared earlier -> None."""
    week = pd.to_datetime(frame["SnapshotWeek"])
    label = pd.Series(None, index=frame.index, dtype=object)
    for name in PARTITIONS:
        label[week.isin(parts[name])] = name
    seen = set()
    for name in PARTITIONS:
        rows = label == name
        dup = rows & frame["TransactionKey"].isin(seen)
        label[dup] = None
        seen.update(frame.loc[rows & ~dup, "TransactionKey"])
    return label


def calibrate(model: ClassifierModel, cal: pd.DataFrame, y: pd.Series, min_sample: int) -> Optional[Dict]:
    """Sigmoid (Platt) calibration on a separate later period. Returns None if the sample is too small."""
    y = y.astype(int)
    if len(y) < min_sample or y.nunique() < 2:
        return None
    calibrator = CalibratedClassifierCV(model.pipeline, method="sigmoid", cv="prefit")
    calibrator.fit(prepare_matrix(cal, model.numeric, model.categorical), y)
    model.calibrator = calibrator
    return {"method": "sigmoid", "rows": int(len(y)), "positives": int(y.sum())}


def wilson_interval(successes: int, n: int, z: float = 1.96) -> tuple:
    if n == 0:
        return (float("nan"), float("nan"))
    p = successes / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def evaluate(scores: np.ndarray, y: np.ndarray, *, capacity: Optional[int] = None, threshold: Optional[float] = None, bins: int = 10) -> Dict:
    """Metrics on REVIEWED rows only (y in {0,1}). Recall is among reviewed positives, never the population."""
    scores, y = np.asarray(scores, dtype=float), np.asarray(y, dtype=int)
    out: Dict = {"reviewed_rows": int(len(y)), "reviewed_positives": int(y.sum())}
    if len(y) == 0:
        return out
    if len(np.unique(y)) == 2:
        out["pr_auc"] = float(average_precision_score(y, scores))
        out["positive_rate"] = float(y.mean())
    if np.all((scores >= 0) & (scores <= 1)):
        out["brier"] = float(brier_score_loss(y, scores))
        edges = np.linspace(0, 1, bins + 1)
        idx = np.clip(np.digitize(scores, edges) - 1, 0, bins - 1)
        out["reliability"] = [{"bin": f"{edges[b]:.1f}-{edges[b + 1]:.1f}", "n": int((idx == b).sum()),
                               "mean_score": float(scores[idx == b].mean()) if (idx == b).any() else None,
                               "observed_rate": float(y[idx == b].mean()) if (idx == b).any() else None} for b in range(bins)]
    if threshold is not None:
        sel = scores >= threshold
        k = int(y[sel].sum())
        out["at_threshold"] = {"threshold": threshold, "selected": int(sel.sum()), "precision": (k / sel.sum()) if sel.sum() else None,
                               "precision_ci95": wilson_interval(k, int(sel.sum())), "recall_among_reviewed": (k / y.sum()) if y.sum() else None}
    if capacity:
        top = np.argsort(-scores)[:capacity]
        k = int(y[top].sum())
        out["at_capacity"] = {"capacity": capacity, "precision": k / len(top), "precision_ci95": wilson_interval(k, len(top))}
    return out


def select_threshold(scores: np.ndarray, y: np.ndarray, *, min_lower_bound: float, min_selected: int, floor: float = 0.0) -> Optional[Dict]:
    """Lowest threshold whose selected set has a Wilson 95% lower precision bound >= min_lower_bound.
    Returns None when no threshold qualifies (publication must then stay disabled)."""
    scores, y = np.asarray(scores, dtype=float), np.asarray(y, dtype=int)
    order = np.argsort(-scores)
    best = None
    tp = 0
    for rank, i in enumerate(order, start=1):
        tp += y[i]
        if rank < min_selected:
            continue
        lower, upper = wilson_interval(tp, rank)
        if lower >= min_lower_bound and scores[i] >= floor:
            best = {"threshold": float(scores[i]), "selected": rank, "precision": tp / rank, "precision_ci95": (lower, upper)}
    return best
