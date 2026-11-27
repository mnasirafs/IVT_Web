import json
from datetime import date

import numpy as np
import pandas as pd
import pytest

from ivt_ml.calibration import select_threshold, temporal_partitions, assign_partitions, wilson_interval
from ivt_ml.config import ConfigError, ControlRow, EffectiveConfig, defaults_as_rows
from ivt_ml.explanations import detect_signals, evidence_count, explain
from ivt_ml.publication import apply_caps, decide, rule_overlap
from ivt_ml.registry import LocalRegistry, RegistryError
from tests.conftest import make_config


# ---- configuration ---------------------------------------------------------------------------------------
def test_config_precedence_specific_to_global():
    rows = defaults_as_rows("v1") + [
        ControlRow("v1", "CARRIER_GROUP", "STE", "MinimumMaterialityAmount", 80, None, date(2026, 1, 1), None),
        ControlRow("v1", "CURRENCY", "NZD", "MinimumMaterialityAmount", 60, None, date(2026, 1, 1), None),
        ControlRow("v1", "CUSTOMER_CARRIER", "CH2|STE", "MinimumMaterialityAmount", 120, None, date(2026, 1, 1), None)]
    cfg = EffectiveConfig("v1", date(2026, 6, 1), rows)
    assert cfg.get("MinimumMaterialityAmount") == 50
    assert cfg.get("MinimumMaterialityAmount", carrier_group="ste") == 80
    assert cfg.get("MinimumMaterialityAmount", currency="NZD") == 60
    assert cfg.get("MinimumMaterialityAmount", customer_carrier="CH2|STE", carrier_group="STE") == 120
    snap = cfg.snapshot()
    assert snap["configVersion"] == "v1" and len(snap["overrides"]) == 3 and len(cfg.checksum()) == 64


def test_config_rejects_overlapping_active_values_and_respects_effective_dates():
    base = defaults_as_rows("v1")
    clash = [ControlRow("v1", "CARRIER_GROUP", "STE", "WeeklyAlertCap", 10, None, date(2026, 1, 1), None),
             ControlRow("v1", "CARRIER_GROUP", "ste", "WeeklyAlertCap", 20, None, date(2026, 3, 1), None)]
    with pytest.raises(ConfigError):
        EffectiveConfig("v1", date(2026, 6, 1), base + clash)
    sequential = [ControlRow("v1", "CARRIER_GROUP", "STE", "WeeklyAlertCap", 10, None, date(2026, 1, 1), date(2026, 3, 1)),
                  ControlRow("v1", "CARRIER_GROUP", "STE", "WeeklyAlertCap", 20, None, date(2026, 3, 1), None)]
    assert EffectiveConfig("v1", date(2026, 2, 1), base + sequential).get("WeeklyAlertCap", carrier_group="STE") == 10
    assert EffectiveConfig("v1", date(2026, 6, 1), base + sequential).get("WeeklyAlertCap", carrier_group="STE") == 20


# ---- evidence and explanations ---------------------------------------------------------------------------
def row(**kw):
    base = dict(CurrencyCode="AUD", PeerCount=200, SparsePeerFlag=0, PeerFallbackLevel=1, PeerGroupKey="L1|x", PeerWindowStart="2026-03-27",
                PeerWindowEnd="2026-09-18", BuyNett=600.0, BuyNettPeerMedian=100.0, BuyNettRobustZ=12.0, BuyPerChargeableKgRobustZ=11.0,
                SellNett=150.0, MarginAmount=-450.0, MarginRatio=-3.0, MarginRatioRobustZ=-9.0, MarginRatioPeerMedian=0.2, NegativeMarginFlag=1,
                BuyFuelRatio=0.18, SellFuelRatio=0.18, FuelRatioDifference=0.0, BuyFuelRatioRobustZ=0.1, Weight=30.0, MoveitWeightKg=10.0,
                BilledVsMoveitWeightRatio=3.0, ChargeableWeight=30.0, MissingBuyFlag=0, MissingSellFlag=0, PreviouslyUnseenCombinationFlag=0,
                AEAnomalyPercentile=99.9, AEReconstructionError=4.2, FinancialReconstructionError=3.0, MeasurementReconstructionError=1.0,
                ReconciliationReconstructionError=0.2, TopReconstructionFeatures='["BuyNett"]', Segment="STANDARD", BuySellMatchStatus="EXACT",
                UnknownCurrencyFlag=0, SnapshotKey="s1", SnapshotWeek="2026-09-25", AccountManager="AM1", CarrierGroup="STE", JobNumber="J1",
                WorkCode="GEN", FromLocation="SYD", ToLocation="MEL")
    base.update(kw)
    return pd.Series(base)


