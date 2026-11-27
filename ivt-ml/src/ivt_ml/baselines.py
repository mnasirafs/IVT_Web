"""Point-in-time peer baselines (docs/feature_dictionary.md, "Historical peer features").

For a scoring week W, peers are snapshots with  W - lookback <= SnapshotWeek < W  (strictly earlier weeks).
Because an IVT job appears in exactly one week (verified in profiling), this also excludes the scored
transaction itself; future records and later corrections can never enter a baseline.

Hierarchy (first level with at least MinimumPeerCount observations wins; every key also carries the
segment and currency so credits, AUD and NZD amounts are never compared with each other):
  L1 Customer | Carrier | Lane | WorkCode
  L2 CustomerGroup | CarrierGroup | Lane | WorkCode
  L3 CarrierGroup | Lane | WorkCode
  L4 CarrierGroup | WorkCode
  L5 CarrierGroup
Robust z = (value - median) / (1.4826 * MAD). When MAD = 0 the spread falls back to (P90 - P10) / 2.5631
(the normal-consistent inter-decile scale); if that is also 0 the z-score is missing and ZeroSpreadFlag = 1.
"""
from __future__ import annotations

from typing import Dict, List

import numpy as np
import pandas as pd

BASELINE_METRICS = ["BuyNett", "SellNett", "MarginRatio", "BuyFuelRatio", "BuyPerChargeableKg"]
LEVELS: Dict[int, List[str]] = {
    1: ["Customer", "Carrier", "Lane", "WorkCode"],
    2: ["CustomerGroup", "CarrierGroup", "Lane", "WorkCode"],
    3: ["CarrierGroup", "Lane", "WorkCode"],
    4: ["CarrierGroup", "WorkCode"],
    5: ["CarrierGroup"],
}
BASELINE_VERSION = "b1"
MAD_SCALE = 1.4826
DECILE_SCALE = 2.5631  # (P90 - P10) of a standard normal


def group_key(df: pd.DataFrame, level: int) -> pd.Series:
    cols = ["Segment", "CurrencyKey"] + LEVELS[level]
    parts = [df[c].fillna("~").astype(str) for c in cols]
    key = parts[0].copy()
    for p in parts[1:]:
        key = key + "|" + p
    return f"L{level}|" + key


