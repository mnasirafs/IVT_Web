"""Run-level monitoring (docs/operations_runbook.md, "Monitoring").

Distinguishes:
  data drift        feature PSI vs the training reference, unknown categories, null/parse rates
  operational       source volumes, reconciliation totals, join ambiguity, peer coverage
  rate changes      shifts concentrated in one carrier group's buy level (peer medians move week on week)
  label drift       hold/review volumes and outcome mix
  model degradation precision / calibration on MATURE reviewed outcomes only
Severe data-quality failures block publication and retraining; they are recorded, not silently retried.
"""
from __future__ import annotations

from typing import Dict, List, Optional

import numpy as np
import pandas as pd

from .calibration import wilson_interval

SEVERE = {"RECONCILIATION_TOTAL_MISMATCH", "DUPLICATE_SNAPSHOT_KEY"}


def psi(expected: np.ndarray, actual: np.ndarray, bins: int = 10) -> Optional[float]:
    """Population stability index using expected-sample quantile bins."""
    e = pd.Series(expected).dropna().to_numpy()
    a = pd.Series(actual).dropna().to_numpy()
    if len(e) < 50 or len(a) < 50:
        return None
    edges = np.unique(np.quantile(e, np.linspace(0, 1, bins + 1)))
    if len(edges) < 3:
        return None
    edges[0], edges[-1] = -np.inf, np.inf
    pe = np.histogram(e, edges)[0] / len(e)
    pa = np.histogram(a, edges)[0] / len(a)
    pe, pa = np.clip(pe, 1e-4, None), np.clip(pa, 1e-4, None)
    return float(np.sum((pa - pe) * np.log(pa / pe)))


def data_quality_gate(issues: List[Dict], snapshots: pd.DataFrame, tolerance: float) -> Dict:
    """Run-level quality decision. BLOCKER issues or too many bad rows fail the run."""
    blockers = [i for i in issues if i.get("Severity") == "BLOCKER" or i.get("IssueCode") in SEVERE]
    n = max(1, len(snapshots))
    bad_rows = int(((snapshots["BuySellMatchStatus"] == "AMBIGUOUS") | snapshots["InvalidDateFlag"].astype(bool)).sum())
    rate = bad_rows / n
    ok = not blockers and rate <= tolerance
    return {"ok": ok, "blockers": [b["IssueCode"] for b in blockers], "row_issue_rate": rate, "tolerance": tolerance}


def run_summary(*, snapshots: pd.DataFrame, features: pd.DataFrame, audit: pd.DataFrame, issues: List[Dict], totals: Dict,
                reference_features: Optional[pd.DataFrame] = None, ae_threshold: float = 99.5, training_categories: Optional[Dict[str, List[str]]] = None,
                model_age_days: Optional[int] = None, config_age_days: Optional[int] = None) -> Dict:
    m: Dict = {"volumes": {"snapshots": int(len(snapshots)), **{k: v for k, v in totals.items()}},
               "match_status": snapshots["BuySellMatchStatus"].value_counts().to_dict(),
               "segments": snapshots["Segment"].value_counts().to_dict(),
               "moveit_match": snapshots["MoveitMatchStatus"].value_counts().to_dict(),
               "data_quality_issues": [{k: i.get(k) for k in ("IssueCode", "Severity", "AffectedRows")} for i in issues]}
    if not features.empty:
        m["peer_coverage"] = {"sparse_share": float(features["SparsePeerFlag"].mean()), "fallback_levels": features["PeerFallbackLevel"].value_counts().to_dict()}
        m["null_rates"] = {c: float(features[c].isna().mean()) for c in ("BuyNett", "SellNett", "Weight", "ChargeableWeight", "Cubic", "MarginRatio") if c in features}
    if "AEAnomalyPercentile" in audit:
        ae = audit["AEAnomalyPercentile"].dropna()
        m["ae_scores"] = {"p50": float(ae.quantile(0.5)) if len(ae) else None, "p99": float(ae.quantile(0.99)) if len(ae) else None,
                          "share_at_or_above_threshold": float((ae >= ae_threshold).mean()) if len(ae) else None,
                          "expected_share_if_stable": round((100 - ae_threshold) / 100, 4)}
    m["decisions"] = audit["Decision"].value_counts().to_dict() if "Decision" in audit else {}
    if "AlreadyRuleFlagged" in audit:
        pub = audit[audit["Decision"] == "PUBLISHED"]
        m["rule_overlap_share_of_published"] = float(pub["AlreadyRuleFlagged"].mean()) if len(pub) else None
    if reference_features is not None and not reference_features.empty:
        m["feature_psi"] = {c: psi(reference_features[c].to_numpy(dtype=float), features[c].to_numpy(dtype=float))
                            for c in ("BuyNett", "SellNett", "MarginRatio", "BuyFuelRatio", "ChargeableWeight") if c in features and c in reference_features}
    if training_categories:
        m["unknown_category_share"] = {c: float((~features[c].isin(vals)).mean()) for c, vals in training_categories.items() if c in features}
    if "CarrierGroup" in features and "BuyNettPeerMedian" in features:
        shift = (features["BuyNett"] - features["BuyNettPeerMedian"]) / features["BuyNettPeerMedian"].where(features["BuyNettPeerMedian"] > 0)
        by_group = shift.groupby(features["CarrierGroup"]).median().dropna()
        m["possible_rate_changes"] = {k: round(float(v), 3) for k, v in by_group[by_group.abs() >= 0.10].items()}
    m["model_age_days"], m["config_age_days"] = model_age_days, config_age_days
    return m


def mature_review_precision(exceptions: pd.DataFrame, feedback: pd.DataFrame, as_of: pd.Timestamp, maturity_days: int = 28) -> Dict:
    """Published precision on reviewed outcomes that had time to mature. Unreviewed rows are not counted."""
    if exceptions.empty or feedback.empty:
        return {"reviewed": 0, "precision": None, "review_coverage": None}
    pub = exceptions[pd.to_datetime(exceptions["PublishedAt"]) <= pd.Timestamp(as_of) - pd.Timedelta(days=maturity_days)]
    cur = feedback[feedback["IsCurrent"].astype(bool)]
    j = pub.merge(cur[["SnapshotKey", "ReviewOutcome"]], on="SnapshotKey", how="left")
    decided = j[j["ReviewOutcome"].isin(["CONFIRMED_EXCEPTION", "CONFIRMED_VALID"])]
    k = int((decided["ReviewOutcome"] == "CONFIRMED_EXCEPTION").sum())
    return {"mature_published": int(len(pub)), "reviewed": int(len(decided)), "review_coverage": len(decided) / len(pub) if len(pub) else None,
            "precision": k / len(decided) if len(decided) else None, "precision_ci95": wilson_interval(k, len(decided)),
            "by_route": decided.groupby("DetectionRoute")["ReviewOutcome"].apply(lambda s: float((s == "CONFIRMED_EXCEPTION").mean())).to_dict()
            if "DetectionRoute" in decided else {}}
