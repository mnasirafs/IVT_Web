"""Deterministic, evidence-based explanations for account managers (no LLM).

Signals are grouped into INDEPENDENCE FAMILIES; EvidenceCount counts families, not signals, so restatements of
one amount (high buy nett, low margin, buy > sell all come from the same buy figure) count once:
  CHARGE_LEVEL    ML_HIGH_BUY_COST, ML_UNUSUAL_MARGIN
  FUEL            ML_UNUSUAL_FUEL_RATIO
  MEASUREMENT     ML_WEIGHT_MISMATCH
  RECONCILIATION  ML_BUY_SELL_MISMATCH
  COMBINATION     ML_UNUSUAL_CHARGE_COMBINATION
  AE pattern      counts only if its dominant reconstruction group maps to a family not already present.
Text never claims causes, savings or that reconstructed values are expected prices; probabilities appear only
when a calibrated confirmed-outcome classifier supplied them.
"""
from __future__ import annotations

import json
import math
from typing import Dict, List, Optional

import pandas as pd

PRIORITY = ["ML_HIGH_BUY_COST", "ML_UNUSUAL_MARGIN", "ML_WEIGHT_MISMATCH", "ML_BUY_SELL_MISMATCH", "ML_UNUSUAL_FUEL_RATIO", "ML_UNUSUAL_CHARGE_COMBINATION"]
FAMILY = {"ML_HIGH_BUY_COST": "CHARGE_LEVEL", "ML_UNUSUAL_MARGIN": "CHARGE_LEVEL", "ML_UNUSUAL_FUEL_RATIO": "FUEL",
          "ML_WEIGHT_MISMATCH": "MEASUREMENT", "ML_BUY_SELL_MISMATCH": "RECONCILIATION", "ML_UNUSUAL_CHARGE_COMBINATION": "COMBINATION"}
AE_GROUP_FAMILY = {"FINANCIAL": "CHARGE_LEVEL", "MEASUREMENT": "MEASUREMENT", "RECONCILIATION": "RECONCILIATION"}
ACTIONS = {
    "ML_HIGH_BUY_COST": "Check the carrier invoice against the agreed rate card, the service used and any surcharges.",
    "ML_UNUSUAL_MARGIN": "Compare the sell rate with the customer's agreed rate and the buy charge; confirm whether a rate change applies.",
    "ML_WEIGHT_MISMATCH": "Verify the billed dead, chargeable and cubic weights against the dispatch record and consignment details.",
    "ML_BUY_SELL_MISMATCH": "Confirm that both the buy and the sell charge exist for this job and that neither has been missed or duplicated.",
    "ML_UNUSUAL_FUEL_RATIO": "Check the fuel surcharge percentage applied by the carrier and to the customer for this week.",
    "ML_UNUSUAL_CHARGE_COMBINATION": "Confirm the service, lane and charges are expected for this customer and carrier.",
}


def _num(v) -> Optional[float]:
    try:
        f = float(v)
        return None if math.isnan(f) else f
    except (TypeError, ValueError):
        return None


def _text(v) -> str:
    """Trimmed text, or '' for missing/blank values (so templates never print 'None' or empty brackets)."""
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return ""
    return str(v).strip()


def kg(v) -> str:
    """Weights below 1 kg keep three decimals so a tiny billed weight never reads as '0.0 kg'."""
    f = _num(v)
    if f is None:
        return "n/a kg"
    return f"{f:,.3f} kg" if abs(f) < 1 else f"{f:,.1f} kg"


def money(v, currency: Optional[str]) -> str:
    f = _num(v)
    if f is None:
        return "n/a"
    text = f"{'-' if f < 0 else ''}${abs(f):,.2f}"
    return f"{text} {currency}" if currency else f"{text} (currency not confirmed)"


def pct(v) -> str:
    f = _num(v)
    return "n/a" if f is None else f"{f * 100:.1f}%"


def window_text(row: pd.Series) -> str:
    s, e = row.get("PeerWindowStart"), row.get("PeerWindowEnd")
    if pd.isna(s) or pd.isna(e):
        return "the comparison window"
    return f"{pd.Timestamp(s):%d %b %Y} to {pd.Timestamp(e):%d %b %Y}"


