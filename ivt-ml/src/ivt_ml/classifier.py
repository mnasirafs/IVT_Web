"""Hybrid logistic regression on transaction features, peer deviations, availability flags and OUT-OF-SAMPLE
autoencoder scores.

Two registered kinds, never confused:
  CLASSIFIER_CONFIRMED         trained on CONFIRMED review outcomes only; after separate-period calibration it may
                               provide ConfirmedExceptionProbability.
  CLASSIFIER_WEAK_EXPERIMENTAL trained on weak hold targets; predicts historical HOLD BEHAVIOUR. Its output is stored
                               only as WeakFlagScore in ML.ScoreAudit and is never published as a probability.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional

import joblib
import numpy as np
import pandas as pd
from sklearn.compose import ColumnTransformer
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import OneHotEncoder, StandardScaler

from .autoencoder import GROUP_COLUMNS, dependency_versions, sha256_file, train_autoencoder
from .features import CATEGORICAL, SPEC_BY_NAME
from .preprocessing import apply_transform, transform_kind

AE_FEATURES = ["AEAnomalyPercentile", "AELogReconstructionError"] + [f"AELog{c}" for c in GROUP_COLUMNS.values()]
AE_LABELS = {"AEAnomalyPercentile": "Overall unusualness (autoencoder)", "AELogReconstructionError": "Reconstruction error (autoencoder)",
             "AELogFinancialReconstructionError": "Unusual financial pattern", "AELogMeasurementReconstructionError": "Unusual measurement pattern",
             "AELogReconciliationReconstructionError": "Unusual reconciliation pattern"}
CONTRIBUTION_METHOD = "linear log-odds: coefficient x standardized feature value (relative to the training mean)"


def add_ae_columns(frame: pd.DataFrame, ae_scores: pd.DataFrame) -> pd.DataFrame:
    out = frame.copy()
    out["AEAnomalyPercentile"] = ae_scores["AEAnomalyPercentile"]
    out["AELogReconstructionError"] = np.log1p(ae_scores["AEReconstructionError"])
    for c in GROUP_COLUMNS.values():
        out[f"AELog{c}"] = np.log1p(ae_scores[c])
    return out


def prepare_matrix(frame: pd.DataFrame, numeric: List[str], categorical: List[str]) -> pd.DataFrame:
    x = pd.DataFrame(index=frame.index)
    for n in numeric:
        x[n] = apply_transform(frame[n], transform_kind(n)) if n in SPEC_BY_NAME else pd.to_numeric(frame[n], errors="coerce")
    for c in categorical:
        x[c] = frame[c].astype("object").where(frame[c].notna(), None)
    return x


@dataclass
class ClassifierModel:
    kind: str
    numeric: List[str]
    categorical: List[str]
    pipeline: Pipeline
    calibrator: Optional[object] = None
    meta: Dict = field(default_factory=dict)

    def raw_score(self, frame: pd.DataFrame) -> np.ndarray:
        return self.pipeline.predict_proba(prepare_matrix(frame, self.numeric, self.categorical))[:, 1]

    def calibrated_probability(self, frame: pd.DataFrame) -> Optional[np.ndarray]:
        """Calibrated confirmed-exception probability, or None when this model cannot provide one."""
        if self.kind != "CLASSIFIER_CONFIRMED" or self.calibrator is None:
            return None
        return self.calibrator.predict_proba(prepare_matrix(frame, self.numeric, self.categorical))[:, 1]

    def contributions(self, frame: pd.DataFrame, top: int = 3) -> List[List[dict]]:
        pre = self.pipeline.named_steps["pre"]
        lr = self.pipeline.named_steps["lr"]
        z = pre.transform(prepare_matrix(frame, self.numeric, self.categorical))
        z = z.toarray() if hasattr(z, "toarray") else z
        contrib = z * lr.coef_[0]
        names = pre.get_feature_names_out()
        result = []
        for row in contrib:
            idx = np.argsort(-row)[:top]
            result.append([{"feature": names[i], "label": business_label(names[i]), "logOddsContribution": round(float(row[i]), 4)}
                           for i in idx if row[i] > 0])
        return result

    def save(self, directory: Path) -> Dict[str, str]:
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / "classifier.joblib"
        joblib.dump(self, path)
        (directory / "classifier_meta.json").write_text(json.dumps(self.meta, indent=2, default=str), encoding="utf-8")
        return {"uri": str(path.resolve()), "sha256": sha256_file(path)}

    @staticmethod
    def load(path: Path, expected_sha256: Optional[str] = None) -> "ClassifierModel":
        if expected_sha256 and sha256_file(path) != expected_sha256:
            raise ValueError(f"Artifact checksum mismatch for {path}")
        return joblib.load(path)


def business_label(transformed_name: str) -> str:
    """Map a ColumnTransformer output name back to a business label."""
    name = transformed_name.split("__", 1)[-1]
    if name.startswith("missingindicator_"):
        base = name[len("missingindicator_"):]
        return f"{SPEC_BY_NAME[base].label if base in SPEC_BY_NAME else AE_LABELS.get(base, base)} is missing"
    for cat in CATEGORICAL:
        if name.startswith(cat + "_"):
            value = name[len(cat) + 1:]
            return f"{SPEC_BY_NAME[cat].label} = {'(other)' if value.startswith('infrequent') else value}"
    if name in SPEC_BY_NAME:
        return SPEC_BY_NAME[name].label
    return AE_LABELS.get(name, name)


def build_pipeline(numeric: List[str], categorical: List[str], *, C: float, class_weight: Optional[str], min_category_frequency: int, seed: int) -> Pipeline:
    pre = ColumnTransformer([
        ("num", Pipeline([("impute", SimpleImputer(strategy="median", add_indicator=True)), ("scale", StandardScaler())]), numeric),
        ("cat", Pipeline([("impute", SimpleImputer(strategy="constant", fill_value="~missing")),
                          ("onehot", OneHotEncoder(handle_unknown="infrequent_if_exist", min_frequency=min_category_frequency, sparse_output=False))]), categorical),
    ], verbose_feature_names_out=True)
    lr = LogisticRegression(C=C, penalty="l2", class_weight=class_weight, max_iter=3000, random_state=seed)
    return Pipeline([("pre", pre), ("lr", lr)])


def train_classifier(train: pd.DataFrame, target: pd.Series, *, kind: str, numeric: List[str], categorical: List[str], C: float = 0.5,
                     class_weight: Optional[str] = "balanced", min_category_frequency: int = 30, seed: int = 42) -> ClassifierModel:
    if kind not in ("CLASSIFIER_CONFIRMED", "CLASSIFIER_WEAK_EXPERIMENTAL"):
        raise ValueError(kind)
    y = target.astype(int)
    if y.nunique() < 2:
        raise ValueError("Classifier training needs both classes in the training period.")
    pipe = build_pipeline(numeric, categorical, C=C, class_weight=class_weight, min_category_frequency=min_category_frequency, seed=seed)
    pipe.fit(prepare_matrix(train, numeric, categorical), y)
    meta = {"kind": kind, "C": C, "class_weight": class_weight, "class_weight_note": "class_weight='balanced' distorts probabilities; raw scores are uncalibrated.",
            "train_rows": int(len(y)), "train_positives": int(y.sum()), "numeric": numeric, "categorical": categorical,
            "min_category_frequency": min_category_frequency, "unknown_category_handling": "infrequent_if_exist (unseen -> infrequent bucket or all-zero)",
            "contribution_method": CONTRIBUTION_METHOD, "dependencies": dependency_versions(), "seed": seed}
    return ClassifierModel(kind, numeric, categorical, pipe, None, meta)


def out_of_sample_ae_scores(frames_by_week: Dict[pd.Timestamp, pd.DataFrame], ae_features: List[str], *, min_train_weeks: int, block_weeks: int,
                            gap_weeks: int, exclude: Optional[pd.Series] = None, seed: int = 42, max_rows: int = 80000,
                            log=print) -> pd.DataFrame:
    """Chronological expanding-window AE scores for classifier training (no in-sample AE scores).

    For each block of `block_weeks` weeks starting after `min_train_weeks`, an AE is fitted only on weeks that end
    at least `gap_weeks` before the block, and the block is scored with it. Rows before the first block get no score.
    """
    weeks = sorted(frames_by_week)
    scored = []
    start = min_train_weeks
    while start < len(weeks):
        block = weeks[start:start + block_weeks]
        train_weeks = weeks[: max(0, start - gap_weeks)]
        if not train_weeks:
            start += block_weeks
            continue
        train = pd.concat([frames_by_week[w] for w in train_weeks])
        mask = exclude.reindex(train.index).fillna(False).astype(bool) if exclude is not None else None
        ae = train_autoencoder(train, ae_features, seed=seed, max_rows=max_rows, exclude_mask=mask)
        for w in block:
            s = ae.score(frames_by_week[w])
            s["OOFTrainEnd"] = train_weeks[-1]
            scored.append(s)
        log(f"OOF AE: trained on {len(train_weeks)} weeks up to {pd.Timestamp(train_weeks[-1]).date()}, scored {len(block)} weeks")
        start += block_weeks
    return pd.concat(scored) if scored else pd.DataFrame()