def test_restatements_of_one_amount_count_once():
    r = row(BilledVsMoveitWeightRatio=1.0)
    sig = detect_signals(r, z_threshold=3.5, min_peer_count=50)
    codes = {s["code"] for s in sig}
    assert {"ML_HIGH_BUY_COST", "ML_UNUSUAL_MARGIN"} <= codes
    assert evidence_count(sig, ae_flagged=True, ae_group="FINANCIAL") == 1   # buy, margin and AE(financial) = one family
    r2 = row()
    sig2 = detect_signals(r2, z_threshold=3.5, min_peer_count=50)
    assert evidence_count(sig2, ae_flagged=True, ae_group="FINANCIAL") == 2  # + independent weight mismatch


def test_sparse_peers_give_no_peer_based_signal():
    sig = detect_signals(row(PeerCount=5, SparsePeerFlag=1, NegativeMarginFlag=0, BilledVsMoveitWeightRatio=1.0), z_threshold=3.5, min_peer_count=50)
    assert not sig


def test_explanation_matches_evidence_and_separates_probability_from_percentile():
    r = row()
    sig = detect_signals(r, z_threshold=3.5, min_peer_count=50)
    ae_only = explain(r, sig, probability=None, ae_threshold=99.5)
    ev = json.loads(ae_only["EvidenceJson"])
    assert "$600.00 AUD" in ae_only["ExplanationText"] and "$100.00 AUD" in ae_only["ExplanationText"]
    assert ev["signals"][0]["observed"] == 600.0 and ev["signals"][0]["peerMedian"] == 100.0 and ev["signals"][0]["peerCount"] == 200
    assert "more unusual than 99.9%" in ae_only["ExplanationText"]
    assert "probability" not in ae_only["ExplanationText"].lower()
    assert ev["classifier"]["confirmedExceptionProbability"] is None
    with_prob = explain(r, sig, probability=0.97, ae_threshold=99.5)
    assert "97% probability of a confirmed exception" in with_prob["ExplanationText"]
    assert "more unusual than" not in with_prob["ExplanationText"]
    assert ae_only["PotentialExposureAmount"] == 500.0 and "not a confirmed saving" in ae_only["ExposureCalculationMethod"]
    no_currency = row(CurrencyCode=None)
    unknown = explain(no_currency, detect_signals(no_currency, z_threshold=3.5, min_peer_count=50), probability=None, ae_threshold=99.5)
    assert "currency not confirmed" in unknown["ExplanationText"]


def test_explanation_wording_edge_cases_from_real_data():
    # High cost per kg only: the text must talk about per-kg cost, not claim the total buy is high.
    r = row(BuyNett=166.56, BuyNettPeerMedian=188.84, BuyNettRobustZ=-0.3, BuyPerChargeableKgRobustZ=6.0, BuyPerChargeableKg=41.6,
            BuyPerChargeableKgPeerMedian=4.2, ChargeableWeight=4.0, NegativeMarginFlag=0, MarginRatioRobustZ=0.0, BilledVsMoveitWeightRatio=1.0)
    sig = {s["code"]: s for s in detect_signals(r, z_threshold=3.5, min_peer_count=50)}
    assert sig["ML_HIGH_BUY_COST"]["basis"] == "per_chargeable_kg"
    assert "per chargeable kg" in sig["ML_HIGH_BUY_COST"]["text"] and "Buy nett is" not in sig["ML_HIGH_BUY_COST"]["text"]
    assert explain(r, list(sig.values()), probability=None, ae_threshold=99.5)["ReasonSummary"].startswith("Buy cost per chargeable kg")
    # Billed weight 0 is a data gap, not a weight mismatch.
    zero = row(Weight=0.0, MoveitWeightKg=3.7, BilledVsMoveitWeightRatio=0.0, BuyNettRobustZ=0.0, BuyPerChargeableKgRobustZ=0.0, NegativeMarginFlag=0, MarginRatioRobustZ=0.0)
    assert "ML_WEIGHT_MISMATCH" not in {s["code"] for s in detect_signals(zero, z_threshold=3.5, min_peer_count=50)}
    # Blank work code / lane never prints empty brackets or 'None'.
    combo = row(PreviouslyUnseenCombinationFlag=1, WorkCode=None, FromLocation="ESTC", ToLocation=" ", Customer="CLI3PARC", Carrier="PFMCORP",
                BuyNettRobustZ=0.0, BuyPerChargeableKgRobustZ=0.0, NegativeMarginFlag=0, MarginRatioRobustZ=0.0, BilledVsMoveitWeightRatio=1.0)
    text = {s["code"]: s for s in detect_signals(combo, z_threshold=3.5, min_peer_count=50)}["ML_UNUSUAL_CHARGE_COMBINATION"]["text"]
    assert "(lane ESTC)" in text and "None" not in text and "(," not in text


