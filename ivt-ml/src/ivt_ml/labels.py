"""Label events and point-in-time training labels (docs/label_policy.md).

Label sources and quality:
  ML_REVIEW  (ML.ReviewFeedback)            -> CONFIRMED. The only source of confirmed labels.
  RELEASE    (dbo.ReleasedConsignment)      -> WEAK, only when the justification matches an APPROVED ML.OutcomeTextMap
                                              pattern; otherwise no label (bulk "as per AM" releases are not adjudications).
  HOLD       (dbo.ConsignmentForReview)     -> WEAK positive evidence of hold behaviour, never a confirmed exception.
  RULE_FLAG  (tmp.IVT_ResultHistory)        -> EVIDENCE_ONLY (no label); used for overlap analysis.

Policy at a training cutoff T (only events with LabelAvailableAt <= T are visible):
  1. A current CONFIRMED review outcome wins (INSUFFICIENT_INFORMATION -> unlabeled).
  2. Otherwise mapped WEAK release outcomes; contradictory mapped outcomes -> excluded and reported.
  3. Otherwise an unreleased hold -> WEAK 1 (as of T). A hold released without an interpretable outcome -> unlabeled.
  4. No events -> unlabeled (NOT a negative).
Ambiguously matched events never produce labels.
"""
from __future__ import annotations

import fnmatch
from dataclasses import dataclass
from typing import List, Optional

import numpy as np
import pandas as pd

from .contracts import NEGATIVE_OUTCOMES, POSITIVE_OUTCOMES
from .keys import normalize_identifier, transaction_key

EVENT_COLUMNS = ["EventSource", "SourceRecordId", "TransactionKey", "SnapshotKey", "MatchStatus", "EventType", "ProposedLabel",
                 "LabelQuality", "OutcomeType", "RuleCode", "EventTime", "LabelAvailableAt", "Reviewer", "Notes"]

MANUAL_HOLD_RULE_CODE = "9999"  # "AM Custom Held From Trading Data" (verified in dbo.ConsignmentForReview profiling)


def match_events(events: pd.DataFrame, snapshots: pd.DataFrame, week_col: str) -> pd.DataFrame:
    """Attach TransactionKey/SnapshotKey to source events by (customer, carrier, job, week).

    EXACT: full business key + week. MAPPED: unique snapshot for (job, week) with different customer/carrier code.
    AMBIGUOUS: several snapshots for (job, week) or the snapshot's buy/sell join is ambiguous. UNMATCHED: none.
    """
    ev = events.copy()
    ev["_job"] = ev["JobNumberRaw"].map(normalize_identifier)
    ev["_week"] = pd.to_datetime(ev[week_col]).dt.normalize()
    ev["_txn"] = [transaction_key(c, k, j) if j else None for c, k, j in zip(ev.get("Customer"), ev.get("Carrier"), ev["_job"])]
    snap = snapshots[["SnapshotKey", "TransactionKey", "JobNumber", "SnapshotWeek", "BuySellMatchStatus"]].copy()
    snap["SnapshotWeek"] = pd.to_datetime(snap["SnapshotWeek"]).dt.normalize()
    exact = ev.merge(snap, left_on=["_txn", "_week"], right_on=["TransactionKey", "SnapshotWeek"], how="left")
    per_job = snap.groupby(["JobNumber", "SnapshotWeek"]).agg(n=("SnapshotKey", "size"), sk=("SnapshotKey", "first"),
                                                              tk=("TransactionKey", "first"), st=("BuySellMatchStatus", "first")).reset_index()
    alt = ev.merge(per_job, left_on=["_job", "_week"], right_on=["JobNumber", "SnapshotWeek"], how="left")
    status, skey, tkey = [], [], []
    for i in range(len(ev)):
        if pd.notna(exact.at[i, "SnapshotKey"]):
            amb = exact.at[i, "BuySellMatchStatus"] == "AMBIGUOUS"
            status.append("AMBIGUOUS" if amb else "EXACT")
            skey.append(exact.at[i, "SnapshotKey"]); tkey.append(exact.at[i, "TransactionKey"])
        elif pd.notna(alt.at[i, "n"]) and alt.at[i, "n"] == 1:
            amb = alt.at[i, "st"] == "AMBIGUOUS"
            status.append("AMBIGUOUS" if amb else "MAPPED")
            skey.append(alt.at[i, "sk"]); tkey.append(alt.at[i, "tk"])
        elif pd.notna(alt.at[i, "n"]):
            status.append("AMBIGUOUS"); skey.append(None); tkey.append(None)
        else:
            status.append("UNMATCHED"); skey.append(None); tkey.append(None)
    ev["MatchStatus"], ev["SnapshotKey"], ev["TransactionKey"] = status, skey, tkey
    return ev