def detect_signals(row: pd.Series, *, z_threshold: float, min_peer_count: int) -> List[Dict]:
    """Factual signals from observed values and point-in-time peer evidence only."""
    sig: List[Dict] = []
    cur = row.get("CurrencyCode") if isinstance(row.get("CurrencyCode"), str) else None
    peers_ok = (_num(row.get("PeerCount")) or 0) >= min_peer_count and not bool(row.get("SparsePeerFlag", 1))
    peer = {"peerCount": int(_num(row.get("PeerCount")) or 0), "peerGroupKey": row.get("PeerGroupKey"), "window": window_text(row),
            "fallbackLevel": int(_num(row.get("PeerFallbackLevel")) or 0)}

    bz, upz = _num(row.get("BuyNettRobustZ")), _num(row.get("BuyPerChargeableKgRobustZ"))
    total_high = bz is not None and bz >= z_threshold
    unit_high = upz is not None and upz >= z_threshold
    if peers_ok and (total_high or unit_high):
        if total_high:  # the total buy amount itself is high
            observed, med = _num(row.get("BuyNett")), _num(row.get("BuyNettPeerMedian"))
            ratio = (observed / med) if med and med > 0 and observed is not None else None
            text = (f"Buy nett is {money(observed, cur)}, compared with a historical median of {money(med, cur)} across "
                    f"{peer['peerCount']} comparable shipments during {peer['window']}" + (f" ({ratio:.1f} times the median)." if ratio else "."))
        else:  # only the cost per chargeable kg is high (e.g. a light consignment billed like a heavy one)
            observed, med = _num(row.get("BuyPerChargeableKg")), _num(row.get("BuyPerChargeableKgPeerMedian"))
            ratio = (observed / med) if med and med > 0 and observed is not None else None
            text = (f"Buy cost is {money(observed, cur)} per chargeable kg ({money(row.get('BuyNett'), cur)} for {kg(row.get('ChargeableWeight'))}), "
                    f"compared with a typical {money(med, cur)} per kg across {peer['peerCount']} comparable shipments during {peer['window']}"
                    + (f" ({ratio:.1f} times the typical rate)." if ratio else "."))
        sig.append({"code": "ML_HIGH_BUY_COST", "basis": "total" if total_high else "per_chargeable_kg", "observed": observed, "peerMedian": med,
                    "robustZ": bz, "unitCostRobustZ": upz, "ratioToMedian": ratio, **peer, "text": text})
    mz = _num(row.get("MarginRatioRobustZ"))
    if _num(row.get("MarginAmount")) is not None and ((peers_ok and mz is not None and mz <= -z_threshold) or row.get("NegativeMarginFlag") == 1):
        med = _num(row.get("MarginRatioPeerMedian"))
        text = f"Margin is {money(row.get('MarginAmount'), cur)} ({pct(row.get('MarginRatio'))} of sell)"
        text += f", compared with a typical {pct(med)} across {peer['peerCount']} comparable shipments." if peers_ok and med is not None else "."
        sig.append({"code": "ML_UNUSUAL_MARGIN", "observed": _num(row.get("MarginAmount")), "marginRatio": _num(row.get("MarginRatio")),
                    "peerMedianRatio": med, "robustZ": mz, "negativeMargin": bool(row.get("NegativeMarginFlag") == 1), **peer, "text": text})
    fz, fd = _num(row.get("BuyFuelRatioRobustZ")), _num(row.get("FuelRatioDifference"))
    if (peers_ok and fz is not None and abs(fz) >= z_threshold) or (fd is not None and abs(fd) >= 0.10):
        sig.append({"code": "ML_UNUSUAL_FUEL_RATIO", "buyFuelRatio": _num(row.get("BuyFuelRatio")), "sellFuelRatio": _num(row.get("SellFuelRatio")),
                    "peerMedianBuyFuelRatio": _num(row.get("BuyFuelRatioPeerMedian")), "robustZ": fz, **peer,
                    "text": f"Buy fuel is {pct(row.get('BuyFuelRatio'))} of buy freight versus {pct(row.get('SellFuelRatio'))} charged to the customer"
                            + (f"; comparable shipments typically show {pct(row.get('BuyFuelRatioPeerMedian'))}." if peers_ok and _num(row.get('BuyFuelRatioPeerMedian')) is not None else ".")})
    wr = _num(row.get("BilledVsMoveitWeightRatio"))
    cw_diff = _num(row.get("BuySellChargeableWeightDifference"))
    cw = _num(row.get("ChargeableWeight"))
    weight_text = None
    billed = _num(row.get("Weight"))
    if billed is None or billed <= 0:
        wr = None  # no billed weight entered: a data gap, not a weight mismatch
    if wr is not None and (wr >= 1.5 or wr <= 0.67):
        weight_text = f"Billed weight is {_num(row.get('Weight')):,.1f} kg versus {_num(row.get('MoveitWeightKg')):,.1f} kg recorded at dispatch ({wr:.2f} times)."
    elif cw_diff is not None and cw is not None and abs(cw_diff) > max(5.0, 0.2 * abs(cw)):
        weight_text = f"The carrier billed chargeable weight {cw + cw_diff:,.1f} kg while the customer was charged for {cw:,.1f} kg."
    if weight_text:
        sig.append({"code": "ML_WEIGHT_MISMATCH", "billedWeight": _num(row.get("Weight")), "dispatchWeight": _num(row.get("MoveitWeightKg")),
                    "billedToDispatchRatio": wr, "buyMinusSellChargeableWeight": cw_diff, "text": weight_text})
    if row.get("MissingBuyFlag") == 1 and _num(row.get("SellNett")) is not None:
        sig.append({"code": "ML_BUY_SELL_MISMATCH", "observed": "no buy charge", "sellNett": _num(row.get("SellNett")),
                    "text": f"The customer was charged {money(row.get('SellNett'), cur)} but no carrier (buy) charge was found for this job in the same week."})
    elif row.get("MissingSellFlag") == 1 and _num(row.get("BuyNett")) is not None and (_num(row.get("BuyNett")) or 0) > 0:
        sig.append({"code": "ML_BUY_SELL_MISMATCH", "observed": "no sell charge", "buyNett": _num(row.get("BuyNett")),
                    "text": f"A carrier charge of {money(row.get('BuyNett'), cur)} was found but nothing was charged to the customer for this job in the same week."})
    if row.get("PreviouslyUnseenCombinationFlag") == 1 and (_num(row.get("PeerCount")) or 0) > 0:
        parts = []
        if _text(row.get("WorkCode")):
            parts.append(f"work code {_text(row.get('WorkCode'))}")
        lane = " to ".join(p for p in (_text(row.get("FromLocation")), _text(row.get("ToLocation"))) if p)
        if lane:
            parts.append(f"lane {lane}")
        detail = f" ({', '.join(parts)})" if parts else ""
        sig.append({"code": "ML_UNUSUAL_CHARGE_COMBINATION", "historicalCombinationCount": 0, **peer,
                    "text": f"This combination of customer {_text(row.get('Customer'))}, carrier {_text(row.get('Carrier'))}{detail} "
                            f"was not seen during {peer['window']}."})
    return sig


