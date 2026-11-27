"""Result stores.

LocalStore  : files under an output directory. Used for dry runs, shadow evaluation and the synthetic integration
              tests. Publication state is swapped atomically (write temp file + os.replace), mirroring the SQL design.
SqlStore    : ML.* tables on SQL Server. Staging inserts happen in one transaction; activation is the stored
              procedure ML.usp_ActivateRun (single transaction + app lock). A failure calls ML.usp_DiscardStagedRun,
              so a failed run never exposes partial published results and earlier results stay active.
"""
from __future__ import annotations

import json
import os
import socket
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, Iterable, List, Optional

import pandas as pd

from .db import Database, insert_rows


def utcnow() -> datetime:
    return datetime.now(timezone.utc).replace(tzinfo=None)


class Store:
    def existing_snapshot_keys(self, week) -> set:
        """Snapshots already captured for the week (immutable: reruns reuse them instead of inserting again)."""
        return set()
    def existing_feature_keys(self, week, feature_version: str) -> set:
        """Feature rows already stored (deterministic for a snapshot and version, so reruns skip them)."""
        return set()
    def start_run(self, run: Dict) -> None: ...
    def finish_run(self, run_id: str, status: str, metrics: Optional[Dict] = None, error: Optional[str] = None) -> None: ...
    def write(self, table: str, frame: pd.DataFrame) -> int: ...
    def active_publication_keys(self) -> set: ...
    def stage_exceptions(self, run_id: str, frame: pd.DataFrame) -> int: ...
    def activate(self, run_id: str) -> None: ...
    def discard(self, run_id: str, error: str) -> None: ...
    def rollback_publication(self, run_id: str, reason: str) -> None: ...