def _prepare(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    for c in out.columns:
        if isinstance(out[c].dtype, pd.CategoricalDtype):
            out[c] = out[c].astype(object)
    out["Lane"] = out["FromLocation"].fillna("~").astype(str).str.upper().str.strip() + ">" + out["ToLocation"].fillna("~").astype(str).str.upper().str.strip()
    out["CarrierGroup"] = out["CarrierGroup"].fillna(out["Carrier"])
    out["CustomerGroup"] = out["CustomerGroup"].fillna(out["Customer"])
    out["CurrencyKey"] = out["CurrencyCode"].fillna("UNK")
    return out


def compute_baselines(history: pd.DataFrame, cutoff_week: pd.Timestamp, lookback_weeks: int) -> pd.DataFrame:
    """Baseline statistics for every peer group at every level, from the window strictly before the cutoff."""
    cutoff_week = pd.Timestamp(cutoff_week).normalize()
    window_start = cutoff_week - pd.Timedelta(weeks=lookback_weeks)
    weeks = pd.to_datetime(history["SnapshotWeek"])
    window = history[(weeks >= window_start) & (weeks < cutoff_week) & (history["BuySellMatchStatus"] != "AMBIGUOUS")]
    if window.empty:
        return pd.DataFrame(columns=["BaselineVersion", "BaselineCutoff", "WindowStart", "WindowEnd", "FallbackLevel", "PeerGroupKey",
                                     "MetricName", "ObservationCount", "Median", "MAD", "P10", "P90"])
    window = _prepare(window)
    window_end = pd.to_datetime(window["SnapshotWeek"]).max()
    frames = []
    for level in LEVELS:
        key = group_key(window, level)
        for metric in BASELINE_METRICS:
            values = pd.to_numeric(window[metric], errors="coerce")
            valid = values.notna()
            if not valid.any():
                continue
            v, k = values[valid], key[valid]
            g = v.groupby(k)
            med = g.median()
            mad = (v - k.map(med)).abs().groupby(k).median()
            q = g.quantile([0.1, 0.9]).unstack()
            frames.append(pd.DataFrame({"FallbackLevel": level, "PeerGroupKey": med.index, "MetricName": metric, "ObservationCount": g.size().values,
                                        "Median": med.values, "MAD": mad.reindex(med.index).values, "P10": q[0.1].reindex(med.index).values,
                                        "P90": q[0.9].reindex(med.index).values}))
    out = pd.concat(frames, ignore_index=True)
    out.insert(0, "BaselineVersion", BASELINE_VERSION)
    out.insert(1, "BaselineCutoff", cutoff_week)
    out.insert(2, "WindowStart", window_start)
    out.insert(3, "WindowEnd", window_end)
    return out


def attach_peer_features(target: pd.DataFrame, baselines: pd.DataFrame, min_count: int) -> pd.DataFrame:
    """Adds robust z-scores and peer statistics per metric, choosing the most specific supported level."""
    t = _prepare(target)
    out = pd.DataFrame(index=target.index)
    if baselines is None or baselines.empty:
        for m in BASELINE_METRICS:
            out[f"{m}RobustZ"], out[f"{m}PeerMedian"], out[f"{m}PeerSpread"] = np.nan, np.nan, np.nan
        out["PeerCount"], out["PeerFallbackLevel"], out["PeerGroupKey"] = 0, 0, None
        out["SparsePeerFlag"], out["ZeroSpreadFlag"], out["BaselineCutoff"], out["PeerWindowStart"], out["PeerWindowEnd"] = 1, 0, pd.NaT, pd.NaT, pd.NaT
        return out
    keys = {level: group_key(t, level) for level in LEVELS}
    zero_spread = pd.Series(False, index=target.index)
    for metric in BASELINE_METRICS:
        b = baselines[baselines["MetricName"] == metric].set_index("PeerGroupKey")
        chosen_level = pd.Series(0, index=target.index)
        stats = {c: pd.Series(np.nan, index=target.index) for c in ["ObservationCount", "Median", "MAD", "P10", "P90"]}
        chosen_key = pd.Series(None, index=target.index, dtype=object)
        # Supported levels first (most specific), then the largest sparse group as a flagged fallback.
        for level in LEVELS:
            k = keys[level]
            cnt = k.map(b["ObservationCount"])
            take = (chosen_level == 0) & (cnt >= min_count)
            for c in stats:
                stats[c][take] = k[take].map(b[c])
            chosen_key[take] = k[take]
            chosen_level[take] = level
        for level in sorted(LEVELS, reverse=True):
            k = keys[level]
            cnt = k.map(b["ObservationCount"])
            take = (chosen_level == 0) & cnt.notna()
            for c in stats:
                stats[c][take] = k[take].map(b[c])
            chosen_key[take] = k[take]
            chosen_level[take] = -level  # negative = sparse fallback
        spread = MAD_SCALE * stats["MAD"]
        alt = (stats["P90"] - stats["P10"]) / DECILE_SCALE
        spread = spread.where(spread > 0, alt)
        valid = spread > 0
        value = pd.to_numeric(target[metric], errors="coerce")
        out[f"{metric}RobustZ"] = ((value - stats["Median"]) / spread).where(valid)
        out[f"{metric}PeerMedian"] = stats["Median"]
        out[f"{metric}PeerSpread"] = spread.where(valid)
        zero_spread |= stats["Median"].notna() & ~valid & value.notna()
        if metric == "BuyNett":
            out["PeerCount"] = stats["ObservationCount"].fillna(0).astype(int)
            out["PeerFallbackLevel"] = chosen_level.abs().astype(int)
            out["SparsePeerFlag"] = (chosen_level <= 0).astype(int)
            out["PeerGroupKey"] = chosen_key
    out["ZeroSpreadFlag"] = zero_spread.astype(int)
    out["BaselineCutoff"] = baselines["BaselineCutoff"].iloc[0]
    out["PeerWindowStart"] = baselines["WindowStart"].iloc[0]
    out["PeerWindowEnd"] = baselines["WindowEnd"].iloc[0]
    return out


def combination_counts(history: pd.DataFrame, cutoff_week: pd.Timestamp, lookback_weeks: int) -> pd.Series:
    """Historical count of (customer, carrier, work code, lane) combinations before the cutoff."""
    cutoff_week = pd.Timestamp(cutoff_week).normalize()
    weeks = pd.to_datetime(history["SnapshotWeek"])
    window = _prepare(history[(weeks >= cutoff_week - pd.Timedelta(weeks=lookback_weeks)) & (weeks < cutoff_week)])
    return combo_key(window).value_counts()


def combo_key(df: pd.DataFrame) -> pd.Series:
    d = _prepare(df) if "Lane" not in df else df
    return d["Customer"].fillna("~").astype(str) + "|" + d["Carrier"].fillna("~").astype(str) + "|" + d["WorkCode"].fillna("~").astype(str) + "|" + d["Lane"]