def _map_justification(text: object, text_map: pd.DataFrame) -> tuple:
    """Return (OutcomeType, ProposedLabel) for the highest-priority APPROVED pattern, else (None, None).
    Patterns use SQL LIKE semantics ('%' and '_'), matched case-insensitively."""
    if text is None or (isinstance(text, float) and np.isnan(text)) or text_map is None or text_map.empty:
        return None, None
    value = str(text).strip().lower()
    for _, row in text_map.sort_values("Priority").iterrows():
        pattern = str(row["LikePattern"]).lower().replace("%", "*").replace("_", "?")
        if fnmatch.fnmatchcase(value, pattern):
            label = row["ProposedLabel"]
            return row["OutcomeType"], (None if pd.isna(label) else int(label))
    return None, None


def build_label_events(snapshots: pd.DataFrame, *, holds: Optional[pd.DataFrame] = None, releases: Optional[pd.DataFrame] = None,
                       rule_history: Optional[pd.DataFrame] = None, feedback: Optional[pd.DataFrame] = None,
                       text_map: Optional[pd.DataFrame] = None) -> pd.DataFrame:
    frames: List[pd.DataFrame] = []
    if holds is not None and not holds.empty:
        h = match_events(holds, snapshots, "InvoiceWeek")
        manual = h["ErrCode"].astype(str).str.strip() == MANUAL_HOLD_RULE_CODE
        frames.append(pd.DataFrame({
            "EventSource": "HOLD", "SourceRecordId": h["SourceRecordId"], "TransactionKey": h["TransactionKey"], "SnapshotKey": h["SnapshotKey"],
            "MatchStatus": h["MatchStatus"], "EventType": np.where(manual, "HOLD_MANUAL", "HOLD_RULE"), "ProposedLabel": 1,
            "LabelQuality": "WEAK", "OutcomeType": None, "RuleCode": h["ErrCode"], "EventTime": pd.to_datetime(h["ActionTime"]),
            "LabelAvailableAt": pd.to_datetime(h["ActionTime"]), "Reviewer": h.get("Reviewer"), "Notes": h.get("ShortErrDesc")}))
    if releases is not None and not releases.empty:
        r = match_events(releases, snapshots, "InvoiceWeek")
        mapped = [_map_justification(j, text_map) for j in r["Justification"]]
        outcome = [m[0] for m in mapped]
        label = [m[1] for m in mapped]
        frames.append(pd.DataFrame({
            "EventSource": "RELEASE", "SourceRecordId": r["SourceRecordId"], "TransactionKey": r["TransactionKey"], "SnapshotKey": r["SnapshotKey"],
            "MatchStatus": r["MatchStatus"], "EventType": "RELEASE", "ProposedLabel": label,
            "LabelQuality": ["WEAK" if o is not None and o != "InsufficientInformation" else "EVIDENCE_ONLY" for o in outcome],
            "OutcomeType": outcome, "RuleCode": r["ErrCode"], "EventTime": pd.to_datetime(r["ActionTime"]),
            "LabelAvailableAt": pd.to_datetime(r["ActionTime"]), "Reviewer": r.get("Reviewer"),
            "Notes": r["Justification"].astype(str).str.slice(0, 500)}))
    if rule_history is not None and not rule_history.empty:
        rh = match_events(rule_history, snapshots, "CurrentWeek")
        frames.append(pd.DataFrame({
            "EventSource": "RULE_RESULT", "SourceRecordId": rh["SourceRecordId"], "TransactionKey": rh["TransactionKey"], "SnapshotKey": rh["SnapshotKey"],
            "MatchStatus": rh["MatchStatus"], "EventType": "RULE_FLAG", "ProposedLabel": None, "LabelQuality": "EVIDENCE_ONLY",
            "OutcomeType": None, "RuleCode": rh["ErrCode"], "EventTime": pd.to_datetime(rh["CurrentWeek"]),
            "LabelAvailableAt": pd.to_datetime(rh["LastModifiedTime"]), "Reviewer": None, "Notes": None}))
    if feedback is not None and not feedback.empty:
        f = feedback.merge(snapshots[["SnapshotKey", "TransactionKey"]], on="SnapshotKey", how="left")
        lbl = [1 if o == "CONFIRMED_EXCEPTION" else 0 if o == "CONFIRMED_VALID" else None for o in f["ReviewOutcome"]]
        frames.append(pd.DataFrame({
            "EventSource": "ML_REVIEW", "SourceRecordId": f["FeedbackId"].astype(str) + ":v" + f["OutcomeVersion"].astype(str),
            "TransactionKey": f["TransactionKey"], "SnapshotKey": f["SnapshotKey"],
            "MatchStatus": np.where(f["TransactionKey"].notna(), "EXACT", "UNMATCHED"), "EventType": "REVIEW_OUTCOME",
            "ProposedLabel": lbl, "LabelQuality": "CONFIRMED", "OutcomeType": f["OutcomeType"], "RuleCode": None,
            "EventTime": pd.to_datetime(f["ReviewedAt"]), "LabelAvailableAt": pd.to_datetime(f["ReviewedAt"]),
            "Reviewer": f["ReviewedBy"], "Notes": "IsCurrent=" + f["IsCurrent"].astype(str) + ";OutcomeVersion=" + f["OutcomeVersion"].astype(str)}))
    if not frames:
        return pd.DataFrame(columns=EVENT_COLUMNS)
    events = pd.concat(frames, ignore_index=True)[EVENT_COLUMNS]
    events["ProposedLabel"] = events["ProposedLabel"].astype("Int64")
    return events


