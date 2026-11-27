"""Read-only extraction from IVT into the canonical line contract (contracts.LINE_COLUMNS).

History and the current week use the SAME adapters and reconciliation code (training/inference parity):
  current week : dbo.Sell_Side / dbo.Buy_Side (+ MoveIT, which is only valid as current evidence)
  history      : dbo.Sell_Side_History / dbo.Buy_Side_History (one query per week; never all history at once)
tmp.CurrentTradingData is read only to reconcile the current week (it is truncated and rebuilt by
WebIVT.IVT_RefreshCurrentTradingData, so it is not used as a historical source).
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from typing import Dict, Optional

import pandas as pd

from .db import Database

_LINE_SELECT = """SELECT '{side}' AS Side, '{table}' AS SourceTable, {row_id} AS SourceRowId, CurrentWeek AS SnapshotWeek,
    customer AS Customer, carrier AS Carrier, jno AS JobNumberRaw, con_note AS ConsignmentNoteRaw, supplier_ref AS SupplierRef,
    work_code AS WorkCode, option_code AS OptionCode, [from] AS FromLocation, [to] AS ToLocation, col_post AS PickupPostcode,
    del_post AS DeliveryPostcode, cons_date AS ConsDateRaw, freight_charges AS Freight, other_charges AS Other, fuel_surch AS Fuel,
    total_nett AS Nett, weight AS Weight, chg_kg AS ChargeableWeight, cubic AS Cubic, pallet AS Pallet, quantity AS Quantity,
    items AS Items, hours AS Hours, lift AS Lift, space AS Space
  FROM {table} WITH (NOLOCK) WHERE CurrentWeek = ?"""

TABLES = {
    ("SELL", "current"): ("dbo.Sell_Side", "CAST(PrimaryKeyField AS nvarchar(30))"),
    ("BUY", "current"): ("dbo.Buy_Side", "CAST(NULL AS nvarchar(30))"),
    ("SELL", "history"): ("dbo.Sell_Side_History", "CAST(PrimaryKeyField AS nvarchar(30))"),
    ("BUY", "history"): ("dbo.Buy_Side_History", "CAST(NULL AS nvarchar(30))"),
}


@dataclass
class ReferenceData:
    customer_groups: Dict[str, str]
    carrier_groups: Dict[str, str]
    routing: pd.DataFrame
    currency_map: Dict[str, str]


class IvtSource:
    is_synthetic = False

    def __init__(self, db: Database):
        self.db = db

    # ---------- weeks ----------
    def current_week(self) -> Optional[date]:
        return self.db.scalar("SELECT MAX(CurrentWeek) FROM dbo.Sell_Side WITH (NOLOCK)")

    def history_weeks(self, start: date, end: date) -> list:
        df = self.db.read_df("SELECT DISTINCT CurrentWeek FROM dbo.Sell_Side_History WITH (NOLOCK) WHERE CurrentWeek BETWEEN ? AND ? ORDER BY CurrentWeek", (start, end))
        return [pd.Timestamp(w).date() for w in df["CurrentWeek"]]

    # ---------- lines ----------
    def lines(self, side: str, week: date, kind: str) -> pd.DataFrame:
        table, row_id = TABLES[(side, kind)]
        df = self.db.read_df(_LINE_SELECT.format(side=side, table=table, row_id=row_id), (week,))
        if side == "BUY":
            df = apply_buy_side_rules(df)
        return df

    # ---------- reference ----------
    def reference(self) -> ReferenceData:
        cust = self.db.read_df("SELECT CustomerSubCode, FinanceGroup FROM AFSXchange.dbo.Customers WITH (NOLOCK) WHERE CustomerSubCode IS NOT NULL")
        carr = self.db.read_df("SELECT CarrierCode, CarrierGroup FROM AFSXchange.dbo.Carriers WITH (NOLOCK) WHERE CarrierCode IS NOT NULL")
        routing = self.db.read_df(
            """SELECT c.CustomerSubCode AS Customer, am.AccountManager, am.LoginName
                 FROM AFSXchange.dbo.Customers c WITH (NOLOCK)
                 JOIN dbo.vAccountManager am ON am.CustomerCode COLLATE DATABASE_DEFAULT = c.CustomerCode COLLATE DATABASE_DEFAULT""")
        currency = {}
        if self.db.scalar("SELECT OBJECT_ID('ML.CurrencyMap')") is not None:
            cm = self.db.read_df("SELECT ScopeKey, CurrencyCode FROM ML.CurrencyMap WHERE ApprovedBy IS NOT NULL")
            currency = dict(zip(cm["ScopeKey"], cm["CurrencyCode"]))
        return ReferenceData(dict(zip(cust["CustomerSubCode"], cust["FinanceGroup"])), dict(zip(carr["CarrierCode"], carr["CarrierGroup"])),
                             routing, currency)

    def moveit_for_current_week(self) -> pd.DataFrame:
        """MoveIT rows for current-week connotes only (full or suffix-stripped, as in IVT's fetcher view)."""
        return self.db.read_df(
            """SELECT m.ConsignmentNo, m.TotWeightKg, m.TotVolM, m.PickupPostcode, m.DeliveryPostcode, m.ActualDespatchDate
                 FROM tmp.Moveit_Consignment m WITH (NOLOCK)
                WHERE m.ConsignmentNo COLLATE DATABASE_DEFAULT IN (
                      SELECT s.con_note COLLATE DATABASE_DEFAULT FROM dbo.Sell_Side s WITH (NOLOCK) WHERE s.con_note IS NOT NULL
                      UNION SELECT dbo.fn_strip_connote_suffix(s.con_note) COLLATE DATABASE_DEFAULT FROM dbo.Sell_Side s WITH (NOLOCK) WHERE s.con_note IS NOT NULL)""")

    def current_trading_totals(self) -> pd.DataFrame:
        return self.db.read_df("SELECT CurrentWeek, COUNT_BIG(*) AS row_count, COUNT(DISTINCT JobNumber) AS jobs, SUM(Sell_Total_Nett) AS sell_nett, SUM(Buy_Total_Nett) AS buy_nett FROM tmp.CurrentTradingData WITH (NOLOCK) GROUP BY CurrentWeek")

    # ---------- labels / rule evidence ----------
    def holds(self, start: date, end: date) -> pd.DataFrame:
        return self.db.read_df(
            """SELECT CAST(Id AS nvarchar(30)) AS SourceRecordId, jno AS JobNumberRaw, Connote, Customer, Carrier, ErrCode, ShortErrDesc,
                      ActionedBy AS Reviewer, ActionTime, InvoiceWeek, IsDeleted
                 FROM dbo.ConsignmentForReview WITH (NOLOCK) WHERE InvoiceWeek BETWEEN ? AND ?""", (start, end))

    def releases(self, start: date, end: date) -> pd.DataFrame:
        return self.db.read_df(
            """SELECT CAST(Id AS nvarchar(30)) AS SourceRecordId, jno AS JobNumberRaw, Connote, Customer, Carrier, ErrCode,
                      Justification, ReleasedBy AS Reviewer, ActionTime, InvoiceWeek
                 FROM dbo.ReleasedConsignment WITH (NOLOCK) WHERE InvoiceWeek BETWEEN ? AND ?""", (start, end))

    def rule_history(self, start: date, end: date) -> pd.DataFrame:
        return self.db.read_df(
            """SELECT CAST(PK_ID AS nvarchar(30)) AS SourceRecordId, jno AS JobNumberRaw, customer AS Customer, carrier AS Carrier,
                      ErrCode, CurrentWeek, LastModifiedTime
                 FROM tmp.IVT_ResultHistory WITH (NOLOCK) WHERE CurrentWeek BETWEEN ? AND ?""", (start, end))

    def current_rule_results(self) -> pd.DataFrame:
        return self.db.read_df(
            """SELECT Id AS RuleResultId, jno AS JobNumberRaw, customer AS Customer, carrier AS Carrier, ErrCode, ShortErrDesc, IsDeleted
                 FROM tmp.CurrentIVTResult WITH (NOLOCK)""")

    def outcome_text_map(self) -> pd.DataFrame:
        if self.db.scalar("SELECT OBJECT_ID('ML.OutcomeTextMap')") is None:
            return pd.DataFrame(columns=["LikePattern", "OutcomeType", "ProposedLabel", "LabelQuality", "Priority"])
        return self.db.read_df("SELECT LikePattern, OutcomeType, ProposedLabel, LabelQuality, Priority FROM ML.OutcomeTextMap WHERE ApprovedBy IS NOT NULL")

    def review_feedback(self) -> pd.DataFrame:
        if self.db.scalar("SELECT OBJECT_ID('ML.ReviewFeedback')") is None:
            return pd.DataFrame(columns=["FeedbackId", "SnapshotKey", "ReviewOutcome", "OutcomeType", "ReviewedBy", "ReviewedAt", "OutcomeVersion", "IsCurrent"])
        return self.db.read_df("SELECT FeedbackId, SnapshotKey, ReviewOutcome, OutcomeType, ReviewedBy, ReviewedAt, OutcomeVersion, IsCurrent FROM ML.ReviewFeedback")


def apply_buy_side_rules(df: pd.DataFrame) -> pd.DataFrame:
    """Source-specific rule replicated from vAllCurrentDetails_Fetcher: NZPOST buy lines carry fuel inside
    total_nett, so buy fuel = total_nett - freight for that carrier. Documented in docs/data_contract.md."""
    if df.empty:
        return df
    out = df.copy()
    nz = out["Carrier"].astype(str).str.strip().str.upper().eq("NZPOST")
    if nz.any():
        nett = pd.to_numeric(out.loc[nz, "Nett"], errors="coerce")
        freight = pd.to_numeric(out.loc[nz, "Freight"], errors="coerce")
        out.loc[nz, "Fuel"] = (nett - freight).round(2)
    return out
