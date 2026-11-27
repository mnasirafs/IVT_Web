"""Publication gates, ranking and capacity (docs/architecture.md, "Publication").

Every scored transaction gets exactly one Decision in ML.ScoreAudit. Order of gates:
  1. UNSUPPORTED_SEGMENT  credits/reversals, zero-value or ambiguous buy/sell joins (scored for audit only)
  2. REJECTED_QUALITY     run-level data-quality failure, both sides missing, or unknown currency when required
  3. candidate routes     CONFIRMED_PATTERN: calibrated confirmed probability >= threshold (approved, in-age model)
                          NOVEL_PATTERN:     AE percentile >= threshold with sufficient peers and >= N independent signals
     NOT_CANDIDATE / REJECTED_THRESHOLD / INSUFFICIENT_EVIDENCE when no route qualifies
  4. REJECTED_MATERIALITY transaction value below the materiality amount for its currency/segment
  5. SHADOW_ONLY          qualifies, but the route or publication is disabled / shadow mode on
  6. caps                 per-account-manager cap, then weekly cap, applied only to eligible rows; overflow is
                          DEFERRED_CAPACITY. Ineligible rows are never promoted to fill capacity.
Ranking: CONFIRMED_PATTERN by calibrated probability; NOVEL_PATTERN by AE percentile, then evidence count, then
transaction value. RankingScore is a ranking key, not a probability.
"""
from __future__ import annotations

from typing import Dict, Optional

import numpy as np
import pandas as pd

from .config import EffectiveConfig
from .explanations import detect_signals, explain
from .keys import normalize_identifier, publication_key


def rule_overlap(scored: pd.DataFrame, rules: Optional[pd.DataFrame]) -> pd.DataFrame:
    out = pd.DataFrame(index=scored.index)
    out["AlreadyRuleFlagged"], out["RuleCodes"], out["ExistingRuleResultId"] = False, None, None
    if rules is None or rules.empty:
        return out
    r = rules[~rules.get("IsDeleted", pd.Series(False, index=rules.index)).fillna(False).astype(bool)].copy()
    r["JobNumber"] = r["JobNumberRaw"].map(normalize_identifier)
    agg = r.groupby("JobNumber").agg(RuleCodes=("ErrCode", lambda s: ",".join(sorted(set(map(str, s))))), ExistingRuleResultId=("RuleResultId", "min"))
    joined = scored[["JobNumber"]].join(agg, on="JobNumber")
    out["AlreadyRuleFlagged"] = joined["RuleCodes"].notna()
    out["RuleCodes"] = joined["RuleCodes"]
    out["ExistingRuleResultId"] = joined["ExistingRuleResultId"]
    return out