def dominant_ae_group(row: pd.Series) -> Optional[str]:
    vals = {g: _num(row.get(c)) for g, c in (("FINANCIAL", "FinancialReconstructionError"), ("MEASUREMENT", "MeasurementReconstructionError"),
                                             ("RECONCILIATION", "ReconciliationReconstructionError"))}
    vals = {g: v for g, v in vals.items() if v is not None}
    return max(vals, key=vals.get) if vals else None


def evidence_count(signals: List[Dict], ae_flagged: bool, ae_group: Optional[str]) -> int:
    families = {FAMILY[s["code"]] for s in signals}
    if ae_flagged and ae_group and AE_GROUP_FAMILY.get(ae_group) not in families:
        families.add(f"AE_{ae_group}")
    return len(families)


def explain(row: pd.Series, signals: List[Dict], *, probability: Optional[float], ae_threshold: float,
            contributions: Optional[List[Dict]] = None, versions: Optional[Dict] = None) -> Dict:
    ordered = sorted(signals, key=lambda s: PRIORITY.index(s["code"]))
    ae_pct = _num(row.get("AEAnomalyPercentile"))
    ae_flag = ae_pct is not None and ae_pct >= ae_threshold
    ae_group = dominant_ae_group(row)
    count = evidence_count(ordered, ae_flag, ae_group)
    primary = ordered[0]["code"] if ordered else ("ML_UNUSUAL_CHARGE_COMBINATION" if ae_flag else None)
    parts = [s["text"] for s in ordered[:3]]
    if probability is not None:
        parts.append(f"The model estimated a {probability * 100:.0f}% probability of a confirmed exception based on the reviewed historical outcomes.")
    elif ae_flag:
        parts.append(f"This transaction is more unusual than {ae_pct:.1f}% of the model's reference transactions.")
    action = ACTIONS.get(primary, "Review the charges for this job.") if primary else None
    summary = _summary(ordered[0], row) if ordered else ("Unusual overall combination of charges and measurements" if ae_flag else None)
    exposure, method = _exposure(row, ordered)
    evidence = {
        "signals": [{k: v for k, v in s.items() if k != "text"} for s in ordered],
        "independentEvidenceFamilies": count,
        "autoencoder": {"percentile": ae_pct, "reconstructionError": _num(row.get("AEReconstructionError")), "dominantGroup": ae_group,
                        "topReconstructionFeatures": json.loads(row["TopReconstructionFeatures"]) if isinstance(row.get("TopReconstructionFeatures"), str) else None,
                        "note": "Percentile of reconstruction error against the frozen reference set; not a probability and not an expected price."},
        "classifier": {"confirmedExceptionProbability": probability, "influences": contributions or [],
                       "note": "Log-odds contributions are model influences, not causes."},
        "dataQuality": {k: int(row.get(k)) for k in ("SparsePeerFlag", "UnknownCurrencyFlag", "InvalidDateFlag", "MissingMoveitFlag", "MeasurementConflictFlag")
                        if row.get(k) is not None and not pd.isna(row.get(k))},
        "versions": versions or {},
    }
    return {"PrimaryReasonCode": primary, "ReasonSummary": summary, "ExplanationText": " ".join(parts), "RecommendedAction": action,
            "EvidenceCount": count, "EvidenceJson": json.dumps(evidence, default=str), "PotentialExposureAmount": exposure,
            "ExposureCalculationMethod": method, "ReasonCodes": ",".join(dict.fromkeys(s["code"] for s in ordered))}