# ---- gates, caps, overlap --------------------------------------------------------------------------------
def frame(n, **kw):
    return pd.DataFrame([row(**{"SnapshotKey": f"s{i}", "JobNumber": f"J{i}", "AccountManager": f"AM{i % 2}", "AEAnomalyPercentile": 99.6 + i * 0.01, **kw})
                         for i in range(n)])


def test_novelty_disabled_by_default_and_shadow_never_publishes():
    audit = decide(frame(3), make_config(), run_quality_ok=True, confirmed_model_ready=False, model_versions={"ae": "ae1"})
    assert set(audit["Decision"]) == {"SHADOW_ONLY"}
    assert audit["ConfirmedExceptionProbability"].isna().all()


def test_thresholds_caps_and_no_promotion_of_ineligible():
    cfg = make_config({"PublicationEnabled": 1, "ShadowMode": 0, "NoveltyPublicationEnabled": 1, "AllowedDetectionRoutes": "NOVEL_PATTERN",
                       "WeeklyAlertCap": 3, "PerAccountManagerAlertCap": 2})
    f = pd.concat([frame(6), frame(2, AEAnomalyPercentile=50.0).assign(SnapshotKey=["low0", "low1"])], ignore_index=True)
    audit = decide(f, cfg, run_quality_ok=True, confirmed_model_ready=False, model_versions={"ae": "ae1"})
    assert (audit["Decision"] == "PUBLISHED").sum() == 3                   # weekly cap
    assert audit.groupby("AccountManager").apply(lambda g: (g["Decision"] == "PUBLISHED").sum()).max() <= 2
    assert (audit["Decision"] == "DEFERRED_CAPACITY").sum() == 3
    low = audit[audit["SnapshotKey"].str.startswith("low")]
    assert set(low["Decision"]) <= {"NOT_CANDIDATE", "REJECTED_THRESHOLD"}  # never promoted to fill capacity
    # Ranking: within one account manager, published items outrank that manager's deferred items.
    for _, g in audit[audit["Decision"].isin(["PUBLISHED", "DEFERRED_CAPACITY"])].groupby("AccountManager"):
        pub, deferred = g[g["Decision"] == "PUBLISHED"], g[g["Decision"] == "DEFERRED_CAPACITY"]
        if len(pub) and len(deferred) and (deferred["DecisionReasonCodes"] == "ACCOUNT_MANAGER_CAP").all():
            assert pub["RankingScore"].min() >= deferred["RankingScore"].max()


def test_quality_currency_materiality_and_segment_gates():
    cfg = make_config({"PublicationEnabled": 1, "ShadowMode": 0, "NoveltyPublicationEnabled": 1, "AllowedDetectionRoutes": "NOVEL_PATTERN",
                       "MinimumMaterialityAmount": 1000})
    f = pd.concat([frame(1), frame(1, UnknownCurrencyFlag=1, CurrencyCode=None).assign(SnapshotKey="cur"),
                   frame(1, Segment="CREDIT_REVERSAL").assign(SnapshotKey="cr")], ignore_index=True)
    audit = decide(f, cfg, run_quality_ok=True, confirmed_model_ready=False, model_versions={"ae": "ae1"}).set_index("SnapshotKey")
    assert audit.loc["s0", "Decision"] == "REJECTED_MATERIALITY"
    assert audit.loc["cur", "DecisionReasonCodes"] == "UNKNOWN_CURRENCY"
    assert audit.loc["cr", "Decision"] == "UNSUPPORTED_SEGMENT"
    bad = decide(frame(2), cfg, run_quality_ok=False, confirmed_model_ready=False, model_versions={"ae": "ae1"})
    assert set(bad["Decision"]) == {"REJECTED_QUALITY"}


def test_confirmed_route_requires_ready_model():
    cfg = make_config({"PublicationEnabled": 1, "ShadowMode": 0, "AllowedDetectionRoutes": "CONFIRMED_PATTERN", "AEPercentileThreshold": 100.0})
    f = frame(2).assign(ConfirmedExceptionProbability=[0.99, 0.5])
    not_ready = decide(f, cfg, run_quality_ok=True, confirmed_model_ready=False, model_versions={"ae": "ae1"})
    assert "PUBLISHED" not in set(not_ready["Decision"])
    ready = decide(f, cfg, run_quality_ok=True, confirmed_model_ready=True, model_versions={"ae": "ae1"}).set_index("SnapshotKey")
    assert ready.loc["s0", "Decision"] == "PUBLISHED" and ready.loc["s0", "CandidateRoute"] == "CONFIRMED_PATTERN"
    assert ready.loc["s1", "Decision"] != "PUBLISHED"