def decide(scored: pd.DataFrame, cfg: EffectiveConfig, *, run_quality_ok: bool, confirmed_model_ready: bool,
           model_versions: Dict[str, str], rules: Optional[pd.DataFrame] = None, already_active: Optional[set] = None,
           contributions: Optional[list] = None) -> pd.DataFrame:
    """Applies all gates and returns the audit frame with explanations for every candidate."""
    already_active = already_active or set()
    scored = scored.copy()
    for col in ("ConfirmedExceptionProbability", "WeakFlagScore"):
        if col not in scored:
            scored[col] = np.nan
    z_thr = float(cfg.get("RobustZEvidenceThreshold"))
    min_peers = int(cfg.get("MinimumPeerCount"))
    ae_thr = float(cfg.get("AEPercentileThreshold"))
    p_thr = float(cfg.get("SupervisedProbabilityThreshold"))
    min_ev = int(cfg.get("MinimumEvidenceCount"))
    routes = {r.strip() for r in str(cfg.get("AllowedDetectionRoutes")).split(",") if r.strip()}
    publishing = int(cfg.get("PublicationEnabled")) == 1 and int(cfg.get("ShadowMode")) == 0
    novelty_on = int(cfg.get("NoveltyPublicationEnabled")) == 1
    require_currency = int(cfg.get("RequireKnownCurrencyForPublication")) == 1
    versions = {"modelVersion": model_versions.get("ae"), "classifierVersion": model_versions.get("classifier"),
                "featureVersion": cfg.get("FeatureVersion"), "configVersion": cfg.config_version}

    overlap = rule_overlap(scored, rules)
    rows = []
    for pos, (idx, row) in enumerate(scored.iterrows()):
        prob = row.get("ConfirmedExceptionProbability")
        prob = None if prob is None or pd.isna(prob) else float(prob)
        signals = detect_signals(row, z_threshold=z_thr, min_peer_count=min_peers)
        exp = explain(row, signals, probability=prob, ae_threshold=ae_thr,
                      contributions=contributions[pos] if contributions is not None else None, versions=versions)
        decision, reasons, route = None, [], None
        if row["Segment"] != "STANDARD" or row["BuySellMatchStatus"] == "AMBIGUOUS":
            decision, reasons = "UNSUPPORTED_SEGMENT", [f"SEGMENT_{row['Segment']}" if row["Segment"] != "STANDARD" else "AMBIGUOUS_JOIN"]
        elif not run_quality_ok:
            decision, reasons = "REJECTED_QUALITY", ["RUN_DATA_QUALITY_FAILURE"]
        elif row.get("MissingBuyFlag") == 1 and row.get("MissingSellFlag") == 1:
            decision, reasons = "REJECTED_QUALITY", ["NO_AMOUNTS"]
        elif require_currency and row.get("UnknownCurrencyFlag") == 1:
            decision, reasons = "REJECTED_QUALITY", ["UNKNOWN_CURRENCY"]
        if decision is None:
            ae_pct = row.get("AEAnomalyPercentile")
            ae_hit = ae_pct is not None and not pd.isna(ae_pct) and ae_pct >= ae_thr
            peers_ok = (row.get("PeerCount") or 0) >= min_peers and row.get("SparsePeerFlag") == 0
            confirmed_hit = confirmed_model_ready and prob is not None and prob >= p_thr
            if confirmed_hit and exp["PrimaryReasonCode"] is not None and len(signals) >= 1:
                route = "CONFIRMED_PATTERN"
            elif ae_hit and peers_ok and exp["EvidenceCount"] >= min_ev and len(signals) >= 1:
                route = "NOVEL_PATTERN"
            elif confirmed_hit or ae_hit:
                decision = "INSUFFICIENT_EVIDENCE"
                reasons = (["NO_FACTUAL_SIGNAL"] if not signals else []) + (["SPARSE_PEERS"] if ae_hit and not peers_ok else []) + \
                          ([f"EVIDENCE_{exp['EvidenceCount']}_LT_{min_ev}"] if ae_hit and exp["EvidenceCount"] < min_ev else [])
            elif (prob is not None and prob >= 0.5 * p_thr) or (ae_pct is not None and not pd.isna(ae_pct) and ae_pct >= ae_thr - 1.0):
                decision, reasons = "REJECTED_THRESHOLD", ["BELOW_THRESHOLD"]
            else:
                decision, reasons = "NOT_CANDIDATE", []
        if decision is None:
            value = max(abs(row.get("BuyNett") or 0.0), abs(row.get("SellNett") or 0.0))
            materiality = float(cfg.get("MinimumMaterialityAmount", currency=row.get("CurrencyCode") if isinstance(row.get("CurrencyCode"), str) else None,
                                        carrier_group=row.get("CarrierGroup")))
            if value < materiality:
                decision, reasons = "REJECTED_MATERIALITY", [f"VALUE_LT_{materiality:g}"]
            elif not publishing or route not in routes or (route == "NOVEL_PATTERN" and not novelty_on):
                decision, reasons = "SHADOW_ONLY", ["PUBLICATION_DISABLED" if not publishing else f"ROUTE_{route}_DISABLED"]
            else:
                decision = "ELIGIBLE"
        ranking = None
        if route == "CONFIRMED_PATTERN":
            ranking = prob
        elif route == "NOVEL_PATTERN":
            ranking = float(row["AEAnomalyPercentile"]) / 100.0 + 0.001 * exp["EvidenceCount"]
        rows.append({**exp, "Decision": decision, "DecisionReasonCodes": ",".join(reasons) or None, "CandidateRoute": route, "RankingScore": ranking,
                     "AlreadyRuleFlagged": bool(overlap.at[idx, "AlreadyRuleFlagged"]), "RuleCodes": overlap.at[idx, "RuleCodes"],
                     "ExistingRuleResultId": overlap.at[idx, "ExistingRuleResultId"], "TransactionValue": max(abs(row.get("BuyNett") or 0.0), abs(row.get("SellNett") or 0.0))})
    audit = pd.concat([scored.reset_index(drop=True), pd.DataFrame(rows)], axis=1)
    audit["PublicationKey"] = [publication_key(s, versions["modelVersion"] or "", str(versions["featureVersion"]), cfg.config_version) for s in audit["SnapshotKey"]]
    return apply_caps(audit, cfg, already_active)