@dataclass
class LabelBuild:
    labels: pd.DataFrame
    report: dict


def training_labels(events: pd.DataFrame, snapshots: pd.DataFrame, cutoff: pd.Timestamp, label_set_version: str) -> LabelBuild:
    """Point-in-time labels at `cutoff` following the module policy. Returns one row per labelled snapshot."""
    cutoff = pd.Timestamp(cutoff)
    visible = events[(events["LabelAvailableAt"] <= cutoff) & events["SnapshotKey"].notna()]
    unusable = visible[visible["MatchStatus"].isin(["AMBIGUOUS", "UNMATCHED"])]
    visible = visible[visible["MatchStatus"].isin(["EXACT", "MAPPED"])]
    rows, conflicts, released_unclear = [], 0, 0
    for skey, grp in visible.groupby("SnapshotKey"):
        review = grp[grp["EventSource"] == "ML_REVIEW"].sort_values("EventTime")
        if not review.empty:
            latest = review.iloc[-1]  # most recent outcome version visible at the cutoff
            if pd.isna(latest["ProposedLabel"]):
                continue  # INSUFFICIENT_INFORMATION stays unlabeled
            rows.append(_label_row(latest, int(latest["ProposedLabel"]), "CONFIRMED", skey, cutoff, label_set_version, "Confirmed review outcome"))
            continue
        weak_release = grp[(grp["EventSource"] == "RELEASE") & grp["ProposedLabel"].notna()]
        if not weak_release.empty:
            if weak_release["ProposedLabel"].nunique() > 1:
                conflicts += 1
                continue
            last = weak_release.sort_values("EventTime").iloc[-1]
            rows.append(_label_row(last, int(last["ProposedLabel"]), "WEAK", skey, cutoff, label_set_version, "Mapped release justification"))
            continue
        holds = grp[grp["EventSource"] == "HOLD"]
        if not holds.empty:
            if (grp["EventSource"] == "RELEASE").any():
                released_unclear += 1  # released without an interpretable outcome: unlabeled
                continue
            first = holds.sort_values("EventTime").iloc[0]
            rows.append(_label_row(first, 1, "WEAK", skey, cutoff, label_set_version, "Unreleased hold as of cutoff (hold behaviour, not confirmed)"))
    labels = pd.DataFrame(rows)
    if not labels.empty:
        tk = snapshots.set_index("SnapshotKey")["TransactionKey"]
        labels["TransactionKey"] = labels["SnapshotKey"].map(tk)
    report = {"cutoff": str(cutoff), "visible_events": int(len(visible)), "unusable_ambiguous_or_unmatched": int(len(unusable)),
              "excluded_conflicts": conflicts, "released_without_outcome": released_unclear,
              "confirmed_positive": int(((labels.get("LabelQuality") == "CONFIRMED") & (labels.get("Label") == 1)).sum()) if not labels.empty else 0,
              "confirmed_negative": int(((labels.get("LabelQuality") == "CONFIRMED") & (labels.get("Label") == 0)).sum()) if not labels.empty else 0,
              "weak_positive": int(((labels.get("LabelQuality") == "WEAK") & (labels.get("Label") == 1)).sum()) if not labels.empty else 0,
              "weak_negative": int(((labels.get("LabelQuality") == "WEAK") & (labels.get("Label") == 0)).sum()) if not labels.empty else 0}
    return LabelBuild(labels, report)