def _summary(sig: Dict, row: pd.Series) -> str:
    code = sig["code"]
    if code == "ML_HIGH_BUY_COST" and sig.get("ratioToMedian"):
        if sig.get("basis") == "per_chargeable_kg":
            return f"Buy cost per chargeable kg {sig['ratioToMedian']:.1f}x the typical rate"
        return f"Buy nett {sig['ratioToMedian']:.1f}x the median of comparable shipments"
    if code == "ML_UNUSUAL_MARGIN":
        return "Negative margin" if sig.get("negativeMargin") else "Margin well below comparable shipments"
    return {"ML_HIGH_BUY_COST": "Buy nett well above comparable shipments", "ML_WEIGHT_MISMATCH": "Billed weight differs from dispatch or carrier weight",
            "ML_BUY_SELL_MISMATCH": f"Buy/sell mismatch: {sig.get('observed')}", "ML_UNUSUAL_FUEL_RATIO": "Unusual fuel surcharge ratio",
            "ML_UNUSUAL_CHARGE_COMBINATION": "New customer/carrier/service/lane combination"}[code]


def _exposure(row: pd.Series, signals: List[Dict]) -> tuple:
    """Indicative exposure only where a factual comparison defines it; never a claimed saving."""
    codes = {s["code"] for s in signals}
    buy, med = _num(row.get("BuyNett")), _num(row.get("BuyNettPeerMedian"))
    if "ML_HIGH_BUY_COST" in codes and buy is not None and med is not None and buy > med:
        return round(buy - med, 2), "Buy nett minus the peer median buy nett (indicative difference, not a confirmed saving)"
    margin = _num(row.get("MarginAmount"))
    if "ML_UNUSUAL_MARGIN" in codes and margin is not None and margin < 0:
        return round(-margin, 2), "Absolute value of the negative margin (indicative, not a confirmed saving)"
    return None, None