def apply_caps(audit: pd.DataFrame, cfg: EffectiveConfig, already_active: set) -> pd.DataFrame:
    out = audit.copy()
    eligible = out["Decision"] == "ELIGIBLE"
    # Idempotent rerun: an identical publication already active stays published and consumes capacity.
    dup = eligible & out["PublicationKey"].isin(already_active)
    out.loc[dup, "DecisionReasonCodes"] = "ALREADY_ACTIVE"
    order = out[eligible].sort_values(["RankingScore", "EvidenceCount", "TransactionValue"], ascending=[False, False, False]).index
    per_am_cap = int(cfg.get("PerAccountManagerAlertCap"))
    weekly_cap = int(cfg.get("WeeklyAlertCap"))
    per_am: Dict[str, int] = {}
    published = 0
    for i in order:
        am = str(out.at[i, "AccountManager"] or "~unassigned")
        if per_am.get(am, 0) >= per_am_cap:
            out.at[i, "Decision"], out.at[i, "DecisionReasonCodes"] = "DEFERRED_CAPACITY", "ACCOUNT_MANAGER_CAP"
            continue
        if published >= weekly_cap:
            out.at[i, "Decision"], out.at[i, "DecisionReasonCodes"] = "DEFERRED_CAPACITY", "WEEKLY_CAP"
            continue
        per_am[am] = per_am.get(am, 0) + 1
        published += 1
        out.at[i, "Decision"] = "PUBLISHED"
    return out


EXCEPTION_COLUMNS = ["PublicationKey", "TransactionKey", "SnapshotKey", "CurrentWeek", "ConsignmentNote", "JobNumber", "SupplierRef", "Customer",
                     "CustomerGroup", "Carrier", "CarrierGroup", "FinanceGroup", "AccountManager", "DetectionRoute", "PrimaryReasonCode",
                     "ReasonSummary", "ExplanationText", "RecommendedAction", "ConfirmedExceptionProbability", "AEReconstructionError",
                     "AEAnomalyPercentile", "EvidenceJson", "PeerGroupKey", "PeerCount", "BaselineCutoff", "EvidenceCount", "CurrencyCode",
                     "BuyNett", "SellNett", "MarginAmount", "MarginRatio", "PotentialExposureAmount", "ExposureCalculationMethod",
                     "AlreadyRuleFlagged", "RuleCodes", "ExistingRuleResultId", "RunId", "ModelVersion", "FeatureVersion", "ConfigVersion",
                     "ScoredAt", "PublicationState", "ReviewStatus", "IsActive", "IsSynthetic"]


def exception_rows(audit: pd.DataFrame, *, run_id: str, model_version: str, feature_version: str, config_version: str,
                   scored_at: pd.Timestamp, already_active: set) -> pd.DataFrame:
    pub = audit[(audit["Decision"] == "PUBLISHED") & ~audit["PublicationKey"].isin(already_active)].copy()
    if pub.empty:
        return pd.DataFrame(columns=EXCEPTION_COLUMNS)
    pub["CurrentWeek"] = pd.to_datetime(pub["SnapshotWeek"]).dt.date
    pub["DetectionRoute"] = pub["CandidateRoute"]
    pub["ConfirmedExceptionProbability"] = np.where(pub["DetectionRoute"] == "CONFIRMED_PATTERN", pub["ConfirmedExceptionProbability"], np.nan)
    pub["RunId"], pub["ModelVersion"], pub["FeatureVersion"], pub["ConfigVersion"] = run_id, model_version, feature_version, config_version
    pub["ScoredAt"] = scored_at
    pub["PublicationState"], pub["ReviewStatus"], pub["IsActive"] = "STAGED", "PENDING", False
    # Rule-flagged items are routed to the existing IVT workflow instead of creating a second AM task.
    pub.loc[pub["AlreadyRuleFlagged"], "ReviewStatus"] = "ROUTED_TO_RULE_WORKFLOW"
    return pub[EXCEPTION_COLUMNS].reset_index(drop=True)