def _label_row(event: pd.Series, label: int, quality: str, skey: str, cutoff: pd.Timestamp, version: str, note: str) -> dict:
    return {"LabelSetVersion": version, "TrainingCutoff": cutoff, "SnapshotKey": skey, "Label": label, "LabelSource": event["EventSource"],
            "LabelQuality": quality, "OutcomeType": event["OutcomeType"], "EventTime": event["EventTime"], "LabelAvailableAt": event["LabelAvailableAt"],
            "Reviewer": event["Reviewer"], "SourceRecordId": event["SourceRecordId"], "MatchStatus": event["MatchStatus"], "ResolutionNotes": note}


def weak_hold_targets(snapshots: pd.DataFrame, events: pd.DataFrame, cutoff: pd.Timestamp, maturity_days: int = 14) -> pd.Series:
    """Target for the EXPERIMENTAL weak-label classifier: "was this job put on hold (manually) within the
    maturity window?". 1 = hold visible by cutoff; 0 = mature with no hold; NA = too recent to know.
    This predicts historical hold behaviour, NOT confirmed exceptions."""
    cutoff = pd.Timestamp(cutoff)
    held = events[(events["EventSource"] == "HOLD") & (events["EventType"] == "HOLD_MANUAL") & (events["LabelAvailableAt"] <= cutoff)
                  & events["MatchStatus"].isin(["EXACT", "MAPPED"])]["SnapshotKey"].unique()
    weeks = pd.to_datetime(snapshots["SnapshotWeek"])
    mature = weeks + pd.Timedelta(days=maturity_days) <= cutoff
    target = pd.Series(pd.NA, index=snapshots.index, dtype="Int64")
    target[mature] = 0
    target[snapshots["SnapshotKey"].isin(held)] = 1
    ambiguous = snapshots["BuySellMatchStatus"] == "AMBIGUOUS"
    target[ambiguous] = pd.NA
    return target


def review_sample(scored: pd.DataFrame, per_stratum: int, seed: int = 7) -> pd.DataFrame:
    """Stratified review sample with recorded sampling probabilities (for selection-bias correction).

    Strata: RULE_FLAGGED, AE_CANDIDATE, MODEL_CANDIDATE, UNFLAGGED. `scored` needs columns AlreadyRuleFlagged,
    IsAECandidate, IsModelCandidate.
    """
    rng = np.random.default_rng(seed)
    strata = np.select([scored["AlreadyRuleFlagged"].astype(bool), scored["IsModelCandidate"].astype(bool), scored["IsAECandidate"].astype(bool)],
                       ["RULE_FLAGGED", "MODEL_CANDIDATE", "AE_CANDIDATE"], default="UNFLAGGED")
    frame = scored.assign(SamplingStratum=strata)
    picks = []
    for name, grp in frame.groupby("SamplingStratum"):
        n = min(per_stratum, len(grp))
        idx = rng.choice(grp.index.values, size=n, replace=False)
        chosen = frame.loc[idx].copy()
        chosen["SamplingProbability"] = n / len(grp)
        picks.append(chosen)
    return pd.concat(picks) if picks else frame.iloc[0:0]
