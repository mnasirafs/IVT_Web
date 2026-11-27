"""Shared feature pipeline (FEATURE_VERSION f1) used identically for historical training and weekly scoring.

Formulas never substitute an epsilon for a zero denominator: an invalid denominator gives a missing value plus
a diagnostic flag. Valid zeros are preserved; missing and zero are distinct. Ratios are always recomputed from
numeric amounts (IVT's formatted Nett_Margin / FSL % strings are never parsed as inputs).
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional

import numpy as np
import pandas as pd

from .baselines import BASELINE_METRICS, attach_peer_features, combo_key, compute_baselines, combination_counts

FEATURE_VERSION = "f1"


@dataclass(frozen=True)
class FeatureSpec:
    name: str
    family: str          # A..I from the specification
    kind: str            # numeric | flag | categorical
    ae_group: Optional[str]  # FINANCIAL | MEASUREMENT | RECONCILIATION | None (not an AE input)
    classifier: bool
    label: str           # business label used in explanations
    moveit: bool = False


FEATURES: List[FeatureSpec] = [
    # A. Financial
    FeatureSpec("BuyNett", "A", "numeric", "FINANCIAL", True, "Buy nett"),
    FeatureSpec("SellNett", "A", "numeric", "FINANCIAL", True, "Sell nett"),
    FeatureSpec("BuyFreight", "A", "numeric", "FINANCIAL", True, "Buy freight"),
    FeatureSpec("SellFreight", "A", "numeric", "FINANCIAL", True, "Sell freight"),
    FeatureSpec("BuyOther", "A", "numeric", "FINANCIAL", True, "Buy other charges"),
    FeatureSpec("SellOther", "A", "numeric", "FINANCIAL", True, "Sell other charges"),
    FeatureSpec("BuyFuel", "A", "numeric", "FINANCIAL", True, "Buy fuel surcharge"),
    FeatureSpec("SellFuel", "A", "numeric", "FINANCIAL", True, "Sell fuel surcharge"),
    FeatureSpec("MarginAmount", "A", "numeric", "FINANCIAL", True, "Margin (sell - buy)"),
    FeatureSpec("MarginRatio", "A", "numeric", "FINANCIAL", True, "Margin as a share of sell"),
    FeatureSpec("NegativeMarginFlag", "A", "flag", "FINANCIAL", True, "Negative margin"),
    FeatureSpec("BuyExceedsSellFlag", "A", "flag", "FINANCIAL", True, "Buy exceeds sell"),
    # B. Charge composition
    FeatureSpec("BuyFuelRatio", "B", "numeric", "FINANCIAL", True, "Buy fuel as a share of buy freight"),
    FeatureSpec("SellFuelRatio", "B", "numeric", "FINANCIAL", True, "Sell fuel as a share of sell freight"),
    FeatureSpec("FuelRatioDifference", "B", "numeric", "FINANCIAL", True, "Buy vs sell fuel ratio difference"),
    FeatureSpec("BuyOtherRatio", "B", "numeric", "FINANCIAL", True, "Buy other charges as a share of buy nett"),
    FeatureSpec("SellOtherRatio", "B", "numeric", "FINANCIAL", True, "Sell other charges as a share of sell nett"),
    FeatureSpec("AncillaryChargeCount", "B", "numeric", "RECONCILIATION", True, "Ancillary charge lines"),
    FeatureSpec("BuyLineCount", "B", "numeric", "RECONCILIATION", True, "Buy charge lines"),
    FeatureSpec("SellLineCount", "B", "numeric", "RECONCILIATION", True, "Sell charge lines"),
    # C. Physical
    FeatureSpec("Weight", "C", "numeric", "MEASUREMENT", True, "Billed weight (kg)"),
    FeatureSpec("ChargeableWeight", "C", "numeric", "MEASUREMENT", True, "Billed chargeable weight (kg)"),
    FeatureSpec("Cubic", "C", "numeric", "MEASUREMENT", True, "Billed cubic"),
    FeatureSpec("Pallet", "C", "numeric", "MEASUREMENT", True, "Pallets"),
    FeatureSpec("Quantity", "C", "numeric", "MEASUREMENT", True, "Quantity"),
    FeatureSpec("Hours", "C", "numeric", "MEASUREMENT", False, "Hours"),
    FeatureSpec("Lift", "C", "numeric", "MEASUREMENT", False, "Lift"),
    FeatureSpec("Space", "C", "numeric", "MEASUREMENT", False, "Space"),
    # D. Unit costs and relationships
    FeatureSpec("BuyPerChargeableKg", "D", "numeric", "MEASUREMENT", True, "Buy per chargeable kg"),
    FeatureSpec("SellPerChargeableKg", "D", "numeric", "MEASUREMENT", True, "Sell per chargeable kg"),
    FeatureSpec("BuyPerCubic", "D", "numeric", "MEASUREMENT", True, "Buy per cubic"),
    FeatureSpec("SellPerCubic", "D", "numeric", "MEASUREMENT", True, "Sell per cubic"),
    FeatureSpec("ChargeableToActualWeightRatio", "D", "numeric", "MEASUREMENT", True, "Chargeable vs actual weight"),
    FeatureSpec("WeightPerCubic", "D", "numeric", "MEASUREMENT", True, "Weight per cubic"),
    FeatureSpec("ChargePerPallet", "D", "numeric", "MEASUREMENT", True, "Sell per pallet"),
    # E. Historical peers
    FeatureSpec("BuyNettRobustZ", "E", "numeric", "FINANCIAL", True, "Buy nett vs comparable shipments"),
    FeatureSpec("SellNettRobustZ", "E", "numeric", "FINANCIAL", True, "Sell nett vs comparable shipments"),
    FeatureSpec("MarginRatioRobustZ", "E", "numeric", "FINANCIAL", True, "Margin vs comparable shipments"),
    FeatureSpec("BuyFuelRatioRobustZ", "E", "numeric", "FINANCIAL", True, "Fuel ratio vs comparable shipments"),
    FeatureSpec("BuyPerChargeableKgRobustZ", "E", "numeric", "MEASUREMENT", True, "Unit cost vs comparable shipments"),
    FeatureSpec("PeerCount", "E", "numeric", None, True, "Comparable shipments"),
    FeatureSpec("PeerFallbackLevel", "E", "numeric", None, True, "Peer group generality"),
    FeatureSpec("SparsePeerFlag", "E", "flag", "RECONCILIATION", True, "Few comparable shipments"),
    # F. Buy/sell reconciliation
    FeatureSpec("MissingBuyFlag", "F", "flag", "RECONCILIATION", True, "No buy charge"),
    FeatureSpec("MissingSellFlag", "F", "flag", "RECONCILIATION", True, "No sell charge"),
    FeatureSpec("BuySellWeightDifference", "F", "numeric", "RECONCILIATION", True, "Buy vs sell weight difference (kg)"),
    FeatureSpec("BuySellChargeableWeightDifference", "F", "numeric", "RECONCILIATION", True, "Buy vs sell chargeable weight difference (kg)"),
    FeatureSpec("BuySellCubicDifference", "F", "numeric", "RECONCILIATION", True, "Buy vs sell cubic difference"),
    FeatureSpec("ChargeLineMismatchFlag", "F", "flag", "RECONCILIATION", True, "Different number of buy and sell lines"),
    FeatureSpec("MeasurementConflictFlag", "F", "flag", "RECONCILIATION", True, "Charge lines disagree on measurements"),
    # G. Dispatch reconciliation (current evidence; model input only when UseMoveitFeaturesForTraining = 1)
    FeatureSpec("BilledVsMoveitWeightRatio", "G", "numeric", "RECONCILIATION", True, "Billed vs dispatch weight", moveit=True),
    FeatureSpec("BilledVsMoveitCubicRatio", "G", "numeric", "RECONCILIATION", True, "Billed vs dispatch cubic", moveit=True),
    FeatureSpec("BillingVsPickupPostcodeMismatch", "G", "flag", "RECONCILIATION", True, "Pickup postcode differs from dispatch", moveit=True),
    FeatureSpec("BillingVsDeliveryPostcodeMismatch", "G", "flag", "RECONCILIATION", True, "Delivery postcode differs from dispatch", moveit=True),
    FeatureSpec("DispatchToBillingDays", "G", "numeric", None, True, "Days from dispatch to billing week", moveit=True),
    # H. Rarity / context
    FeatureSpec("CustomerGroup", "H", "categorical", None, True, "Customer group"),
    FeatureSpec("CarrierGroup", "H", "categorical", None, True, "Carrier group"),
    FeatureSpec("WorkCode", "H", "categorical", None, True, "Work code"),
    FeatureSpec("PreviouslyUnseenCombinationFlag", "H", "flag", "RECONCILIATION", True, "New customer/carrier/service/lane combination"),
    FeatureSpec("HistoricalCombinationCount", "H", "numeric", None, True, "Times this combination was seen"),
    FeatureSpec("ConsignmentAgeDays", "H", "numeric", None, True, "Days from consignment to billing week"),
    # I. Availability
    FeatureSpec("MissingWeightFlag", "I", "flag", "RECONCILIATION", True, "Missing weight"),
    FeatureSpec("MissingCubicFlag", "I", "flag", "RECONCILIATION", True, "Missing cubic"),
    FeatureSpec("MissingMoveitFlag", "I", "flag", None, True, "No dispatch record", moveit=True),
    FeatureSpec("UnknownCurrencyFlag", "I", "flag", None, False, "Currency unknown"),
    FeatureSpec("InvalidDateFlag", "I", "flag", None, True, "Invalid consignment date"),
    FeatureSpec("MarginRatioUndefinedFlag", "I", "flag", "FINANCIAL", True, "Margin ratio undefined (sell <= 0)"),
    FeatureSpec("FuelRatioUndefinedFlag", "I", "flag", None, True, "Fuel ratio undefined (freight <= 0)"),
]
SPEC_BY_NAME: Dict[str, FeatureSpec] = {f.name: f for f in FEATURES}
CATEGORICAL = [f.name for f in FEATURES if f.kind == "categorical"]

# Never used as model inputs (docs/feature_dictionary.md, "Excluded inputs").
EXCLUDED_INPUTS = {"ErrCode", "ErrDesc", "Reason", "Sub_Reason", "HeldBy", "LoginName", "ContactEmail", "AccountManager",
                   "ReviewOutcome", "ActionTime", "LastModifiedTime", "del_com", "Moveit_SpecialIns", "PickupAddress1", "DeliveryAddress1"}


def _div(num: pd.Series, den: pd.Series) -> pd.Series:
    """num / den only where den > 0; otherwise missing (no epsilon)."""
    num = pd.to_numeric(num, errors="coerce")
    den = pd.to_numeric(den, errors="coerce")
    return (num / den).where(den > 0)


def base_metrics(snap: pd.DataFrame) -> pd.DataFrame:
    """Row-level metrics that need no history. Also used to compute peer baselines."""
    s = snap.copy()
    for c in ["SellNett", "BuyNett", "SellFreight", "BuyFreight", "SellFuel", "BuyFuel", "SellOther", "BuyOther", "Weight", "ChargeableWeight",
              "Cubic", "Pallet", "BuyWeight", "BuyChargeableWeight", "BuyCubic", "MoveitWeightKg", "MoveitCubic"]:
        s[c] = pd.to_numeric(s[c], errors="coerce")
    s["MarginAmount"] = s["SellNett"] - s["BuyNett"]
    s["MarginRatio"] = _div(s["MarginAmount"], s["SellNett"])
    s["MarginRatioUndefinedFlag"] = (~(s["SellNett"] > 0)).astype(int)
    s["NegativeMarginFlag"] = (s["MarginAmount"] < 0).astype(int)
    s["BuyExceedsSellFlag"] = (s["BuyNett"] > s["SellNett"]).astype(int)
    s["BuyFuelRatio"] = _div(s["BuyFuel"], s["BuyFreight"])
    s["SellFuelRatio"] = _div(s["SellFuel"], s["SellFreight"])
    s["FuelRatioDifference"] = s["BuyFuelRatio"] - s["SellFuelRatio"]
    s["FuelRatioUndefinedFlag"] = (~(s["BuyFreight"] > 0) | ~(s["SellFreight"] > 0)).astype(int)
    s["BuyOtherRatio"] = _div(s["BuyOther"], s["BuyNett"])
    s["SellOtherRatio"] = _div(s["SellOther"], s["SellNett"])
    s["BuyPerChargeableKg"] = _div(s["BuyNett"], s["ChargeableWeight"])
    s["SellPerChargeableKg"] = _div(s["SellNett"], s["ChargeableWeight"])
    s["BuyPerCubic"] = _div(s["BuyNett"], s["Cubic"])
    s["SellPerCubic"] = _div(s["SellNett"], s["Cubic"])
    s["ChargeableToActualWeightRatio"] = _div(s["ChargeableWeight"], s["Weight"])
    s["WeightPerCubic"] = _div(s["Weight"], s["Cubic"])
    s["ChargePerPallet"] = _div(s["SellNett"], s["Pallet"])
    s["MissingBuyFlag"] = (s["BuyLineCount"].fillna(0) == 0).astype(int)
    s["MissingSellFlag"] = (s["SellLineCount"].fillna(0) == 0).astype(int)
    s["BuySellWeightDifference"] = s["BuyWeight"] - s["Weight"]
    s["BuySellChargeableWeightDifference"] = s["BuyChargeableWeight"] - s["ChargeableWeight"]
    s["BuySellCubicDifference"] = s["BuyCubic"] - s["Cubic"]
    s["ChargeLineMismatchFlag"] = ((s["BuyLineCount"].fillna(0) != s["SellLineCount"].fillna(0)) & (s["BuyLineCount"].fillna(0) > 0) & (s["SellLineCount"].fillna(0) > 0)).astype(int)
    s["MeasurementConflictFlag"] = s["MeasurementConflict"].fillna(False).astype(bool).astype(int)
    s["BilledVsMoveitWeightRatio"] = _div(s["Weight"], s["MoveitWeightKg"])
    s["BilledVsMoveitCubicRatio"] = _div(s["Cubic"], s["MoveitCubic"])
    has_moveit = s["MoveitMatchStatus"].isin(["EXACT", "MAPPED"])
    s["BillingVsPickupPostcodeMismatch"] = (has_moveit & s["MoveitPickupPostcode"].notna() & (s["PickupPostcode"].astype(str).str.strip() != s["MoveitPickupPostcode"].astype(str).str.strip())).astype(int)
    s["BillingVsDeliveryPostcodeMismatch"] = (has_moveit & s["MoveitDeliveryPostcode"].notna() & (s["DeliveryPostcode"].astype(str).str.strip() != s["MoveitDeliveryPostcode"].astype(str).str.strip())).astype(int)
    week = pd.to_datetime(s["SnapshotWeek"])
    s["DispatchToBillingDays"] = (week - pd.to_datetime(s["MoveitDespatchDate"], errors="coerce")).dt.days
    s["ConsignmentAgeDays"] = (week - pd.to_datetime(s["ConsignmentDate"], errors="coerce")).dt.days
    s["MissingWeightFlag"] = s["Weight"].isna().astype(int)
    s["MissingCubicFlag"] = s["Cubic"].isna().astype(int)
    s["MissingMoveitFlag"] = (~has_moveit).astype(int)
    s["UnknownCurrencyFlag"] = (s["CurrencyStatus"] != "MAPPED").astype(int)
    s["InvalidDateFlag"] = s["InvalidDateFlag"].fillna(False).astype(bool).astype(int)
    return s


PEER_COLUMNS = ["SnapshotWeek", "Segment", "CurrencyCode", "Customer", "CustomerGroup", "Carrier", "CarrierGroup", "WorkCode", "FromLocation",
                "ToLocation", "BuySellMatchStatus", *BASELINE_METRICS]


def peer_frame(snapshots: pd.DataFrame) -> pd.DataFrame:
    """Memory-light history for baselines and rarity counts: metrics precomputed, identifiers as categoricals."""
    m = base_metrics(snapshots)[PEER_COLUMNS].copy()
    for c in ("Segment", "CurrencyCode", "Customer", "CustomerGroup", "Carrier", "CarrierGroup", "WorkCode", "FromLocation", "ToLocation", "BuySellMatchStatus"):
        m[c] = m[c].astype("category")
    for c in BASELINE_METRICS:
        m[c] = m[c].astype("float32")
    return m


def build_features(snapshots: pd.DataFrame, history: pd.DataFrame, *, scoring_week: pd.Timestamp, lookback_weeks: int,
                   min_peer_count: int, baselines: Optional[pd.DataFrame] = None) -> tuple:
    """Feature frame for `snapshots` (all in `scoring_week`) using only `history` strictly before that week.

    Returns (features, baselines). The same function serves historical replay and live scoring.
    """
    week = pd.Timestamp(scoring_week).normalize()
    if (pd.to_datetime(snapshots["SnapshotWeek"]).dt.normalize() != week).any():
        raise ValueError("build_features expects snapshots from a single scoring week")
    cur = base_metrics(snapshots)
    if history is None or history.empty:
        hist = cur.iloc[0:0]
    elif set(BASELINE_METRICS) <= set(history.columns):
        hist = history  # slim peer frame with metrics already computed by base_metrics (see peer_frame)
    else:
        hist = base_metrics(history)
    if baselines is None:
        baselines = compute_baselines(hist, week, lookback_weeks)
    peers = attach_peer_features(cur, baselines, min_peer_count)
    feats = pd.concat([cur, peers], axis=1)
    counts = combination_counts(hist, week, lookback_weeks)
    combos = combo_key(feats)
    feats["HistoricalCombinationCount"] = combos.map(counts).fillna(0).astype(int)
    feats["PreviouslyUnseenCombinationFlag"] = (feats["HistoricalCombinationCount"] == 0).astype(int)
    feats["CarrierGroup"] = feats["CarrierGroup"].fillna(feats["Carrier"])
    feats["CustomerGroup"] = feats["CustomerGroup"].fillna(feats["Customer"])
    feats["FeatureVersion"] = FEATURE_VERSION
    return feats, baselines


def model_feature_names(*, use_moveit: bool, purpose: str) -> List[str]:
    """Feature list for 'ae' (numeric only) or 'classifier' (numeric + categorical)."""
    names = []
    for f in FEATURES:
        if f.moveit and not use_moveit:
            continue
        if purpose == "ae" and f.ae_group is not None:
            names.append(f.name)
        elif purpose == "classifier" and f.classifier:
            names.append(f.name)
    return names


def validate_feature_frame(frame: pd.DataFrame, names: List[str]) -> List[str]:
    """Feature-contract check: required columns present and no excluded inputs leaked in."""
    problems = [f"missing feature {n}" for n in names if n not in frame.columns]
    problems += [f"excluded input used as feature: {n}" for n in names if n in EXCLUDED_INPUTS]
    return problems
