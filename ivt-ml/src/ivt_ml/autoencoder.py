"""Tabular dense autoencoder (scikit-learn MLPRegressor trained to reconstruct its input) plus benchmarks.

* Architecture defaults to Input -> 32 -> 16 -> 8 -> 16 -> 32 -> Reconstruction, shrunk when the input is small.
* Deterministic seeds, early stopping, and recorded training metadata.
* Reference distribution: reconstruction errors of a chronologically LATER slice of the training window that the
  network did not train on, frozen and versioned with the model. AEAnomalyPercentile is the share of reference
  transactions with a lower error. It is a measure of unusualness, never an exception probability.
"""
from __future__ import annotations

import hashlib
import json
import platform
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional

import joblib
import numpy as np
import pandas as pd
import sklearn
from sklearn.ensemble import IsolationForest
from sklearn.neural_network import MLPRegressor

from .features import FEATURE_VERSION, SPEC_BY_NAME
from .preprocessing import AEPreprocessor

GROUP_COLUMNS = {"FINANCIAL": "FinancialReconstructionError", "MEASUREMENT": "MeasurementReconstructionError",
                 "RECONCILIATION": "ReconciliationReconstructionError"}


def architecture_for(n_inputs: int, base=(32, 16, 8)) -> tuple:
    """32-16-8 bottleneck, narrowed for small inputs so every layer is smaller than the one before it."""
    first = max(4, min(base[0], n_inputs - 1))
    second = max(3, min(base[1], first // 2))
    bottleneck = max(2, min(base[2], second // 2))
    return (first, second, bottleneck, second, first)


@dataclass
class AutoencoderModel:
    features: List[str]
    preprocessor: AEPreprocessor
    network: MLPRegressor
    reference_errors: np.ndarray
    meta: Dict = field(default_factory=dict)

    # ---------- scoring ----------
    def _weighted_sq_errors(self, df: pd.DataFrame) -> np.ndarray:
        x = self.preprocessor.transform(df)
        w = np.sqrt(self.preprocessor.weights)
        xw = x * w
        recon = self.network.predict(xw)
        if recon.ndim == 1:
            recon = recon.reshape(-1, 1)
        return (recon - xw) ** 2

    def score(self, df: pd.DataFrame) -> pd.DataFrame:
        sq = self._weighted_sq_errors(df)
        total = sq.mean(axis=1)
        out = pd.DataFrame(index=df.index)
        out["AEReconstructionError"] = total
        out["AEAnomalyPercentile"] = percentile_of(total, self.reference_errors)
        groups = np.array(self.preprocessor.groups)
        for g, col in GROUP_COLUMNS.items():
            mask = groups == g
            out[col] = sq[:, mask].sum(axis=1) / sq.shape[1] if mask.any() else np.nan
        names = np.array(self.preprocessor.output_names)
        top_idx = np.argsort(-sq, axis=1)[:, :3]
        out["TopReconstructionFeatures"] = [json.dumps([n.replace("__missing", " (missing)") for n in names[i]]) for i in top_idx]
        return out

    # ---------- persistence ----------
    def save(self, directory: Path) -> Dict[str, str]:
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / "autoencoder.joblib"
        joblib.dump({"features": self.features, "preprocessor": self.preprocessor, "network": self.network,
                     "reference_errors": self.reference_errors, "meta": self.meta}, path)
        (directory / "autoencoder_meta.json").write_text(json.dumps(self.meta, indent=2, default=str), encoding="utf-8")
        return {"uri": str(path.resolve()), "sha256": sha256_file(path)}

    @staticmethod
    def load(path: Path, expected_sha256: Optional[str] = None) -> "AutoencoderModel":
        if expected_sha256 and sha256_file(path) != expected_sha256:
            raise ValueError(f"Artifact checksum mismatch for {path}")
        d = joblib.load(path)
        return AutoencoderModel(d["features"], d["preprocessor"], d["network"], d["reference_errors"], d["meta"])


def percentile_of(values: np.ndarray, reference: np.ndarray) -> np.ndarray:
    ref = np.sort(np.asarray(reference))
    if len(ref) == 0:
        return np.full(len(values), np.nan)
    return 100.0 * np.searchsorted(ref, values, side="right") / len(ref)


def train_autoencoder(train: pd.DataFrame, features: List[str], *, seed: int = 42, max_rows: int = 150000,
                      reference_fraction: float = 0.15, hidden: Optional[tuple] = None, max_iter: int = 200,
                      exclude_mask: Optional[pd.Series] = None) -> AutoencoderModel:
    """Fit on normal-reference rows. `exclude_mask` marks known/suspected anomalies (confirmed positives and,
    by policy, weak hold positives) that must not shape the definition of "normal"."""
    started = time.time()
    data = train if exclude_mask is None else train[~exclude_mask.reindex(train.index).fillna(False).astype(bool)]
    data = data.sort_values("SnapshotWeek", kind="stable")
    weeks = pd.to_datetime(data["SnapshotWeek"])
    ref_weeks = sorted(weeks.unique())
    # Chronological reference: the latest weeks holding about `reference_fraction` of rows.
    cutoff_idx = int(len(data) * (1 - reference_fraction))
    split_week = weeks.iloc[min(cutoff_idx, len(data) - 1)]
    fit_part = data[weeks < split_week] if (weeks < split_week).sum() >= 50 else data.iloc[:cutoff_idx]
    ref_part = data.drop(fit_part.index)
    if len(fit_part) > max_rows:
        fit_part = fit_part.sample(max_rows, random_state=seed)
    pre = AEPreprocessor(features).fit(fit_part)
    x = pre.transform(fit_part) * np.sqrt(pre.weights)
    layers = hidden or architecture_for(x.shape[1])
    net = MLPRegressor(hidden_layer_sizes=layers, activation="relu", alpha=1e-4, learning_rate_init=1e-3, batch_size=256,
                       early_stopping=True, validation_fraction=0.1, n_iter_no_change=8, max_iter=max_iter, random_state=seed)
    net.fit(x, x)
    model = AutoencoderModel(features, pre, net, np.array([]))
    ref_errors = model._weighted_sq_errors(ref_part).mean(axis=1) if len(ref_part) else model._weighted_sq_errors(fit_part).mean(axis=1)
    model.reference_errors = np.sort(ref_errors)
    model.meta = {
        "feature_version": FEATURE_VERSION, "preprocessing_version": pre.version, "architecture": [x.shape[1], *layers, x.shape[1]],
        "seed": seed, "fit_rows": int(len(fit_part)), "reference_rows": int(len(ref_part)), "excluded_rows": int(len(train) - len(data)),
        "training_weeks": [str(pd.Timestamp(w).date()) for w in (ref_weeks[0], ref_weeks[-1])] if ref_weeks else [],
        "reference_split_week": str(pd.Timestamp(split_week).date()), "iterations": int(net.n_iter_), "best_validation_score": float(getattr(net, "best_validation_score_", np.nan) or np.nan),
        "group_weights": {g: float(np.sum(pre.weights[np.array(pre.groups) == g])) for g in pre.group_names()},
        "dependencies": dependency_versions(), "train_seconds": round(time.time() - started, 1),
        "percentile_note": "AEAnomalyPercentile = share of frozen reference transactions with lower reconstruction error; not a probability.",
    }
    return model


def contamination_sensitivity(train: pd.DataFrame, features: List[str], suspected: pd.Series, probe: pd.DataFrame, seed: int = 42, max_rows: int = 60000) -> Dict:
    """Retrain with suspected anomalies INCLUDED and compare probe-set rankings with the clean model."""
    clean = train_autoencoder(train, features, seed=seed, max_rows=max_rows, exclude_mask=suspected)
    dirty = train_autoencoder(train, features, seed=seed, max_rows=max_rows, exclude_mask=None)
    a = clean.score(probe)["AEReconstructionError"].rank()
    b = dirty.score(probe)["AEReconstructionError"].rank()
    top_a = set(a.nlargest(max(1, len(a) // 100)).index)
    top_b = set(b.nlargest(max(1, len(b) // 100)).index)
    return {"spearman_rank_correlation": float(a.corr(b)), "top1pct_overlap": len(top_a & top_b) / max(1, len(top_a)),
            "suspected_rows": int(suspected.reindex(train.index).fillna(False).sum())}


# ---------- benchmarks ----------
def peer_deviation_score(features: pd.DataFrame) -> pd.Series:
    """Robust peer baseline benchmark: the largest absolute robust z-score across peer metrics."""
    cols = [c for c in features.columns if c.endswith("RobustZ")]
    return features[cols].abs().max(axis=1)


@dataclass
class IsolationForestModel:
    preprocessor: AEPreprocessor
    forest: IsolationForest

    def score(self, df: pd.DataFrame) -> pd.Series:
        return pd.Series(-self.forest.score_samples(self.preprocessor.transform(df)), index=df.index)


def train_isolation_forest(train: pd.DataFrame, features: List[str], seed: int = 42, max_rows: int = 150000) -> IsolationForestModel:
    part = train.sample(min(max_rows, len(train)), random_state=seed)
    pre = AEPreprocessor(features).fit(part)
    forest = IsolationForest(n_estimators=200, max_samples=min(4096, len(part)), random_state=seed, n_jobs=-1).fit(pre.transform(part))
    return IsolationForestModel(pre, forest)


def dependency_versions() -> Dict[str, str]:
    return {"python": platform.python_version(), "scikit-learn": sklearn.__version__, "numpy": np.__version__,
            "pandas": pd.__version__, "joblib": joblib.__version__}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()
