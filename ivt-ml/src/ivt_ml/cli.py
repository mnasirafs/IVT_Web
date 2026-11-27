"""Command line entry point: `ivt-ml <command> [options]` (also wrapped by scripts/*.py).

Safety defaults: every command uses the LOCAL store (files under --output-dir) unless --store sql is given, and
--store sql still refuses database writes when --dry-run is set. Schema migrations print SQL unless --apply is
given together with --i-am-authorised.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import logging
import re
import sys
from datetime import date, datetime
from pathlib import Path
from typing import List, Optional

import pandas as pd

from .config import DEFAULTS, ControlRow, EffectiveConfig, Settings, defaults_as_rows

log = logging.getLogger("ivt_ml.cli")
MIGRATIONS_DIR = Path(__file__).resolve().parents[2] / "sql" / "migrations"


# ---------------------------------------------------------------- helpers
def _db(settings: Settings):
    from .db import Database
    return Database(settings.db_conn)


def _config(args, settings: Settings, as_of: date) -> EffectiveConfig:
    version = getattr(args, "config_version", None) or "dev-defaults"
    rows: List[ControlRow] = []
    if getattr(args, "store", "local") == "sql" and settings.db_conn:
        db = _db(settings)
        if db.scalar("SELECT OBJECT_ID('ML.ControlParameter')") is not None:
            df = db.read_df("SELECT * FROM ML.ControlParameter WHERE ConfigVersion = ?", (version,))
            rows = [ControlRow(r.ConfigVersion, r.ScopeType, r.ScopeKey, r.ParameterName, None if pd.isna(r.NumericValue) else float(r.NumericValue),
                               r.TextValue, pd.Timestamp(r.EffectiveFrom).date(), None if pd.isna(r.EffectiveTo) else pd.Timestamp(r.EffectiveTo).date(),
                               bool(r.IsEnabled), r.ApprovedBy, r.ApprovedAt) for r in df.itertuples()]
    if not rows:
        rows = defaults_as_rows(version)
    if getattr(args, "config_file", None):
        overrides = json.loads(Path(args.config_file).read_text(encoding="utf-8"))
        rows = [r for r in rows if r.parameter_name not in overrides]
        for name, value in overrides.items():
            numeric = float(value) if isinstance(value, (int, float)) else None
            rows.append(ControlRow(version, "GLOBAL", "", name, numeric, None if numeric is not None else str(value), date(2000, 1, 1), None))
    return EffectiveConfig(version, as_of, rows)


def _context(args, settings: Settings, as_of: date):
    from .pipeline import Context, SyntheticSource
    from .store import LocalStore, SqlStore

    out = Path(args.output_dir or settings.output_dir)
    if getattr(args, "synthetic", False):
        from .synthetic import build_fixture
        source = SyntheticSource(build_fixture())
    else:
        from .source_adapters import IvtSource
        source = IvtSource(_db(settings))
    if args.store == "sql" and not args.dry_run:
        store = SqlStore(_db(settings))
    else:
        store = LocalStore(out / "store")
    return Context(source=source, store=store, cfg=_config(args, settings, as_of), artifact_dir=Path(args.artifact_dir or settings.artifact_dir),
                   cache_dir=out / "cache")


def _week(value: Optional[str]) -> Optional[date]:
    return datetime.strptime(value, "%Y-%m-%d").date() if value else None


# ---------------------------------------------------------------- commands
def cmd_profile(args, settings) -> int:
    from .profiling import run_profile
    out = Path(args.output_dir or settings.output_dir / "profiling")
    out.mkdir(parents=True, exist_ok=True)
    report = run_profile(_db(settings), recent_weeks=args.recent_weeks, log=log.info)
    (out / "profiling_report.md").write_text(report, encoding="utf-8")
    log.info("wrote %s", out / "profiling_report.md")
    return 0


def split_batches(sql: str) -> List[str]:
    sql = sql.lstrip("﻿")
    return [b.strip() for b in re.split(r"^\s*GO\s*$", sql, flags=re.M | re.I) if b.strip()]


def migration_checksum(path: Path) -> str:
    """SHA-256 of the script with BOM removed and line endings normalised, so a git checkout that converts
    LF/CRLF does not look like a changed migration."""
    data = path.read_bytes().lstrip(b"\xef\xbb\xbf").replace(b"\r\n", b"\n")
    return hashlib.sha256(data).hexdigest()


def cmd_migrate(args, settings) -> int:
    files = sorted(MIGRATIONS_DIR.glob("V*.sql"))
    if not args.apply:
        for f in files:
            print(f"-- ===== {f.name} (sha256 {migration_checksum(f)[:12]}) =====")
            print(f.read_text(encoding="utf-8"))
        log.info("dry run: printed %d migration files; nothing executed", len(files))
        return 0
    if not args.i_am_authorised:
        log.error("Refusing to apply migrations without --i-am-authorised (environment change requires explicit authorisation).")
        return 2
    db = _db(settings)
    for f in files:
        version = f.name.split("__")[0]
        checksum = migration_checksum(f)
        applied = db.scalar("SELECT Checksum FROM ML.SchemaVersion WHERE Version = ?", (version,)) if db.scalar("SELECT OBJECT_ID('ML.SchemaVersion')") else None
        if applied:
            if applied != checksum:
                log.error("%s was applied with a different checksum; stop and review", f.name)
                return 3
            log.info("%s already applied", f.name)
            continue
        with db.transaction() as cur:
            for batch in split_batches(f.read_text(encoding="utf-8")):
                cur.execute(batch)
            cur.execute("INSERT INTO ML.SchemaVersion (Version, ScriptName, Checksum) VALUES (?, ?, ?)", version, f.name, checksum)
        log.info("applied %s", f.name)
    return 0


def cmd_seed_config(args, settings) -> int:
    lines = [f"/* Development defaults for ConfigVersion '{args.config_version}'. NOT production-approved values. */"]
    for name, value in DEFAULTS.items():
        numeric = "NULL" if not isinstance(value, (int, float)) else repr(float(value))
        text = "NULL" if isinstance(value, (int, float)) else "N'" + str(value).replace("'", "''") + "'"
        lines.append(f"IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'{args.config_version}' AND ScopeType = N'GLOBAL' AND ParameterName = N'{name}')\n"
                     f"    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)\n"
                     f"    VALUES (N'{args.config_version}', N'GLOBAL', N'', N'{name}', {numeric}, {text}, '2000-01-01', N'Development default');")
    sql = "\n".join(lines) + "\n"
    if args.output:
        Path(args.output).write_text(sql, encoding="utf-8")
    else:
        print(sql)
    return 0


def cmd_snapshot(args, settings) -> int:
    """Extract + reconcile one week into the canonical snapshot (milestones B/C); writes CSVs and a reconciliation report."""
    from .pipeline import snapshot_week
    week = _week(args.week)
    ctx = _context(args, settings, week or date.today())
    current = pd.Timestamp(ctx.source.current_week())
    week = pd.Timestamp(week) if week else current
    res = snapshot_week(ctx, week, kind="current" if week == current else "history")
    out = Path(args.output_dir or settings.output_dir) / "snapshots"
    out.mkdir(parents=True, exist_ok=True)
    res.snapshots.to_csv(out / f"snapshot_{week:%Y-%m-%d}.csv", index=False)
    report = {"week": str(week.date()), "totals": res.totals, "issues": res.issues, "match_status": res.snapshots["BuySellMatchStatus"].value_counts().to_dict(),
              "segments": res.snapshots["Segment"].value_counts().to_dict(), "moveit": res.snapshots["MoveitMatchStatus"].value_counts().to_dict(),
              "currency": res.snapshots["CurrencyStatus"].value_counts().to_dict()}
    (out / f"reconciliation_{week:%Y-%m-%d}.json").write_text(json.dumps(report, indent=2, default=str), encoding="utf-8")
    print(json.dumps(report, indent=2, default=str))
    return 0


def cmd_labels(args, settings) -> int:
    """Label events and point-in-time label report for snapshot weeks [start, as-of)."""
    from .labels import build_label_events, training_labels
    from .pipeline import history_frame
    as_of = pd.Timestamp(_week(args.as_of) or date.today())
    ctx = _context(args, settings, as_of.date())
    weeks = [pd.Timestamp(w) for w in ctx.source.history_weeks((as_of - pd.Timedelta(weeks=args.weeks)).date(), (as_of - pd.Timedelta(days=1)).date())]
    snaps = history_frame(ctx, weeks)
    start = weeks[0].date()
    events = build_label_events(snaps, holds=ctx.source.holds(start, as_of.date()), releases=ctx.source.releases(start, as_of.date()),
                                rule_history=ctx.source.rule_history(start, as_of.date()), feedback=ctx.source.review_feedback(),
                                text_map=ctx.source.outcome_text_map())
    built = training_labels(events, snaps, as_of, f"ls-{as_of:%Y%m%d}")
    out = Path(args.output_dir or settings.output_dir) / "labels"
    out.mkdir(parents=True, exist_ok=True)
    events.to_csv(out / f"label_events_{as_of:%Y%m%d}.csv", index=False)
    built.labels.to_csv(out / f"training_labels_{as_of:%Y%m%d}.csv", index=False)
    summary = {"report": built.report, "events_by_source_and_match": events.groupby(["EventSource", "MatchStatus"]).size().to_dict()}
    (out / f"label_report_{as_of:%Y%m%d}.json").write_text(json.dumps(summary, indent=2, default=str), encoding="utf-8")
    print(json.dumps(summary, indent=2, default=str))
    return 0


def cmd_features(args, settings) -> int:
    """Point-in-time baselines + features for one week (shared code path with training and scoring)."""
    from .pipeline import features_for_week, snapshot_week
    week = pd.Timestamp(_week(args.week))
    ctx = _context(args, settings, week.date())
    current = pd.Timestamp(ctx.source.current_week())
    lookback = int(ctx.cfg.get("PeerLookbackWeeks"))
    snap = snapshot_week(ctx, week, kind="current" if week == current else "history").snapshots
    weeks = [pd.Timestamp(w) for w in ctx.source.history_weeks((week - pd.Timedelta(weeks=lookback + 1)).date(), (week - pd.Timedelta(days=1)).date())]
    feats = features_for_week(ctx, week, snap, weeks)
    out = Path(args.output_dir or settings.output_dir) / "features"
    out.mkdir(parents=True, exist_ok=True)
    feats.drop(columns=["AccountManager", "LoginName"], errors="ignore").to_csv(out / f"features_{week:%Y-%m-%d}.csv", index=False)
    print(json.dumps({"rows": len(feats), "sparse_peer_share": float(feats["SparsePeerFlag"].mean()),
                      "fallback_levels": feats["PeerFallbackLevel"].value_counts().to_dict()}, indent=2, default=str))
    return 0


def cmd_train(args, settings) -> int:
    from .pipeline import train
    as_of = _week(args.as_of) or date.today()
    ctx = _context(args, settings, as_of)
    if args.synthetic:
        as_of = ctx.source.current_week()
    report = train(ctx, as_of=as_of, feature_weeks=args.feature_weeks, output_dir=Path(args.output_dir or settings.output_dir), contamination_check=not args.skip_contamination_check)
    print(json.dumps({k: report.get(k) for k in ("run_id", "classifier_kind", "labels", "registered", "benchmarks")}, indent=2, default=str))
    return 0


def cmd_score(args, settings) -> int:
    from .pipeline import score_week
    week = _week(args.week)
    ctx = _context(args, settings, week or date.today())
    if args.publish and (args.dry_run or args.store != "sql") and not args.synthetic:
        log.warning("--publish with a local/dry-run store activates results in the local store only")
    result = score_week(ctx, week=week, output_dir=Path(args.output_dir or settings.output_dir), allow_candidate=args.allow_candidate, publish=args.publish)
    print(json.dumps({"run_id": result["run_id"], "decisions": result["summary"]["decisions"], "staged": result["summary"]["staged"],
                      "quality": result["summary"]["quality"]}, indent=2, default=str))
    return 0


def cmd_backtest(args, settings) -> int:
    """Replays weekly scoring over historical weeks in a local, isolated store (never publishes to SQL)."""
    from .pipeline import score_week
    from .store import LocalStore
    args.store, args.dry_run = "local", True
    weeks = pd.date_range(args.start, args.end, freq="7D")
    out = Path(args.output_dir or settings.output_dir) / "backtest"
    rows = []
    for w in weeks:
        ctx = _context(args, settings, w.date())
        ctx.store = LocalStore(out / "store")
        avail = [pd.Timestamp(x) for x in ctx.source.history_weeks(w.date(), w.date())]
        if not avail:
            continue
        res = score_week(ctx, week=avail[0].date(), output_dir=out, allow_candidate=True)
        audit = res["audit"]
        rows.append({"week": str(avail[0].date()), "scored": len(audit), **{f"decision_{k}": v for k, v in audit["Decision"].value_counts().items()},
                     "ae_ge_threshold": int((audit["AEAnomalyPercentile"] >= float(ctx.cfg.get("AEPercentileThreshold"))).sum()),
                     "with_2plus_evidence": int((audit["EvidenceCount"] >= 2).sum())})
    df = pd.DataFrame(rows)
    out.mkdir(parents=True, exist_ok=True)
    df.to_csv(out / "backtest_summary.csv", index=False)
    print(df.to_string(index=False))
    return 0


def cmd_publish(args, settings) -> int:
    from .store import LocalStore, SqlStore
    store = SqlStore(_db(settings)) if args.store == "sql" else LocalStore(Path(args.output_dir or settings.output_dir) / "store")
    store.activate(args.run_id)
    log.info("activated run %s", args.run_id)
    return 0


def cmd_rollback_publication(args, settings) -> int:
    from .store import LocalStore, SqlStore
    store = SqlStore(_db(settings)) if args.store == "sql" else LocalStore(Path(args.output_dir or settings.output_dir) / "store")
    store.rollback_publication(args.run_id, args.reason)
    log.info("rolled back run %s", args.run_id)
    return 0


def cmd_approve_model(args, settings) -> int:
    from .registry import LocalRegistry
    entry = LocalRegistry(Path(args.artifact_dir or settings.artifact_dir)).approve(args.model_version, args.approved_by, evidence_note=args.evidence)
    if args.store == "sql" and not args.dry_run:
        with _db(settings).transaction() as cur:
            cur.execute("UPDATE ML.ModelRegistry SET Status = 'RETIRED' WHERE ModelKind = ? AND Status = 'APPROVED'", entry["ModelKind"])
            cur.execute("UPDATE ML.ModelRegistry SET Status = 'APPROVED', ApprovedBy = ?, ApprovedAt = SYSUTCDATETIME() WHERE ModelVersion = ?", args.approved_by, args.model_version)
    print(json.dumps(entry, indent=2, default=str))
    return 0


def cmd_rollback_model(args, settings) -> int:
    from .registry import LocalRegistry
    entry = LocalRegistry(Path(args.artifact_dir or settings.artifact_dir)).rollback(args.kind, args.to_version, args.approved_by)
    if args.store == "sql" and not args.dry_run:
        with _db(settings).transaction() as cur:
            cur.execute("UPDATE ML.ModelRegistry SET Status = 'RETIRED' WHERE ModelKind = ? AND Status = 'APPROVED'", args.kind)
            cur.execute("UPDATE ML.ModelRegistry SET Status = 'APPROVED', ApprovedBy = ?, ApprovedAt = SYSUTCDATETIME() WHERE ModelVersion = ?", args.approved_by, args.to_version)
    print(json.dumps(entry, indent=2, default=str))
    return 0


FEEDBACK_COLUMNS = ["SnapshotKey", "ExceptionId", "ReviewOutcome", "OutcomeType", "ReviewedBy", "ReviewedAt", "CorrectedBuyAmount", "CorrectedSellAmount",
                    "ConfirmedFinancialImpact", "CurrencyCode", "Comments", "SamplingStratum", "SamplingProbability"]


def validate_feedback(df: pd.DataFrame) -> List[str]:
    from .contracts import NEGATIVE_OUTCOMES, POSITIVE_OUTCOMES
    problems = [f"missing column {c}" for c in ("SnapshotKey", "ReviewOutcome", "OutcomeType", "ReviewedBy", "ReviewedAt") if c not in df]
    if problems:
        return problems
    for i, r in df.iterrows():
        ok = ((r.ReviewOutcome == "CONFIRMED_EXCEPTION" and r.OutcomeType in POSITIVE_OUTCOMES) or
              (r.ReviewOutcome == "CONFIRMED_VALID" and r.OutcomeType in NEGATIVE_OUTCOMES) or
              (r.ReviewOutcome == "INSUFFICIENT_INFORMATION" and r.OutcomeType == "InsufficientInformation"))
        if not ok:
            problems.append(f"row {i}: inconsistent ReviewOutcome {r.ReviewOutcome!r} / OutcomeType {r.OutcomeType!r}")
    return problems


def cmd_ingest_feedback(args, settings) -> int:
    """Appends review outcomes as NEW OutcomeVersions (history is never overwritten)."""
    df = pd.read_csv(args.file)
    problems = validate_feedback(df)
    if problems:
        for p in problems:
            log.error(p)
        return 2
    df = df.reindex(columns=FEEDBACK_COLUMNS)
    if args.store != "sql" or args.dry_run:
        out = Path(args.output_dir or settings.output_dir) / "store"
        out.mkdir(parents=True, exist_ok=True)
        path = out / "ML.ReviewFeedback.csv"
        existing = pd.read_csv(path) if path.exists() else pd.DataFrame(columns=FEEDBACK_COLUMNS + ["OutcomeVersion", "IsCurrent"])
        rows = []
        for r in df.to_dict("records"):
            prior = existing[existing["SnapshotKey"] == r["SnapshotKey"]]
            existing.loc[prior.index, "IsCurrent"] = 0
            rows.append({**r, "OutcomeVersion": int(prior["OutcomeVersion"].max()) + 1 if len(prior) else 1, "IsCurrent": 1})
        pd.concat([existing, pd.DataFrame(rows)], ignore_index=True).to_csv(path, index=False)
        log.info("recorded %d outcomes locally in %s", len(rows), path)
        return 0
    with _db(settings).transaction() as cur:
        for r in df.to_dict("records"):
            cur.execute("SELECT ISNULL(MAX(OutcomeVersion), 0), MAX(CASE WHEN IsCurrent = 1 THEN FeedbackId END) FROM ML.ReviewFeedback WITH (UPDLOCK, HOLDLOCK) WHERE SnapshotKey = ?", r["SnapshotKey"])
            version, current_id = cur.fetchone()
            if current_id:
                cur.execute("UPDATE ML.ReviewFeedback SET IsCurrent = 0 WHERE FeedbackId = ?", current_id)
            cur.execute("""INSERT INTO ML.ReviewFeedback (ExceptionId, SnapshotKey, ReviewOutcome, OutcomeType, ReviewedBy, ReviewedAt, CorrectedBuyAmount,
                           CorrectedSellAmount, ConfirmedFinancialImpact, CurrencyCode, Comments, OutcomeVersion, SupersedesFeedbackId, IsCurrent,
                           SamplingStratum, SamplingProbability) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)""",
                        *[None if pd.isna(r.get(c)) else r.get(c) for c in ("ExceptionId", "SnapshotKey", "ReviewOutcome", "OutcomeType", "ReviewedBy")],
                        pd.Timestamp(r["ReviewedAt"]).to_pydatetime(),
                        *[None if pd.isna(r.get(c)) else r.get(c) for c in ("CorrectedBuyAmount", "CorrectedSellAmount", "ConfirmedFinancialImpact", "CurrencyCode", "Comments")],
                        int(version) + 1, current_id, None if pd.isna(r.get("SamplingStratum")) else r.get("SamplingStratum"),
                        None if pd.isna(r.get("SamplingProbability")) else r.get("SamplingProbability"))
    log.info("recorded %d outcomes in ML.ReviewFeedback", len(df))
    return 0


def cmd_sample(args, settings) -> int:
    """Stratified review sample (rule findings, AE candidates, model candidates, unflagged) with probabilities."""
    from .labels import review_sample
    audit = pd.read_csv(args.audit_csv)
    thr = float(DEFAULTS["AEPercentileThreshold"]) if args.ae_threshold is None else args.ae_threshold
    audit["IsAECandidate"] = audit["AEAnomalyPercentile"] >= thr
    audit["IsModelCandidate"] = audit.get("WeakFlagScore", pd.Series(0, index=audit.index)).fillna(0) >= args.model_threshold
    sample = review_sample(audit, args.per_stratum, seed=args.seed)
    cols = ["SnapshotKey", "SnapshotWeek", "Customer", "Carrier", "JobNumber", "ConsignmentNote", "AccountManager", "SamplingStratum", "SamplingProbability",
            "ReasonCodes", "AEAnomalyPercentile", "BuyNett", "SellNett"]
    sample = sample.reindex(columns=cols).assign(ReviewOutcome="", OutcomeType="", ReviewedBy="", ReviewedAt="", Comments="")
    sample.to_csv(args.output, index=False)
    log.info("wrote %d sampled rows to %s", len(sample), args.output)
    return 0


def cmd_monitor(args, settings) -> int:
    from .monitoring import mature_review_precision
    from .store import LocalStore
    root = Path(args.output_dir or settings.output_dir) / "store"
    if args.store == "sql":
        db = _db(settings)
        exc = db.read_df("SELECT SnapshotKey, DetectionRoute, PublishedAt FROM ML.DetectedException WHERE PublishedAt IS NOT NULL AND IsSynthetic = 0")
        fb = db.read_df("SELECT SnapshotKey, ReviewOutcome, IsCurrent FROM ML.ReviewFeedback")
        runs = db.read_df("SELECT TOP 10 RunId, RunType, ScoringWeek, Status, CompletedAt FROM ML.Run ORDER BY StartedAt DESC")
    else:
        store = LocalStore(root)
        exc = store.exceptions()
        fb_path = root / "ML.ReviewFeedback.csv"
        fb = pd.read_csv(fb_path) if fb_path.exists() else pd.DataFrame(columns=["SnapshotKey", "ReviewOutcome", "IsCurrent"])
        runs = pd.DataFrame(store._runs().values())
    report = {"mature_review_precision": mature_review_precision(exc, fb, pd.Timestamp.utcnow().tz_localize(None), args.maturity_days),
              "recent_runs": runs.tail(10).to_dict("records") if not runs.empty else []}
    print(json.dumps(report, indent=2, default=str))
    return 0


# ---------------------------------------------------------------- parser
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="ivt-ml", description="IVT hybrid anomaly detection (shadow-first).")
    sub = p.add_subparsers(dest="command", required=True)

    def common(sp, store=True):
        sp.add_argument("--output-dir")
        sp.add_argument("--artifact-dir")
        sp.add_argument("--config-version")
        sp.add_argument("--config-file", help="JSON of GLOBAL parameter overrides for local experiments")
        if store:
            sp.add_argument("--store", choices=["local", "sql"], default="local")
            sp.add_argument("--dry-run", action="store_true", help="Never write to SQL Server (forces the local store)")
        sp.add_argument("--synthetic", action="store_true", help="Use the synthetic fixture instead of IVT (results are marked synthetic)")

    s = sub.add_parser("profile"); s.add_argument("--recent-weeks", type=int, default=8); s.add_argument("--output-dir"); s.set_defaults(func=cmd_profile)
    s = sub.add_parser("migrate"); s.add_argument("--apply", action="store_true"); s.add_argument("--i-am-authorised", action="store_true"); s.set_defaults(func=cmd_migrate)
    s = sub.add_parser("seed-config"); s.add_argument("--config-version", default="dev-defaults"); s.add_argument("--output"); s.set_defaults(func=cmd_seed_config)
    s = sub.add_parser("snapshot"); common(s); s.add_argument("--week", "--current-week", dest="week"); s.set_defaults(func=cmd_snapshot)
    s = sub.add_parser("labels"); common(s); s.add_argument("--as-of"); s.add_argument("--weeks", type=int, default=26); s.set_defaults(func=cmd_labels)
    s = sub.add_parser("features"); common(s); s.add_argument("--week", required=True); s.set_defaults(func=cmd_features)
    s = sub.add_parser("train"); common(s); s.add_argument("--as-of"); s.add_argument("--feature-weeks", type=int, default=26)
    s.add_argument("--skip-contamination-check", action="store_true"); s.set_defaults(func=cmd_train)
    s = sub.add_parser("score"); common(s); s.add_argument("--week", "--current-week", dest="week"); s.add_argument("--allow-candidate", action="store_true")
    s.add_argument("--publish", action="store_true"); s.add_argument("--shadow", action="store_true", help="Documentation alias: shadow is controlled by ShadowMode"); s.set_defaults(func=cmd_score)
    s = sub.add_parser("backtest"); common(s); s.add_argument("--start", required=True); s.add_argument("--end", required=True); s.set_defaults(func=cmd_backtest)
    s = sub.add_parser("publish"); common(s); s.add_argument("--run-id", required=True); s.set_defaults(func=cmd_publish)
    s = sub.add_parser("rollback-publication"); common(s); s.add_argument("--run-id", required=True); s.add_argument("--reason", required=True); s.set_defaults(func=cmd_rollback_publication)
    s = sub.add_parser("approve-model"); common(s); s.add_argument("--model-version", required=True); s.add_argument("--approved-by", required=True)
    s.add_argument("--evidence", required=True, help="Reference to the validation evidence (report path / ticket)"); s.set_defaults(func=cmd_approve_model)
    s = sub.add_parser("rollback-model"); common(s); s.add_argument("--kind", required=True); s.add_argument("--to-version", required=True)
    s.add_argument("--approved-by", required=True); s.set_defaults(func=cmd_rollback_model)
    s = sub.add_parser("ingest-feedback"); common(s); s.add_argument("--file", required=True); s.set_defaults(func=cmd_ingest_feedback)
    s = sub.add_parser("sample"); s.add_argument("--audit-csv", required=True); s.add_argument("--output", required=True); s.add_argument("--per-stratum", type=int, default=50)
    s.add_argument("--ae-threshold", type=float); s.add_argument("--model-threshold", type=float, default=0.5); s.add_argument("--seed", type=int, default=7); s.set_defaults(func=cmd_sample)
    s = sub.add_parser("monitor"); common(s); s.add_argument("--maturity-days", type=int, default=28); s.set_defaults(func=cmd_monitor)
    return p


def main(argv: Optional[List[str]] = None) -> int:
    settings = Settings.from_env()
    logging.basicConfig(level=settings.log_level, format='{"time":"%(asctime)s","level":"%(levelname)s","logger":"%(name)s","message":"%(message)s"}')
    args = build_parser().parse_args(argv)
    return args.func(args, settings)


if __name__ == "__main__":
    sys.exit(main())
