"""SYNTHETIC fixture for integration tests. Nothing here is IVT data; every record is marked synthetic.

Scenarios placed in the final (scoring) week, by job number prefix:
  N- normal          A- confirmed anomaly (buy 6x peers + weight mismatch)    V- valid high-value shipment
  S- sparse peers    M- missing MoveIT                                        D- ambiguous join (duplicate buy groups)
  R- repeated source snapshot (byte-identical sell line twice)                L- multiple legitimate charge lines
  O- rule-overlapping candidate                                               C- credit/reversal
  U- unreviewed transaction (ordinary)
"""
from __future__ import annotations

from datetime import date, timedelta
from typing import Dict

import numpy as np
import pandas as pd

SYNTHETIC_TAG = "SYNTHETIC"


def _line(side: str, week: date, job: str, customer: str, carrier: str, freight: float, other: float, fuel: float, weight: float, chg: float,
          cubic: float, work: str = "GEN", frm: str = "SYD", to: str = "MEL", connote: str = None, row_id: str = None) -> Dict:
    return {"Side": side, "SourceTable": f"{SYNTHETIC_TAG}.{side}", "SourceRowId": row_id, "SnapshotWeek": pd.Timestamp(week),
            "Customer": customer, "Carrier": carrier, "JobNumberRaw": job, "ConsignmentNoteRaw": connote or f"0{job}FC",
            "SupplierRef": f"SR{job}", "WorkCode": work, "OptionCode": "STD", "FromLocation": frm, "ToLocation": to,
            "PickupPostcode": "2000", "DeliveryPostcode": "3000", "ConsDateRaw": (week - timedelta(days=3)).strftime("%d-%m-%Y"),
            "Freight": f"{freight:.2f}", "Other": f"{other:.2f}", "Fuel": f"{fuel:.2f}", "Nett": f"{freight + other + fuel:.2f}",
            "Weight": f"{weight:.1f}", "ChargeableWeight": f"{chg:.1f}", "Cubic": f"{cubic:.3f}", "Pallet": "1", "Quantity": "1",
            "Items": "1", "Hours": "0", "Lift": "0", "Space": "0"}


