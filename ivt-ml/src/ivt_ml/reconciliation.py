"""Builds the canonical one-row-per-job snapshot from buy and sell charge lines.

Rules (docs/data_contract.md):
* Each side is aggregated independently to (week, customer, carrier, job) BEFORE joining, so multiple buy and
  sell lines never multiply each other.
* Charges are summed across lines; consignment measurements are NOT summed (they repeat on every charge line):
  the maximum is taken and MeasurementConflict is set when lines disagree.
* Buy/sell matching on (week, job): EXACT (one buy group, same customer+carrier), MAPPED (one buy group,
  different carrier/customer code), AMBIGUOUS (several buy groups -> quarantined: buy amounts withheld, no labels),
  UNMATCHED (no counterpart).
* Totals are reconciled before/after consolidation; any difference is a BLOCKER data-quality issue.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional

import numpy as np
import pandas as pd

from .contracts import KEY_VERSION, SNAPSHOT_COLUMNS, parse_series_date, parse_series_decimal, segment_for
from .keys import normalize_identifier, row_hash, snapshot_key, strip_connote_suffix, transaction_key

AMOUNTS = ["Freight", "Other", "Fuel", "Nett"]
MEASURES = ["Weight", "ChargeableWeight", "Cubic", "Pallet", "Quantity", "Items", "Hours", "Lift", "Space"]
GROUP = ["SnapshotWeek", "Customer", "Carrier", "JobNumber"]


@dataclass
class ReconciliationResult:
    snapshots: pd.DataFrame
    source_rows: pd.DataFrame
    quarantine: pd.DataFrame
    issues: List[dict] = field(default_factory=list)
    totals: Dict[str, float] = field(default_factory=dict)


def _prepare(lines: pd.DataFrame, side: str, issues: List[dict]) -> pd.DataFrame:
    df = lines.copy()
    df["Side"] = side
    for col in ["Customer", "Carrier"]:
        df[col] = df[col].map(normalize_identifier)
    df["JobNumber"] = df["JobNumberRaw"].map(normalize_identifier)
    df["ConsignmentNote"] = df["ConsignmentNoteRaw"].map(normalize_identifier)
    missing_job = df["JobNumber"].isna()
    if missing_job.any():
        issues.append(dict(IssueCode="MISSING_JOB_NUMBER", Severity="WARN", SourceTable=side, AffectedRows=int(missing_job.sum()),
                           Detail="Lines without a job number cannot be keyed and are excluded."))
        df = df[~missing_job]
    for col in AMOUNTS + MEASURES:
        if col not in df:
            df[col] = np.nan
        if df[col].dtype == object:
            parsed, bad = parse_series_decimal(df[col])
            if bad.any():
                issues.append(dict(IssueCode=f"UNPARSEABLE_{col.upper()}", Severity="WARN", SourceTable=side, AffectedRows=int(bad.sum()),
                                   Detail=f"{side} {col} values that are not numbers were treated as missing."))
            df[col] = parsed
        else:
            df[col] = pd.to_numeric(df[col], errors="coerce")
    df["ConsDate"], df["InvalidDate"] = parse_series_date(df["ConsDateRaw"]) if "ConsDateRaw" in df else (pd.NaT, False)
    df["SnapshotWeek"] = pd.to_datetime(df["SnapshotWeek"]).dt.normalize()
    raw_cols = [c for c in lines.columns if c not in ("Side",)]
    df["SourceRowHash"] = [row_hash(r) for r in lines.loc[df.index, raw_cols].to_dict("records")]
    dup = df.duplicated(subset=["SourceRowHash"], keep="first")
    if dup.any():
        issues.append(dict(IssueCode="REPEATED_SOURCE_ROW", Severity="INFO", SourceTable=side, AffectedRows=int(dup.sum()),
                           Detail="Byte-identical source rows (repeated snapshot) were counted once."))
        df = df[~dup]
    return df


def _aggregate(df: pd.DataFrame, prefix: str) -> pd.DataFrame:
    if df.empty:
        return pd.DataFrame(columns=GROUP)
    g = df.groupby(GROUP, dropna=False, sort=False)
    out = g[AMOUNTS].sum(min_count=1).add_prefix(prefix)
    out[f"{prefix}LineCount"] = g.size()
    out[f"{prefix}AncillaryChargeCount"] = g["Other"].apply(lambda s: int((s.fillna(0) != 0).sum()))
    meas = g[MEASURES].max()
    distinct = g[MEASURES].nunique(dropna=True)
    out[f"{prefix}MeasurementConflict"] = (distinct > 1).any(axis=1)
    for m in MEASURES:
        out[f"{prefix}{m}"] = meas[m]
    # Descriptive attributes come from the line with the largest freight (the primary charge line).
    primary = df.sort_values("Freight", ascending=False, na_position="last").drop_duplicates(GROUP)
    attrs = primary.set_index(GROUP)[["ConsignmentNote", "ConsignmentNoteRaw", "JobNumberRaw", "SupplierRef", "WorkCode", "OptionCode",
                                      "FromLocation", "ToLocation", "PickupPostcode", "DeliveryPostcode", "ConsDate"]].add_prefix(prefix)
    invalid = g["InvalidDate"].any().rename(f"{prefix}InvalidDate")
    return out.join(attrs).join(invalid).reset_index()


def reconcile(sell_lines: pd.DataFrame, buy_lines: pd.DataFrame, *, source_system: str,
              customer_groups: Optional[Dict[str, str]] = None, carrier_groups: Optional[Dict[str, str]] = None,
              routing: Optional[pd.DataFrame] = None, currency_map: Optional[Dict[str, str]] = None,
              moveit: Optional[pd.DataFrame] = None, is_synthetic: bool = False) -> ReconciliationResult:
    issues: List[dict] = []
    sell = _prepare(sell_lines, "SELL", issues)
    buy = _prepare(buy_lines, "BUY", issues)
    totals = {"sell_lines_nett": float(sell["Nett"].sum()), "buy_lines_nett": float(buy["Nett"].sum()),
              "sell_lines": int(len(sell)), "buy_lines": int(len(buy))}

    s = _aggregate(sell, "Sell")
    b = _aggregate(buy, "Buy")

    # Match buy groups to sell groups on (week, job); count candidate buy groups per job.
    b_counts = b.groupby(["SnapshotWeek", "JobNumber"]).size().rename("BuyGroups").reset_index()
    s_counts = s.groupby(["SnapshotWeek", "JobNumber"]).size().rename("SellGroups").reset_index()
    s = s.merge(b_counts, on=["SnapshotWeek", "JobNumber"], how="left").merge(s_counts, on=["SnapshotWeek", "JobNumber"], how="left")
    s["BuyGroups"] = s["BuyGroups"].fillna(0).astype(int)

    exact = s.merge(b, on=GROUP, how="left", suffixes=("", "_b"), indicator=True)
    exact["BuySellMatchStatus"] = np.where(exact["BuyGroups"] == 0, "UNMATCHED",
                                  np.where((exact["BuyGroups"] > 1) | (exact["SellGroups"] > 1), "AMBIGUOUS",
                                  np.where(exact["_merge"] == "both", "EXACT", "MAPPED")))
    exact = exact.drop(columns="_merge")

    # MAPPED: the single buy group for this job carries a different customer/carrier code.
    mapped_mask = exact["BuySellMatchStatus"] == "MAPPED"
    if mapped_mask.any():
        single_b = b.merge(b_counts[b_counts["BuyGroups"] == 1], on=["SnapshotWeek", "JobNumber"]).drop(columns=["BuyGroups", "Customer", "Carrier"])
        fill = exact.loc[mapped_mask, ["SnapshotWeek", "JobNumber"]].merge(single_b, on=["SnapshotWeek", "JobNumber"], how="left")
        for col in fill.columns:
            if col in exact.columns and col not in ("SnapshotWeek", "JobNumber"):
                exact.loc[mapped_mask, col] = fill[col].values

    # AMBIGUOUS: quarantine financial join -> buy side withheld.
    amb = exact["BuySellMatchStatus"] == "AMBIGUOUS"
    buy_cols = [c for c in exact.columns if c.startswith("Buy") and c not in ("BuyGroups", "BuySellMatchStatus")]
    quarantine = exact.loc[amb, GROUP + ["SellNett"]].copy()
    if amb.any():
        issues.append(dict(IssueCode="AMBIGUOUS_BUY_SELL_JOIN", Severity="WARN", SourceTable="BUY", AffectedRows=int(amb.sum()),
                           Detail="Several buy (or sell) groups share a job number in the week; buy amounts withheld and no labels assigned."))
        exact[buy_cols] = exact[buy_cols].astype(object)
        exact.loc[amb, buy_cols] = None
        exact.loc[amb, "BuyLineCount"] = 0

    # Buy-only jobs (no sell side): kept as entities with a missing sell side.
    matched_jobs = s[["SnapshotWeek", "JobNumber"]].drop_duplicates()
    buy_only = b.merge(matched_jobs, on=["SnapshotWeek", "JobNumber"], how="left", indicator=True)
    buy_only = buy_only[buy_only["_merge"] == "left_only"].drop(columns="_merge")
    buy_only["BuySellMatchStatus"] = "UNMATCHED"
    for col in s.columns:
        if col.startswith("Sell") and col not in buy_only:
            buy_only[col] = np.nan
    buy_only["SellLineCount"] = 0
    snap = pd.concat([exact, buy_only], ignore_index=True, sort=False)

    snap["SellLineCount"] = snap["SellLineCount"].fillna(0).astype(int)
    snap["BuyLineCount"] = snap["BuyLineCount"].fillna(0).astype(int)
    for attr in ["ConsignmentNote", "ConsignmentNoteRaw", "JobNumberRaw", "SupplierRef", "WorkCode", "OptionCode", "FromLocation",
                 "ToLocation", "PickupPostcode", "DeliveryPostcode", "ConsDate"]:
        snap[attr] = snap.get(f"Sell{attr}").combine_first(snap.get(f"Buy{attr}")) if f"Sell{attr}" in snap else snap.get(f"Buy{attr}")
    for m in MEASURES:
        snap[m] = snap.get(f"Sell{m}")
    snap["BuyWeight"], snap["BuyChargeableWeight"], snap["BuyCubic"] = snap.get("BuyWeight"), snap.get("BuyChargeableWeight"), snap.get("BuyCubic")
    snap["MeasurementConflict"] = _flag(snap, "SellMeasurementConflict") | _flag(snap, "BuyMeasurementConflict")
    snap["AncillaryChargeCount"] = snap.get("SellAncillaryChargeCount")
    snap["InvalidDateFlag"] = _flag(snap, "SellInvalidDate") | _flag(snap, "BuyInvalidDate")
    numeric_cols = [f"{side}{c}" for side in ("Sell", "Buy") for c in AMOUNTS + MEASURES] + MEASURES
    for col in numeric_cols:
        if col in snap:
            snap[col] = pd.to_numeric(snap[col], errors="coerce")
    snap["ConsignmentDate"] = pd.to_datetime(snap["ConsDate"], errors="coerce")

    # Reference enrichment (current mappings; documented as not point-in-time).
    cg = {normalize_identifier(k): v for k, v in (customer_groups or {}).items()}
    kg = {normalize_identifier(k): v for k, v in (carrier_groups or {}).items()}
    snap["CustomerGroup"] = snap["Customer"].map(cg)
    snap["FinanceGroup"] = snap["CustomerGroup"]
    snap["CarrierGroup"] = snap["Carrier"].map(kg)
    if routing is not None and not routing.empty:
        r = routing.copy()
        r["Customer"] = r["Customer"].map(normalize_identifier)
        r = r.drop_duplicates("Customer")
        snap = snap.merge(r[["Customer", "AccountManager", "LoginName"]], on="Customer", how="left")
    else:
        snap["AccountManager"], snap["LoginName"] = None, None

    cm = {normalize_identifier(k): v for k, v in (currency_map or {}).items()}
    snap["CurrencyCode"] = [cm.get(c) or cm.get(g) for c, g in zip(snap["Carrier"], snap["CarrierGroup"].map(normalize_identifier))]
    snap["CurrencyStatus"] = np.where(snap["CurrencyCode"].notna(), "MAPPED", "UNKNOWN")
    snap["Segment"] = [segment_for(sv, bv) for sv, bv in zip(snap["SellNett"], snap["BuyNett"])]

    snap = _attach_moveit(snap, moveit)
    snap["SourceSystem"] = source_system
    snap["KeyVersion"] = KEY_VERSION
    snap["IsSynthetic"] = bool(is_synthetic)
    snap["TransactionKey"] = [transaction_key(c, k, j) for c, k, j in zip(snap["Customer"], snap["Carrier"], snap["JobNumber"])]
    snap["SnapshotKey"] = [snapshot_key(t, w, source_system) for t, w in zip(snap["TransactionKey"], snap["SnapshotWeek"])]
    dup_keys = snap["SnapshotKey"].duplicated(keep=False)
    if dup_keys.any():
        issues.append(dict(IssueCode="DUPLICATE_SNAPSHOT_KEY", Severity="BLOCKER", AffectedRows=int(dup_keys.sum()),
                           Detail="Snapshot keys are not unique; key strategy must be revised before use."))

    # Reconciliation of totals: lines -> snapshot (+ quarantined buy amounts).
    snap_sell = float(snap["SellNett"].sum())
    snap_buy = float(snap["BuyNett"].sum())
    quarantined_buy = float(buy.merge(quarantine[["SnapshotWeek", "JobNumber"]].drop_duplicates(), on=["SnapshotWeek", "JobNumber"])["Nett"].sum())
    totals.update(snapshot_sell_nett=snap_sell, snapshot_buy_nett=snap_buy, quarantined_buy_nett=quarantined_buy, snapshots=int(len(snap)))
    if not np.isclose(snap_sell, totals["sell_lines_nett"], atol=0.01) or not np.isclose(snap_buy + quarantined_buy, totals["buy_lines_nett"], atol=0.01):
        issues.append(dict(IssueCode="RECONCILIATION_TOTAL_MISMATCH", Severity="BLOCKER", AffectedRows=None,
                           Detail=f"Sell lines {totals['sell_lines_nett']:.2f} vs snapshot {snap_sell:.2f}; buy lines {totals['buy_lines_nett']:.2f} vs snapshot+quarantine {snap_buy + quarantined_buy:.2f}."))

    source_rows = _source_rows(sell, buy, snap)
    for col in SNAPSHOT_COLUMNS:
        if col not in snap:
            snap[col] = np.nan
    return ReconciliationResult(snapshots=snap[SNAPSHOT_COLUMNS].reset_index(drop=True), source_rows=source_rows,
                                quarantine=quarantine, issues=issues, totals=totals)


def _flag(df: pd.DataFrame, col: str) -> pd.Series:
    if col not in df:
        return pd.Series(False, index=df.index)
    return df[col].map(lambda v: bool(v) if v is not None and not (isinstance(v, float) and np.isnan(v)) else False).astype(bool)


def _attach_moveit(snap: pd.DataFrame, moveit: Optional[pd.DataFrame]) -> pd.DataFrame:
    cols = ["MoveitWeightKg", "MoveitCubic", "MoveitPickupPostcode", "MoveitDeliveryPostcode", "MoveitDespatchDate"]
    if moveit is None or moveit.empty:
        for c in cols:
            snap[c] = np.nan
        snap["MoveitMatchStatus"] = "NOT_APPLICABLE"
        return snap
    m = moveit.copy()
    m["MatchConnote"] = m["ConsignmentNo"].map(normalize_identifier)
    m = m.dropna(subset=["MatchConnote"])
    counts = m.groupby("MatchConnote").agg(n=("TotWeightKg", "size"), distinct_w=("TotWeightKg", "nunique")).reset_index()
    first = m.drop_duplicates("MatchConnote").rename(columns={"TotWeightKg": "MoveitWeightKg", "TotVolM": "MoveitCubic",
                                                              "PickupPostcode": "MoveitPickupPostcode", "DeliveryPostcode": "MoveitDeliveryPostcode",
                                                              "ActualDespatchDate": "MoveitDespatchDate"})
    first = first.merge(counts, on="MatchConnote")[["MatchConnote"] + cols + ["n", "distinct_w"]]
    # Same order as IVT's fetcher: suffix-stripped connote first (MAPPED), then the full connote (EXACT).
    snap["_stripped"] = snap["ConsignmentNote"].map(strip_connote_suffix)
    exact = snap[["ConsignmentNote"]].merge(first, left_on="ConsignmentNote", right_on="MatchConnote", how="left")
    mapped = snap[["_stripped"]].merge(first, left_on="_stripped", right_on="MatchConnote", how="left")
    use_mapped = mapped["n"].notna().values
    chosen = exact.copy()
    chosen.loc[use_mapped, cols + ["n", "distinct_w"]] = mapped.loc[use_mapped, cols + ["n", "distinct_w"]].values
    for c in cols:
        snap[c] = chosen[c].values
    status = np.where(chosen["n"].isna(), "UNMATCHED", np.where(chosen["distinct_w"] > 1, "AMBIGUOUS", np.where(use_mapped, "MAPPED", "EXACT")))
    snap["MoveitMatchStatus"] = status
    amb = snap["MoveitMatchStatus"] == "AMBIGUOUS"
    snap.loc[amb, cols] = np.nan  # conflicting dispatch records are not used as evidence
    return snap.drop(columns=["_stripped"])


def _source_rows(sell: pd.DataFrame, buy: pd.DataFrame, snap: pd.DataFrame) -> pd.DataFrame:
    keys = snap[["SnapshotWeek", "Customer", "Carrier", "JobNumber", "SnapshotKey", "BuySellMatchStatus"]]
    frames = []
    for df in (sell, buy):
        if df.empty:
            continue
        joined = df.merge(keys, on=["SnapshotWeek", "Customer", "Carrier", "JobNumber"], how="left")
        if joined["SnapshotKey"].isna().any():  # MAPPED buy groups attach by (week, job)
            alt = df.merge(keys.drop(columns=["Customer", "Carrier"]).drop_duplicates(["SnapshotWeek", "JobNumber"]), on=["SnapshotWeek", "JobNumber"], how="left")
            joined["SnapshotKey"] = joined["SnapshotKey"].fillna(alt["SnapshotKey"])
            joined["BuySellMatchStatus"] = joined["BuySellMatchStatus"].fillna(alt["BuySellMatchStatus"])
        frames.append(pd.DataFrame({"SnapshotKey": joined["SnapshotKey"], "SourceTable": joined["SourceTable"], "SourceRowId": joined["SourceRowId"],
                                    "SourceRowHash": joined["SourceRowHash"], "Side": joined["Side"], "LineAmountNett": joined["Nett"],
                                    "MatchStatus": joined["BuySellMatchStatus"].fillna("UNMATCHED")}))
    return pd.concat(frames, ignore_index=True) if frames else pd.DataFrame()