class LocalStore(Store):
    def __init__(self, root: Path):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self.state_path = self.root / "detected_exception.json"
        self.runs_path = self.root / "runs.json"

    # ---------- runs ----------
    def _runs(self) -> Dict:
        return json.loads(self.runs_path.read_text(encoding="utf-8")) if self.runs_path.exists() else {}

    def _save_runs(self, runs: Dict) -> None:
        _atomic_write(self.runs_path, json.dumps(runs, indent=2, default=str))

    def start_run(self, run: Dict) -> None:
        runs = self._runs()
        for r in runs.values():  # one in-flight run per type and week (same rule as UX_ML_Run_OneActivePerWeek)
            if r["Status"] == "STARTED" and r["RunType"] == run["RunType"] and r.get("ScoringWeek") == run.get("ScoringWeek"):
                raise RuntimeError(f"Run {r['RunId']} is already in progress for {run['RunType']} {run.get('ScoringWeek')}")
        runs[run["RunId"]] = {**run, "Status": "STARTED", "StartedAt": utcnow().isoformat(), "HostName": socket.gethostname()}
        self._save_runs(runs)

    def finish_run(self, run_id: str, status: str, metrics: Optional[Dict] = None, error: Optional[str] = None) -> None:
        runs = self._runs()
        runs[run_id].update(Status=status, CompletedAt=utcnow().isoformat(), MetricsJson=metrics, ErrorText=error)
        self._save_runs(runs)

    def run(self, run_id: str) -> Dict:
        return self._runs()[run_id]

    # ---------- tables ----------
    def write(self, table: str, frame: pd.DataFrame) -> int:
        if frame is None or frame.empty:
            return 0
        path = self.root / f"{table}.csv"
        frame.to_csv(path, mode="a", header=not path.exists(), index=False)
        return len(frame)

    # ---------- publication ----------
    def _state(self) -> List[Dict]:
        return json.loads(self.state_path.read_text(encoding="utf-8")) if self.state_path.exists() else []

    def exceptions(self) -> pd.DataFrame:
        return pd.DataFrame(self._state())

    def active_publication_keys(self) -> set:
        return {r["PublicationKey"] for r in self._state() if r["PublicationState"] == "ACTIVE"}

    def stage_exceptions(self, run_id: str, frame: pd.DataFrame) -> int:
        state = self._state()
        keys = {r["PublicationKey"] for r in state if r["PublicationState"] in ("STAGED", "ACTIVE")}
        next_id = max([r["ExceptionId"] for r in state], default=0) + 1
        added = 0
        for rec in json.loads(frame.to_json(orient="records", date_format="iso")):
            if rec["PublicationKey"] in keys:
                raise RuntimeError(f"Duplicate publication key {rec['PublicationKey']}")
            rec.update(ExceptionId=next_id, RunId=run_id, PublicationState="STAGED", IsActive=False, SupersededByExceptionId=None)
            state.append(rec)
            next_id += 1
            added += 1
        _atomic_write(self.state_path, json.dumps(state, indent=1, default=str))
        return added

    def activate(self, run_id: str) -> None:
        if self._runs()[run_id]["Status"] not in ("SUCCEEDED", "PUBLISHED"):
            raise RuntimeError("Only SUCCEEDED runs can be published")
        state = self._state()  # build the whole new state, then swap it in one atomic replace
        staged = {r["SnapshotKey"]: r for r in state if r["RunId"] == run_id and r["PublicationState"] == "STAGED"}
        now = utcnow().isoformat()
        for r in state:
            if r["IsActive"] and r["SnapshotKey"] in staged and r["RunId"] != run_id:
                r.update(IsActive=False, PublicationState="SUPERSEDED", SupersededByExceptionId=staged[r["SnapshotKey"]]["ExceptionId"])
        for r in staged.values():
            r.update(IsActive=True, PublicationState="ACTIVE", PublishedAt=now)
        _atomic_write(self.state_path, json.dumps(state, indent=1, default=str))
        self.finish_run(run_id, "PUBLISHED", self._runs()[run_id].get("MetricsJson"))

    def discard(self, run_id: str, error: str) -> None:
        state = self._state()
        for r in state:
            if r["RunId"] == run_id and r["PublicationState"] == "STAGED":
                r["PublicationState"] = "DISCARDED"
        _atomic_write(self.state_path, json.dumps(state, indent=1, default=str))
        if run_id in self._runs():
            self.finish_run(run_id, "FAILED", error=error)

    def rollback_publication(self, run_id: str, reason: str) -> None:
        state = self._state()
        withdrawn = {r["ExceptionId"] for r in state if r["RunId"] == run_id and r["PublicationState"] == "ACTIVE"}
        for r in state:
            if r["ExceptionId"] in withdrawn:
                r.update(IsActive=False, PublicationState="DISCARDED")
        active = {r["SnapshotKey"] for r in state if r["IsActive"]}
        for r in state:
            if r["PublicationState"] == "SUPERSEDED" and r["SupersededByExceptionId"] in withdrawn and r["SnapshotKey"] not in active:
                r.update(IsActive=True, PublicationState="ACTIVE", SupersededByExceptionId=None)
                active.add(r["SnapshotKey"])
        _atomic_write(self.state_path, json.dumps(state, indent=1, default=str))
        self.finish_run(run_id, "ABANDONED", error=f"Rolled back: {reason}")


def _atomic_write(path: Path, text: str) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)