def build_fixture(weeks: int = 30, jobs_per_week: int = 160, seed: int = 11) -> Dict[str, pd.DataFrame]:
    rng = np.random.default_rng(seed)
    first = date(2026, 1, 2)
    week_list = [first + timedelta(weeks=i) for i in range(weeks)]
    sell, buy, holds, releases, feedback_jobs = [], [], [], [], []
    carriers = [("STE", "STAR TRACK"), ("TGE", "TEAM GLOBAL"), ("NZ_NZCOU", "NZ COURIERS")]
    customers = ["CUSTA", "CUSTB", "CUSTC"]
    job_no = 100000

    def normal_job(week: date, prefix: str = "N", customer=None, carrier=None) -> str:
        nonlocal job_no
        job_no += 1
        job = f"{prefix}{job_no:07d}"
        cust = customer or customers[rng.integers(len(customers))]
        car = carrier or carriers[rng.integers(len(carriers))][0]
        weight = float(rng.uniform(5, 40))
        chg = weight * float(rng.uniform(1.0, 1.3))
        rate = 2.0 if car != "NZ_NZCOU" else 2.6
        buy_freight = chg * rate * float(rng.normal(1, 0.05))
        sell_freight = buy_freight * float(rng.uniform(1.15, 1.30))
        fuel = 0.18
        sell.append(_line("SELL", week, job, cust, car, sell_freight, 0, sell_freight * fuel, weight, chg, weight / 250, row_id=f"S{job}"))
        buy.append(_line("BUY", week, job, cust, car, buy_freight, 0, buy_freight * fuel, weight, chg, weight / 250))
        return job

    for w in week_list:
        for _ in range(jobs_per_week):
            normal_job(w)
    # Weak hold history: a few historical holds/releases (synthetic) for the experimental classifier.
    for i, w in enumerate(week_list[:-1]):
        for _ in range(3):
            job = normal_job(w, "H")
            # make held jobs genuinely odd: high buy
            buy[-1].update(Freight=f"{float(buy[-1]['Freight']) * 3:.2f}", Nett=f"{float(buy[-1]['Nett']) * 3:.2f}")
            holds.append({"SourceRecordId": f"H{job}", "JobNumberRaw": job, "Connote": f"0{job}FC", "Customer": sell[-1]["Customer"], "Carrier": sell[-1]["Carrier"],
                          "ErrCode": "9999", "ShortErrDesc": "Custom Rule", "Reviewer": "synthetic.am", "ActionTime": pd.Timestamp(w) + pd.Timedelta(days=3),
                          "InvoiceWeek": pd.Timestamp(w), "IsDeleted": 1})
            if i % 2 == 0:
                releases.append({"SourceRecordId": f"R{job}", "JobNumberRaw": job, "Connote": f"0{job}FC", "Customer": sell[-1]["Customer"], "Carrier": sell[-1]["Carrier"],
                                 "ErrCode": "9999", "Justification": "Release - credit received from carrier", "Reviewer": "synthetic.am",
                                 "ActionTime": pd.Timestamp(w) + pd.Timedelta(days=10), "InvoiceWeek": pd.Timestamp(w)})
            feedback_jobs.append((job, w))

    final = week_list[-1]
    scenario: Dict[str, str] = {}
    # Confirmed anomaly: buy 6x and billed weight far above dispatch.
    j = normal_job(final, "A", "CUSTA", "STE"); scenario["confirmed_anomaly"] = j
    buy[-1].update(Freight=f"{float(buy[-1]['Freight']) * 6:.2f}", Nett=f"{float(buy[-1]['Nett']) * 6:.2f}")
    # Valid high-value: large but proportionate shipment.
    j = normal_job(final, "V", "CUSTB", "TGE"); scenario["valid_high_value"] = j
    for rec in (sell[-1], buy[-1]):
        for c in ("Freight", "Fuel", "Nett", "Weight", "ChargeableWeight"):
            rec[c] = f"{float(rec[c]) * 8:.2f}"
    # Sparse peers: unseen customer/carrier/lane.
    j = normal_job(final, "S", "NEWCUST", "XFM"); scenario["sparse_peer"] = j
    for rec in (sell[-1], buy[-1]):
        rec.update(FromLocation="PER", ToLocation="DRW")
    j = normal_job(final, "M"); scenario["missing_moveit"] = j
    # Ambiguous join: two buy groups for the same job (different carrier codes).
    j = normal_job(final, "D", "CUSTA", "STE"); scenario["ambiguous_join"] = j
    extra = dict(buy[-1]); extra.update(Carrier="TGE"); buy.append(extra)
    # Repeated source snapshot: identical sell row twice.
    j = normal_job(final, "R"); scenario["repeated_snapshot"] = j
    sell.append(dict(sell[-1]))
    # Multiple legitimate charge lines: freight line + ancillary line on both sides.
    j = normal_job(final, "L", "CUSTC", "STE"); scenario["multi_line"] = j
    sell.append(_line("SELL", final, j, "CUSTC", "STE", 0, 25.0, 0, float(sell[-1]["Weight"]), float(sell[-1]["ChargeableWeight"]), float(sell[-1]["Cubic"]), work="TAIL", row_id=f"S{j}b"))
    buy.append(_line("BUY", final, j, "CUSTC", "STE", 0, 20.0, 0, float(buy[-1]["Weight"]), float(buy[-1]["ChargeableWeight"]), float(buy[-1]["Cubic"]), work="TAIL"))
    # Rule-overlapping candidate: same oddity as the anomaly, and present in current rule results.
    j = normal_job(final, "O", "CUSTB", "STE"); scenario["rule_overlap"] = j
    buy[-1].update(Freight=f"{float(buy[-1]['Freight']) * 5:.2f}", Nett=f"{float(buy[-1]['Nett']) * 5:.2f}")
    # Credit/reversal.
    j = normal_job(final, "C"); scenario["credit_reversal"] = j
    for rec in (sell[-1], buy[-1]):
        for c in ("Freight", "Fuel", "Nett"):
            rec[c] = f"{-abs(float(rec[c])):.2f}"
    j = normal_job(final, "U"); scenario["unreviewed"] = j

    sell_df, buy_df = pd.DataFrame(sell), pd.DataFrame(buy)
    moveit_rows = []
    for rec in sell:
        if rec["SnapshotWeek"] != pd.Timestamp(final) or rec["JobNumberRaw"].startswith("M"):
            continue
        w = float(rec["Weight"])
        if rec["JobNumberRaw"] == scenario["confirmed_anomaly"]:
            w = w / 3.0  # dispatch weight far below billed weight
        moveit_rows.append({"ConsignmentNo": rec["ConsignmentNoteRaw"][:-2], "TotWeightKg": w, "TotVolM": float(rec["Cubic"]), "PickupPostcode": "2000",
                            "DeliveryPostcode": "3000", "ActualDespatchDate": pd.Timestamp(final) - pd.Timedelta(days=4)})
    rules = pd.DataFrame([{"RuleResultId": 501, "JobNumberRaw": scenario["rule_overlap"], "Customer": "CUSTB", "Carrier": "STE", "ErrCode": "3",
                           "ShortErrDesc": "Revenue Check", "IsDeleted": False}])
    return {"sell": sell_df, "buy": buy_df, "moveit": pd.DataFrame(moveit_rows).drop_duplicates("ConsignmentNo"), "holds": pd.DataFrame(holds),
            "releases": pd.DataFrame(releases), "rules": rules, "scenario": scenario, "weeks": [pd.Timestamp(w) for w in week_list],
            "customer_groups": {"CUSTA": "GROUP A", "CUSTB": "GROUP B", "CUSTC": "GROUP C", "NEWCUST": "GROUP NEW"},
            "carrier_groups": {"STE": "STE", "TGE": "TGE", "NZ_NZCOU": "NZ COURIERS", "XFM": "XFM"},
            "routing": pd.DataFrame([{"Customer": c, "AccountManager": f"Synthetic AM {c[-1]}", "LoginName": f"am{c[-1].lower()}"} for c in ["CUSTA", "CUSTB", "CUSTC", "NEWCUST"]]),
            "currency_map": {"STE": "AUD", "TGE": "AUD", "XFM": "AUD", "NZ_NZCOU": "NZD"},
            "text_map": pd.DataFrame([{"LikePattern": "%credit received%", "OutcomeType": "IncorrectBuy", "ProposedLabel": 1, "LabelQuality": "WEAK", "Priority": 10}]),
            "feedback_jobs": feedback_jobs}
