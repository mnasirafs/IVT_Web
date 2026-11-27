"""Canonical data contract: column names, enumerations and safe parsing of IVT source values.

Source quirks handled here (see docs/data_contract.md):
* Amounts and measurements are stored as VARCHAR in buy/sell tables -> parsed with explicit failure flags.
* Dates arrive as mixed strings (dd/mm/yyyy, yyyy-mm-dd, dd-mm-yyyy) and placeholders such as "/  /".
  Ambiguous or invalid values become NaT with InvalidDateFlag, never a guessed date.
* Percentages may be strings like "12.50%" or fractions; normalised to fractions explicitly.
"""
from __future__ import annotations

import re
from datetime import date, datetime
from typing import Optional, Tuple

import numpy as np
import pandas as pd

KEY_VERSION = "k1"
CONTRACT_VERSION = "c1"

MATCH_STATES = ("EXACT", "MAPPED", "AMBIGUOUS", "UNMATCHED", "NOT_APPLICABLE")
SEGMENTS = ("STANDARD", "CREDIT_REVERSAL", "ZERO_VALUE")
OUTCOME_TYPES = ("IncorrectBuy", "IncorrectSell", "IncorrectDimensions", "DuplicateCharge", "MissingCharge",
                 "ValidRateChange", "ValidTransaction", "InsufficientInformation")
POSITIVE_OUTCOMES = {"IncorrectBuy", "IncorrectSell", "IncorrectDimensions", "DuplicateCharge", "MissingCharge"}
NEGATIVE_OUTCOMES = {"ValidRateChange", "ValidTransaction"}
REASON_CODES = ("ML_HIGH_BUY_COST", "ML_UNUSUAL_MARGIN", "ML_WEIGHT_MISMATCH", "ML_UNUSUAL_FUEL_RATIO",
                "ML_BUY_SELL_MISMATCH", "ML_UNUSUAL_CHARGE_COMBINATION")
DECISIONS = ("PUBLISHED", "REJECTED_THRESHOLD", "REJECTED_QUALITY", "UNSUPPORTED_SEGMENT", "INSUFFICIENT_EVIDENCE",
             "REJECTED_MATERIALITY", "DEFERRED_CAPACITY", "SHADOW_ONLY", "NOT_CANDIDATE")

# Canonical line-level input expected by reconciliation (one row per source charge line).
LINE_COLUMNS = ["Side", "SourceTable", "SourceRowId", "SnapshotWeek", "Customer", "Carrier", "JobNumberRaw", "ConsignmentNoteRaw",
                "SupplierRef", "WorkCode", "OptionCode", "FromLocation", "ToLocation", "PickupPostcode", "DeliveryPostcode",
                "ConsDateRaw", "Freight", "Other", "Fuel", "Nett", "Weight", "ChargeableWeight", "Cubic", "Pallet", "Quantity",
                "Items", "Hours", "Lift", "Space"]

# Canonical snapshot (ML.TradingSnapshot) columns produced by reconciliation.
SNAPSHOT_COLUMNS = ["SnapshotKey", "TransactionKey", "KeyVersion", "SnapshotWeek", "SourceSystem", "Customer", "CustomerGroup",
                    "Carrier", "CarrierGroup", "FinanceGroup", "JobNumber", "JobNumberRaw", "ConsignmentNote", "ConsignmentNoteRaw",
                    "SupplierRef", "WorkCode", "OptionCode", "FromLocation", "ToLocation", "PickupPostcode", "DeliveryPostcode",
                    "ConsignmentDate", "CurrencyCode", "CurrencyStatus", "Segment", "SellFreight", "SellOther", "SellFuel", "SellNett",
                    "BuyFreight", "BuyOther", "BuyFuel", "BuyNett", "SellLineCount", "BuyLineCount", "AncillaryChargeCount",
                    "Weight", "ChargeableWeight", "Cubic", "Pallet", "Quantity", "Items", "Hours", "Lift", "Space",
                    "BuyWeight", "BuyChargeableWeight", "BuyCubic", "MeasurementConflict", "MoveitMatchStatus", "MoveitWeightKg",
                    "MoveitCubic", "MoveitPickupPostcode", "MoveitDeliveryPostcode", "MoveitDespatchDate", "BuySellMatchStatus",
                    "InvalidDateFlag", "AccountManager", "LoginName", "IsSynthetic"]

