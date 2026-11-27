"""Autoencoder preprocessing, fitted on training rows only and persisted with the model (PREPROCESSING_VERSION p1).

Steps per numeric feature:
  1. Transform: signed log1p for amounts/measurements (negative values are valid: credits, differences);
     clipping for ratios and robust z-scores so a single extreme value cannot dominate.
  2. Median imputation + an explicit missing indicator (missing is not zero).
  3. Robust scaling (median / IQR) and clipping to [-10, 10].
Feature-group weights make each AE group (FINANCIAL, MEASUREMENT, RECONCILIATION) contribute equally to the
reconstruction error regardless of how many columns it has.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List

import numpy as np
import pandas as pd

from .features import SPEC_BY_NAME

PREPROCESSING_VERSION = "p1"
CLIP_RATIO = 10.0
CLIP_Z = 20.0


def transform_kind(name: str) -> str:
    spec = SPEC_BY_NAME[name]
    if spec.kind == "flag":
        return "flag"
    if name.endswith("RobustZ"):
        return "z"
    if "Ratio" in name:  # includes FuelRatioDifference and the billed-vs-dispatch ratios
        return "ratio"
    return "log"


def apply_transform(values: pd.Series, kind: str) -> pd.Series:
    v = pd.to_numeric(values, errors="coerce").astype("float64")
    if kind == "log":
        return np.sign(v) * np.log1p(np.abs(v))
    if kind == "ratio":
        return v.clip(-CLIP_RATIO, CLIP_RATIO)
    if kind == "z":
        return v.clip(-CLIP_Z, CLIP_Z)
    return v


@dataclass
class AEPreprocessor:
    features: List[str]
    medians: Dict[str, float] = field(default_factory=dict)
    centers: Dict[str, float] = field(default_factory=dict)
    scales: Dict[str, float] = field(default_factory=dict)
    indicator_for: List[str] = field(default_factory=list)
    output_names: List[str] = field(default_factory=list)
    groups: List[str] = field(default_factory=list)
    weights: np.ndarray = None
    version: str = PREPROCESSING_VERSION

    def fit(self, df: pd.DataFrame) -> "AEPreprocessor":
        self.indicator_for = []
        for name in self.features:
            t = apply_transform(df[name], transform_kind(name))
            med = float(t.median()) if t.notna().any() else 0.0
            filled = t.fillna(med)
            q1, q3 = np.nanpercentile(filled, [25, 75]) if len(filled) else (0.0, 1.0)
            iqr = float(q3 - q1)
            std = float(filled.std()) if len(filled) > 1 else 0.0
            self.medians[name] = med
            self.centers[name] = med
            self.scales[name] = iqr if iqr > 0 else (std if std > 0 else 1.0)
            if t.isna().any() and transform_kind(name) != "flag":
                self.indicator_for.append(name)
        self.output_names = list(self.features) + [f"{n}__missing" for n in self.indicator_for]
        self.groups = [SPEC_BY_NAME[n].ae_group for n in self.features] + [SPEC_BY_NAME[n].ae_group for n in self.indicator_for]
        counts = pd.Series(self.groups).value_counts()
        n_groups = len(counts)
        raw = np.array([1.0 / (counts[g] * n_groups) for g in self.groups])
        self.weights = raw / raw.mean()  # mean weight 1: each group contributes 1/n_groups of the error
        return self

    def transform(self, df: pd.DataFrame) -> np.ndarray:
        cols = []
        for name in self.features:
            t = apply_transform(df[name], transform_kind(name)).fillna(self.medians[name])
            cols.append(((t - self.centers[name]) / self.scales[name]).clip(-10, 10).to_numpy())
        for name in self.indicator_for:
            cols.append(pd.to_numeric(df[name], errors="coerce").isna().astype(float).to_numpy())
        return np.column_stack(cols).astype("float64")

    def group_names(self) -> List[str]:
        return sorted(set(self.groups))