def test_rule_overlap_and_idempotent_caps():
    f = frame(2)
    rules = pd.DataFrame([{"RuleResultId": 7, "JobNumberRaw": " j0 ", "ErrCode": "3", "IsDeleted": False}, {"RuleResultId": 9, "JobNumberRaw": "J0", "ErrCode": "5", "IsDeleted": False},
                          {"RuleResultId": 11, "JobNumberRaw": "J1", "ErrCode": "8", "IsDeleted": True}])
    o = rule_overlap(f, rules)
    assert o.loc[0, "AlreadyRuleFlagged"] and o.loc[0, "RuleCodes"] == "3,5" and o.loc[0, "ExistingRuleResultId"] == 7
    assert not o.loc[1, "AlreadyRuleFlagged"]                                  # deleted rule result ignored
    cfg = make_config({"WeeklyAlertCap": 1})
    audit = f.assign(Decision="ELIGIBLE", RankingScore=[0.9, 0.8], EvidenceCount=2, TransactionValue=10, PublicationKey=["k0", "k1"])
    capped = apply_caps(audit, cfg, already_active={"k0"})
    assert capped.loc[0, "Decision"] == "PUBLISHED" and capped.loc[0, "DecisionReasonCodes"] == "ALREADY_ACTIVE"
    assert capped.loc[1, "Decision"] == "DEFERRED_CAPACITY"


# ---- validation utilities --------------------------------------------------------------------------------
def test_temporal_partitions_are_ordered_with_gaps_and_entity_grouping():
    weeks = pd.date_range("2026-01-02", periods=20, freq="7D")
    parts = temporal_partitions(weeks, gap_weeks=1)
    order = [parts[p] for p in ("train", "selection", "calibration", "test")]
    for a, b in zip(order, order[1:]):
        assert max(a) < min(b) - pd.Timedelta(days=7)
    df = pd.DataFrame({"SnapshotWeek": [weeks[0], weeks[-1]], "TransactionKey": ["t", "t"]})
    labels = assign_partitions(df, parts)
    assert labels.iloc[0] == "train" and labels.iloc[1] is None                # same entity cannot leak into test


def test_threshold_selection_uses_wilson_lower_bound():
    scores = np.linspace(1, 0, 200)
    y = np.r_[np.ones(60), (np.arange(140) % 4 == 0).astype(int)]
    t = select_threshold(scores, y, min_lower_bound=0.9, min_selected=20)
    assert t is not None and t["precision_ci95"][0] >= 0.9 and t["selected"] >= 20
    assert select_threshold(scores, np.zeros(200, int), min_lower_bound=0.9, min_selected=20) is None
    lo, hi = wilson_interval(95, 100)
    assert 0.88 < lo < 0.95 < hi


# ---- model rollback --------------------------------------------------------------------------------------
def test_model_approval_and_rollback(tmp_path):
    reg = LocalRegistry(tmp_path)
    for v in ("ae-1", "ae-2"):
        art = tmp_path / f"{v}.bin"; art.write_bytes(v.encode())
        from ivt_ml.autoencoder import sha256_file
        reg.register({"ModelVersion": v, "ModelKind": "AUTOENCODER", "ArtifactUri": str(art), "ArtifactSha256": sha256_file(art), "IsSyntheticOnly": False,
                      "FeatureVersion": "f1"})
    reg.approve("ae-1", "lead", evidence_note="backtest 1")
    reg.approve("ae-2", "lead", evidence_note="backtest 2")
    assert reg.current("AUTOENCODER")["ModelVersion"] == "ae-2"
    reg.rollback("AUTOENCODER", "ae-1", "lead")
    assert reg.current("AUTOENCODER")["ModelVersion"] == "ae-1"
    (tmp_path / "ae-2.bin").write_bytes(b"tampered")
    with pytest.raises(RegistryError):
        reg.approve("ae-2", "lead", evidence_note="x")
    with pytest.raises(RegistryError):
        reg.register({"ModelVersion": "w", "ModelKind": "CLASSIFIER_WEAK_EXPERIMENTAL", "ProvidesCalibratedProbability": True,
                      "ArtifactUri": "x", "ArtifactSha256": "y"})