ROUTING_ONLY_COLUMNS = {"AccountManager", "LoginName", "ContactEmail"}  # never model inputs

_PLACEHOLDER = re.compile(r"^[\s/\-.:0]*$")
_DATE_FORMATS = [("%d/%m/%Y", re.compile(r"^\d{1,2}/\d{1,2}/\d{4}")), ("%Y-%m-%d", re.compile(r"^\d{4}-\d{2}-\d{2}")),
                 ("%d-%m-%Y", re.compile(r"^\d{1,2}-\d{1,2}-\d{4}")), ("%d.%m.%Y", re.compile(r"^\d{1,2}\.\d{1,2}\.\d{4}"))]


def parse_date(value: object) -> Tuple[Optional[date], bool]:
    """Returns (date, invalid_flag). Blank -> (None, False); placeholder/unparseable -> (None, True).

    IVT dates are Australian day-first. US month-first is never guessed: a value such as 13/25/2026 is invalid.
    """
    if value is None or (isinstance(value, float) and np.isnan(value)):
        return None, False
    if isinstance(value, (datetime, pd.Timestamp)):
        return value.date(), False
    if isinstance(value, date):
        return value, False
    text = str(value).strip()
    if not text:
        return None, False
    if _PLACEHOLDER.match(text):
        return None, True
    for fmt, pattern in _DATE_FORMATS:
        if pattern.match(text):
            token = text.split(" ")[0].split("T")[0]
            try:
                return datetime.strptime(token, fmt).date(), False
            except ValueError:
                return None, True
    return None, True


def parse_decimal(value: object) -> Tuple[Optional[float], bool]:
    """Returns (number, invalid_flag). Accepts '1,234.50', '(12.00)' and '-12'. Blank -> (None, False)."""
    if value is None:
        return None, False
    if isinstance(value, (int, float, np.integer, np.floating)):
        return (None, False) if pd.isna(value) else (float(value), False)
    if hasattr(value, "is_finite"):  # Decimal
        return float(value), False
    text = str(value).strip().replace(",", "").replace("$", "")
    if not text:
        return None, False
    negative = text.startswith("(") and text.endswith(")")
    if negative:
        text = text[1:-1]
    try:
        number = float(text)
    except ValueError:
        return None, True
    if not np.isfinite(number):
        return None, True
    return (-number if negative else number), False


def parse_percent(value: object) -> Tuple[Optional[float], bool]:
    """Normalises percentages to fractions: '12.50%' -> 0.125. Bare numbers are treated as fractions
    only when explicitly unit-less in the source; callers must recompute ratios from amounts instead
    (this exists to read legacy formatted strings such as Nett_Margin for reconciliation checks)."""
    if value is None:
        return None, False
    text = str(value).strip()
    if not text:
        return None, False
    if text.endswith("%"):
        number, bad = parse_decimal(text[:-1])
        return (None, True) if bad or number is None else (number / 100.0, False)
    return parse_decimal(text)


def parse_series_decimal(series: pd.Series) -> Tuple[pd.Series, pd.Series]:
    parsed = series.map(parse_decimal)
    return parsed.map(lambda x: x[0]).astype("float64"), parsed.map(lambda x: x[1]).astype(bool)


def parse_series_date(series: pd.Series) -> Tuple[pd.Series, pd.Series]:
    parsed = series.map(parse_date)
    return pd.to_datetime(parsed.map(lambda x: x[0]), errors="coerce"), parsed.map(lambda x: x[1]).astype(bool)


def segment_for(sell_nett: Optional[float], buy_nett: Optional[float]) -> str:
    """Credits/reversals (any negative nett) and all-zero transactions are separate segments: they are
    scored against their own peers and never pollute standard baselines."""
    values = [v for v in (sell_nett, buy_nett) if v is not None and not pd.isna(v)]
    if any(v < 0 for v in values):
        return "CREDIT_REVERSAL"
    if values and all(v == 0 for v in values):
        return "ZERO_VALUE"
    return "STANDARD"