# Column lists for SQL inserts (must match sql/migrations). Identifiers are code constants only.
SQL_TABLE_COLUMNS: Dict[str, List[str]] = {
    "ML.TradingSnapshot": ["SnapshotKey", "TransactionKey", "KeyVersion", "RunId", "SnapshotWeek", "SnapshotCapturedAt", "SourceSystem", "Customer",
                           "CustomerGroup", "Carrier", "CarrierGroup", "FinanceGroup", "JobNumber", "JobNumberRaw", "ConsignmentNote", "ConsignmentNoteRaw",
                           "SupplierRef", "WorkCode", "OptionCode", "FromLocation", "ToLocation", "PickupPostcode", "DeliveryPostcode", "ConsignmentDate",
                           "CurrencyCode", "CurrencyStatus", "Segment", "SellFreight", "SellOther", "SellFuel", "SellNett", "BuyFreight", "BuyOther",
                           "BuyFuel", "BuyNett", "SellLineCount", "BuyLineCount", "AncillaryChargeCount", "Weight", "ChargeableWeight", "Cubic",
                           "Pallet", "Quantity", "Items", "Hours", "Lift", "Space", "BuyWeight", "BuyChargeableWeight", "BuyCubic", "MeasurementConflict",
                           "MoveitMatchStatus", "MoveitWeightKg", "MoveitCubic", "MoveitPickupPostcode", "MoveitDeliveryPostcode", "MoveitDespatchDate",
                           "BuySellMatchStatus", "InvalidDateFlag", "AccountManager", "LoginName", "IsSynthetic"],
    "ML.SourceRowMap": ["SnapshotKey", "SourceTable", "SourceRowId", "SourceRowHash", "Side", "LineAmountNett", "MatchStatus"],
    "ML.DataQualityIssue": ["RunId", "SnapshotKey", "SourceTable", "IssueCode", "Severity", "Detail", "AffectedRows"],
    "ML.LabelEvent": ["EventSource", "SourceRecordId", "TransactionKey", "SnapshotKey", "MatchStatus", "EventType", "ProposedLabel", "LabelQuality",
                      "OutcomeType", "RuleCode", "EventTime", "LabelAvailableAt", "Reviewer", "Notes", "RunId"],
    "ML.TrainingLabel": ["LabelSetVersion", "TrainingCutoff", "TransactionKey", "SnapshotKey", "Label", "LabelSource", "LabelQuality", "OutcomeType",
                         "EventTime", "LabelAvailableAt", "Reviewer", "SourceRecordId", "MatchStatus", "ResolutionNotes", "RunId"],
    "ML.FeatureSnapshot": ["SnapshotKey", "FeatureVersion", "BaselineVersion", "RunId", "FeatureJson", "FeatureChecksum"],
    "ML.PeerBaseline": ["BaselineVersion", "BaselineCutoff", "WindowStart", "WindowEnd", "FallbackLevel", "PeerGroupKey", "MetricName",
                        "ObservationCount", "Median", "MAD", "P10", "P90", "RunId"],
    "ML.ModelRegistry": ["ModelVersion", "ModelKind", "Status", "FeatureVersion", "PreprocessingVersion", "TrainingWindowStart", "TrainingWindowEnd",
                         "LabelSetVersion", "ArtifactUri", "ArtifactSha256", "DependenciesJson", "MetricsJson", "LimitationsText",
                         "ProvidesCalibratedProbability", "IsSyntheticOnly", "ParentModelVersion"],
    "ML.ScoreAudit": ["RunId", "SnapshotKey", "TransactionKey", "ScoringWeek", "Decision", "DecisionReasonCodes", "CandidateRoute", "RankingScore",
                      "ConfirmedExceptionProbability", "WeakFlagScore", "AEReconstructionError", "AEAnomalyPercentile", "EvidenceCount", "ReasonCodes",
                      "PeerFallbackLevel", "PeerCount", "ModelVersion", "ClassifierVersion", "FeatureVersion", "ConfigVersion", "ScoredAt",
                      "IsReviewCandidate", "SamplingStratum", "SamplingProbability", "PrimaryReasonCode", "ReasonSummary", "ExplanationText",
                      "RecommendedAction", "EvidenceJson", "AlreadyRuleFlagged", "RuleCodes"],
    "ML.DetectedException": ["PublicationKey", "TransactionKey", "SnapshotKey", "CurrentWeek", "ConsignmentNote", "JobNumber", "SupplierRef", "Customer",
                             "CustomerGroup", "Carrier", "CarrierGroup", "FinanceGroup", "AccountManager", "DetectionRoute", "PrimaryReasonCode",
                             "ReasonSummary", "ExplanationText", "RecommendedAction", "ConfirmedExceptionProbability", "AEReconstructionError",
                             "AEAnomalyPercentile", "EvidenceJson", "PeerGroupKey", "PeerCount", "BaselineCutoff", "EvidenceCount", "CurrencyCode",
                             "BuyNett", "SellNett", "MarginAmount", "MarginRatio", "PotentialExposureAmount", "ExposureCalculationMethod",
                             "AlreadyRuleFlagged", "RuleCodes", "ExistingRuleResultId", "RunId", "ModelVersion", "FeatureVersion", "ConfigVersion",
                             "ScoredAt", "PublicationState", "ReviewStatus", "IsActive", "IsSynthetic"],
}


