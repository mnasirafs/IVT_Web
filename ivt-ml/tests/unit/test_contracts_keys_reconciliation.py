from datetime import date

import numpy as np
import pandas as pd
import pytest

from ivt_ml.contracts import parse_date, parse_decimal, parse_percent, segment_for
from ivt_ml.keys import normalize_identifier, snapshot_key, strip_connote_suffix, transaction_key
from ivt_ml.reconciliation import reconcile
from ivt_ml.synthetic import _line

W = date(2026, 9, 25)


def lines(*recs):
    return pd.DataFrame(list(recs))


# ---- identifiers -----------------------------------------------------------------------------------------
def test_leading_zeros_and_suffixes_are_preserved():
    assert normalize_identifier("  00123ab ") == "00123AB"
    assert normalize_identifier("0800") == "0800"
    assert strip_connote_suffix("0ABC123FC") == "0ABC123"     # same as dbo.fn_strip_connote_suffix
    assert strip_connote_suffix("00123") is None               # numeric: no suffix stripping
    s = _line("SELL", W, "J1", "C1", "STE", 10, 0, 1, 5, 5, 0.1, connote="000777XA")
    s["PickupPostcode"] = "0800"
    b = _line("BUY", W, "J1", "C1", "STE", 8, 0, 1, 5, 5, 0.1, connote="000777XA")
    snap = reconcile(lines(s), lines(b), source_system="T").snapshots.iloc[0]
    assert snap["ConsignmentNote"] == "000777XA" and snap["ConsignmentNoteRaw"] == "000777XA"
    assert snap["PickupPostcode"] == "0800"


def test_keys_are_versioned_and_stable():
    t1 = transaction_key("cust", "ste", " J1 ")
    assert t1 == transaction_key("CUST", "STE", "J1")
    assert t1 != transaction_key("CUST", "TGE", "J1")
    assert snapshot_key(t1, "2026-09-25", "X") != snapshot_key(t1, "2026-10-02", "X")
    with pytest.raises(ValueError):
        transaction_key("C", "K", None)


# ---- parsing ---------------------------------------------------------------------------------------------
@pytest.mark.parametrize("raw,expected,invalid", [
    ("25-09-2026", date(2026, 9, 25), False), ("9-09-2026", date(2026, 9, 9), False), ("25/09/2026", date(2026, 9, 25), False),
    ("2026-09-25", date(2026, 9, 25), False), ("/  /", None, True), ("  ", None, False), ("31-02-2026", None, True),
    ("09/25/2026", None, True), ("garbage", None, True)])
def test_mixed_dates_and_placeholders(raw, expected, invalid):
    assert parse_date(raw) == (expected, invalid)


def test_percent_and_amount_parsing():
    assert parse_percent("12.50%") == (0.125, False)
    assert parse_percent("1,234.00%") == (12.34, False)
    assert parse_percent("abc%") == (None, True)
    assert parse_decimal("(12.50)") == (-12.5, False)
    assert parse_decimal("1,000.5") == (1000.5, False)
    assert parse_decimal("") == (None, False)
    assert parse_decimal("n/a") == (None, True)


def test_credit_and_zero_segments():
    assert segment_for(-10, -8) == "CREDIT_REVERSAL"
    assert segment_for(10, -1) == "CREDIT_REVERSAL"
    assert segment_for(0, 0) == "ZERO_VALUE"
    assert segment_for(10, None) == "STANDARD"


# ---- consolidation ---------------------------------------------------------------------------------------
def test_buy_sell_aggregation_has_no_cartesian_multiplication_and_measures_not_summed():
    sell = lines(_line("SELL", W, "J1", "C", "STE", 100, 0, 10, 20, 25, 0.2), _line("SELL", W, "J1", "C", "STE", 0, 15, 0, 20, 25, 0.2, work="TAIL"))
    buy = lines(_line("BUY", W, "J1", "C", "STE", 80, 0, 8, 20, 25, 0.2), _line("BUY", W, "J1", "C", "STE", 0, 12, 0, 20, 25, 0.2, work="TAIL"))
    r = reconcile(sell, buy, source_system="T")
    s = r.snapshots.iloc[0]
    assert len(r.snapshots) == 1
    assert s["SellNett"] == pytest.approx(125) and s["BuyNett"] == pytest.approx(100)   # 2x2 lines, not 4 combinations
    assert s["SellLineCount"] == 2 and s["BuyLineCount"] == 2 and s["AncillaryChargeCount"] == 1
    assert s["Weight"] == 20 and s["ChargeableWeight"] == 25                               # repeated measurement, not 40/50
    assert not s["MeasurementConflict"]
    assert r.totals["sell_lines_nett"] == pytest.approx(r.totals["snapshot_sell_nett"])
    assert not [i for i in r.issues if i["Severity"] == "BLOCKER"]


def test_ambiguous_join_is_quarantined():
    sell = lines(_line("SELL", W, "J1", "C", "STE", 100, 0, 10, 20, 25, 0.2))
    buy = lines(_line("BUY", W, "J1", "C", "STE", 80, 0, 8, 20, 25, 0.2), _line("BUY", W, "J1", "C", "TGE", 70, 0, 7, 20, 25, 0.2))
    r = reconcile(sell, buy, source_system="T")
    s = r.snapshots.iloc[0]
    assert s["BuySellMatchStatus"] == "AMBIGUOUS"
    assert pd.isna(s["BuyNett"]) and s["BuyLineCount"] == 0
    assert any(i["IssueCode"] == "AMBIGUOUS_BUY_SELL_JOIN" for i in r.issues)
    assert r.totals["quarantined_buy_nett"] == pytest.approx(165)
    assert not [i for i in r.issues if i["IssueCode"] == "RECONCILIATION_TOTAL_MISMATCH"]


def test_repeated_source_snapshot_counted_once_and_buy_only_kept():
    s = _line("SELL", W, "J1", "C", "STE", 100, 0, 10, 20, 25, 0.2)
    buy = lines(_line("BUY", W, "J1", "C", "STE", 80, 0, 8, 20, 25, 0.2), _line("BUY", W, "J2", "C", "STE", -30, 0, 0, 0, 0, 0))
    r = reconcile(lines(s, dict(s)), buy, source_system="T")
    snaps = r.snapshots.set_index("JobNumber")
    assert snaps.loc["J1", "SellNett"] == pytest.approx(110)
    assert any(i["IssueCode"] == "REPEATED_SOURCE_ROW" for i in r.issues)
    assert snaps.loc["J2", "BuySellMatchStatus"] == "UNMATCHED" and snaps.loc["J2", "Segment"] == "CREDIT_REVERSAL"


def test_currency_is_mapped_or_explicitly_unknown():
    s = _line("SELL", W, "J1", "C", "NZ_NZCOU", 100, 0, 10, 20, 25, 0.2)
    b = _line("BUY", W, "J1", "C", "NZ_NZCOU", 80, 0, 8, 20, 25, 0.2)
    unknown = reconcile(lines(s), lines(b), source_system="T").snapshots.iloc[0]
    mapped = reconcile(lines(s), lines(b), source_system="T", currency_map={"NZ_NZCOU": "NZD"}).snapshots.iloc[0]
    assert unknown["CurrencyStatus"] == "UNKNOWN" and pd.isna(unknown["CurrencyCode"])
    assert mapped["CurrencyStatus"] == "MAPPED" and mapped["CurrencyCode"] == "NZD"