class SqlStore(Store):
    def __init__(self, db: Database):
        self.db = db

    def start_run(self, run: Dict) -> None:
        with self.db.transaction() as cur:
            insert_rows(cur, "ML.Run", ["RunId", "RunType", "ScoringWeek", "Status", "IsDryRun", "IsShadow", "ConfigVersion", "ConfigSnapshotJson",
                                        "ConfigChecksum", "ModelVersion", "FeatureVersion", "KeyVersion", "SourceWatermarkJson", "HostName"],
                        [[run["RunId"], run["RunType"], run.get("ScoringWeek"), "STARTED", 0, int(run.get("IsShadow", 1)), run.get("ConfigVersion"),
                          run.get("ConfigSnapshotJson"), run.get("ConfigChecksum"), run.get("ModelVersion"), run.get("FeatureVersion"),
                          run.get("KeyVersion"), json.dumps(run.get("SourceWatermark"), default=str), socket.gethostname()]])

    def finish_run(self, run_id: str, status: str, metrics: Optional[Dict] = None, error: Optional[str] = None) -> None:
        with self.db.transaction() as cur:
            cur.execute("UPDATE ML.Run SET Status = ?, MetricsJson = ?, ErrorText = ?, CompletedAt = SYSUTCDATETIME() WHERE RunId = ?",
                        status, json.dumps(metrics, default=str) if metrics else None, (error or "")[:4000] or None, run_id)

    def write(self, table: str, frame: pd.DataFrame) -> int:
        if frame is None or frame.empty:
            return 0
        cols = SQL_TABLE_COLUMNS[table]
        with self.db.transaction() as cur:
            return insert_rows(cur, table, cols, frame.reindex(columns=cols).itertuples(index=False, name=None))

    def existing_feature_keys(self, week, feature_version: str) -> set:
        df = self.db.read_df("""SELECT f.SnapshotKey FROM ML.FeatureSnapshot f JOIN ML.TradingSnapshot s ON s.SnapshotKey = f.SnapshotKey
                                WHERE s.SnapshotWeek = ? AND f.FeatureVersion = ?""", (pd.Timestamp(week).date(), feature_version))
        return set(df["SnapshotKey"])

    def existing_snapshot_keys(self, week) -> set:
        df = self.db.read_df("SELECT SnapshotKey FROM ML.TradingSnapshot WHERE SnapshotWeek = ?", (pd.Timestamp(week).date(),))
        return set(df["SnapshotKey"])

    def active_publication_keys(self) -> set:
        df = self.db.read_df("SELECT PublicationKey FROM ML.DetectedException WHERE PublicationState = 'ACTIVE'")
        return set(df["PublicationKey"])

    def stage_exceptions(self, run_id: str, frame: pd.DataFrame) -> int:
        return self.write("ML.DetectedException", frame)

    def activate(self, run_id: str) -> None:
        with self.db.transaction() as cur:
            cur.execute("EXEC ML.usp_ActivateRun @RunId = ?", run_id)

    def discard(self, run_id: str, error: str) -> None:
        with self.db.transaction() as cur:
            cur.execute("EXEC ML.usp_DiscardStagedRun @RunId = ?, @ErrorText = ?", run_id, error[:4000])

    def rollback_publication(self, run_id: str, reason: str) -> None:
        with self.db.transaction() as cur:
            cur.execute("EXEC ML.usp_RollbackPublication @RunId = ?, @Reason = ?", run_id, reason[:1000])
