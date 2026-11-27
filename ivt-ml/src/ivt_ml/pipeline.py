"""End-to-end orchestration shared by the CLI, scripts and integration tests.

Weekly scoring order (docs/operations_runbook.md): verify source -> start run + config snapshot -> snapshot ->
validate/reconcile -> baselines -> features -> load approved artifacts -> score -> explain -> gates & caps ->
rule overlap -> stage -> publish atomically -> complete run + monitoring summary.
"""
from __future__ import annotations

import hashlib
import json
import logging
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

import joblib
import numpy as np
import pandas as pd

from .autoencoder import (AutoencoderModel, contamination_sensitivity, dependency_versions, peer_deviation_score, train_autoencoder,
                          train_isolation_forest)
from .calibration import assign_partitions, calibrate, evaluate, select_threshold, temporal_partitions
from .classifier import AE_FEATURES, ClassifierModel, add_ae_columns, out_of_sample_ae_scores, train_classifier
from .config import EffectiveConfig
from .contracts import KEY_VERSION
from .features import CATEGORICAL, FEATURE_VERSION, build_features, model_feature_names, peer_frame, validate_feature_frame
from .labels import build_label_events, training_labels, weak_hold_targets
from .monitoring import data_quality_gate, run_summary
from .preprocessing import PREPROCESSING_VERSION
from .publication import decide, exception_rows
from .reconciliation import ReconciliationResult, reconcile
from .registry import LocalRegistry
from .store import Store, utcnow

log = logging.getLogger("ivt_ml")


# ---------------------------------------------------------------- sources
class SourceProtocol:
    """What the pipeline needs from a data source (IVT via SQL, or the synthetic fixture)."""
    is_synthetic: bool = False
    def current_week(self): ...
    def history_weeks(self, start, end) -> list: ...
    def lines(self, side: str, week, kind: str) -> pd.DataFrame: ...
    def reference(self): ...
    def moveit_for_current_week(self) -> pd.DataFrame: ...
    def holds(self, start, end) -> pd.DataFrame: ...
    def releases(self, start, end) -> pd.DataFrame: ...
    def rule_history(self, start, end) -> pd.DataFrame: ...
    def current_rule_results(self) -> pd.DataFrame: ...
    def outcome_text_map(self) -> pd.DataFrame: ...
    def review_feedback(self) -> pd.DataFrame: ...


class SyntheticSource(SourceProtocol):
    is_synthetic = True

    def __init__(self, fixture: Dict):
        self.f = fixture

    def current_week(self):
        return self.f["weeks"][-1].date()

    def history_weeks(self, start, end):
        return [w.date() for w in self.f["weeks"][:-1] if pd.Timestamp(start) <= w <= pd.Timestamp(end)]

    def lines(self, side, week, kind):
        df = self.f["sell" if side == "SELL" else "buy"]
        return df[df["SnapshotWeek"] == pd.Timestamp(week)].copy()

    def reference(self):
        from .source_adapters import ReferenceData
        return ReferenceData(self.f["customer_groups"], self.f["carrier_groups"], self.f["routing"], self.f["currency_map"])

    def moveit_for_current_week(self):
        return self.f["moveit"]

    def holds(self, start, end):
        return self.f["holds"]

    def releases(self, start, end):
        return self.f["releases"]

    def rule_history(self, start, end):
        return pd.DataFrame()

    def current_rule_results(self):
        return self.f["rules"]

    def outcome_text_map(self):
        return self.f["text_map"]

    def review_feedback(self):
        return self.f.get("feedback", pd.DataFrame())


# ---------------------------------------------------------------- context
@dataclass
class Context:
    source: SourceProtocol
    store: Store
    cfg: EffectiveConfig
    artifact_dir: Path
    cache_dir: Path
    seed: int = 42
    reference: Optional[object] = None
    _snapshots: Dict[pd.Timestamp, ReconciliationResult] = field(default_factory=dict)   # current week only
    _peers: Dict[pd.Timestamp, pd.DataFrame] = field(default_factory=dict)               # slim history per week
    _keys: Dict[pd.Timestamp, pd.DataFrame] = field(default_factory=dict)                # snapshot keys per week

    @property
    def registry(self) -> LocalRegistry:
        return LocalRegistry(self.artifact_dir)

    def ref(self):
        if self.reference is None:
            self.reference = self.source.reference()
        return self.reference


def new_run_id(run_type: str, week=None) -> str:
    stamp = utcnow().strftime("%Y%m%dT%H%M%S")
    return f"{run_type.lower()}-{pd.Timestamp(week):%Y%m%d}-{stamp}-{uuid.uuid4().hex[:6]}" if week is not None else f"{run_type.lower()}-{stamp}-{uuid.uuid4().hex[:6]}"


# ---------------------------------------------------------------- snapshots
def snapshot_week(ctx: Context, week, *, kind: str) -> ReconciliationResult:
    """Canonical snapshot for one week (cached on disk; historical weeks are immutable once captured)."""
    week = pd.Timestamp(week).normalize()
    if week in ctx._snapshots:
        return ctx._snapshots[week]
    cache = ctx.cache_dir / "snapshots" / f"{week:%Y-%m-%d}_{kind}_{KEY_VERSION}{'_synthetic' if ctx.source.is_synthetic else ''}.joblib"
    if kind == "history" and cache.exists():
        result = joblib.load(cache)
    else:
        ref = ctx.ref()
        sell = ctx.source.lines("SELL", week.date(), kind)
        buy = ctx.source.lines("BUY", week.date(), kind)
        moveit = ctx.source.moveit_for_current_week() if kind == "current" else None
        result = reconcile(sell, buy, source_system="SYNTHETIC" if ctx.source.is_synthetic else ("CURRENT_TRADING" if kind == "current" else "BUY_SELL_HISTORY"),
                           customer_groups=ref.customer_groups, carrier_groups=ref.carrier_groups, routing=ref.routing,
                           currency_map=ref.currency_map, moveit=moveit, is_synthetic=ctx.source.is_synthetic)
        if kind == "history":
            cache.parent.mkdir(parents=True, exist_ok=True)
            joblib.dump(result, cache, compress=3)
    if kind == "current":
        ctx._snapshots[week] = result
    # Only slim derivatives of history stay in memory; full history snapshots are re-read from the disk cache.
    ctx._peers.setdefault(week, peer_frame(result.snapshots))
    ctx._keys.setdefault(week, result.snapshots[KEY_COLUMNS].copy())
    log.info("snapshot %s (%s): %s rows, totals %s", week.date(), kind, len(result.snapshots), {k: round(v, 2) for k, v in result.totals.items()})
    return result


KEY_COLUMNS = ["SnapshotKey", "TransactionKey", "JobNumber", "SnapshotWeek", "BuySellMatchStatus"]


def peer_history(ctx: Context, weeks: List[pd.Timestamp]) -> pd.DataFrame:
    """Slim, memory-light history (metrics + grouping keys) used for baselines and rarity counts."""
    frames = []
    for w in weeks:
        w = pd.Timestamp(w).normalize()
        if w not in ctx._peers:
            snapshot_week(ctx, w, kind="history")
        frames.append(ctx._peers[w])
    return pd.concat(frames, ignore_index=True) if frames else pd.DataFrame()


def history_frame(ctx: Context, weeks: List[pd.Timestamp]) -> pd.DataFrame:
    """Snapshot keys for label matching (full snapshots are not kept in memory)."""
    frames = []
    for w in weeks:
        w = pd.Timestamp(w).normalize()
        if w not in ctx._keys:
            snapshot_week(ctx, w, kind="history")
        frames.append(ctx._keys[w])
    return pd.concat(frames, ignore_index=True) if frames else pd.DataFrame(columns=KEY_COLUMNS)


def features_for_week(ctx: Context, week: pd.Timestamp, snap: pd.DataFrame, all_weeks: List[pd.Timestamp]) -> pd.DataFrame:
    lookback = int(ctx.cfg.get("PeerLookbackWeeks"))
    week = pd.Timestamp(week).normalize()
    prior = [w for w in all_weeks if week - pd.Timedelta(weeks=lookback) <= w < week]
    feats, _ = build_features(snap, peer_history(ctx, prior), scoring_week=week, lookback_weeks=lookback,
                              min_peer_count=int(ctx.cfg.get("MinimumPeerCount")))
    return feats


# ---------------------------------------------------------------- training
def train(ctx: Context, *, as_of, feature_weeks: int, output_dir: Path, run_id: Optional[str] = None, contamination_check: bool = True) -> Dict:
    """Builds point-in-time features for `feature_weeks` weeks ending before `as_of`, then:
    benchmarks -> chronological OOF AE scores -> classifier (confirmed if enough confirmed labels, else weak
    experimental) -> separate-period calibration and threshold -> one evaluation on the untouched test period.
    Registers CANDIDATE models only."""
    as_of = pd.Timestamp(as_of).normalize()
    lookback = int(ctx.cfg.get("PeerLookbackWeeks"))
    run_id = run_id or new_run_id("TRAIN", as_of)
    ctx.store.start_run({"RunId": run_id, "RunType": "TRAIN", "ScoringWeek": as_of.date(), "ConfigVersion": ctx.cfg.config_version,
                         "ConfigSnapshotJson": ctx.cfg.snapshot_json(), "ConfigChecksum": ctx.cfg.checksum(), "FeatureVersion": FEATURE_VERSION,
                         "KeyVersion": KEY_VERSION, "IsShadow": 1})
    try:
        all_weeks = [pd.Timestamp(w) for w in ctx.source.history_weeks((as_of - pd.Timedelta(weeks=lookback + feature_weeks + 1)).date(), (as_of - pd.Timedelta(days=1)).date())]
        fweeks = all_weeks[-feature_weeks:]
        log.info("training: %d history weeks, %d feature weeks (%s .. %s)", len(all_weeks), len(fweeks), fweeks[0].date(), fweeks[-1].date())
        use_moveit = int(ctx.cfg.get("UseMoveitFeaturesForTraining")) == 1
        needed = set(KEY_COLUMNS) | {"Segment"} | set(model_feature_names(use_moveit=use_moveit, purpose="ae")) | \
            set(model_feature_names(use_moveit=use_moveit, purpose="classifier"))
        frames: Dict[pd.Timestamp, pd.DataFrame] = {}
        for w in fweeks:
            snap = snapshot_week(ctx, w, kind="history").snapshots
            f = features_for_week(ctx, w, snap, all_weeks)
            keep = f[[c for c in f.columns if c in needed]].copy()
            for c in keep.select_dtypes("float64").columns:
                keep[c] = keep[c].astype("float32")
            frames[w] = keep
        data = pd.concat(frames.values())
        data.index = data["SnapshotKey"]
        for w in frames:
            frames[w].index = frames[w]["SnapshotKey"]

        use_moveit = int(ctx.cfg.get("UseMoveitFeaturesForTraining")) == 1
        ae_feats = model_feature_names(use_moveit=use_moveit, purpose="ae")
        clf_num = [n for n in model_feature_names(use_moveit=use_moveit, purpose="classifier") if n not in CATEGORICAL] + AE_FEATURES
        problems = validate_feature_frame(data, ae_feats + [n for n in clf_num if n not in AE_FEATURES])
        if problems:
            raise RuntimeError("Feature contract violation: " + "; ".join(problems))

        # Labels (point-in-time) -------------------------------------------------
        snaps_all = data[["SnapshotKey", "TransactionKey", "JobNumber", "SnapshotWeek", "BuySellMatchStatus"]].reset_index(drop=True)
        start, end = fweeks[0].date(), as_of.date()
        events = build_label_events(snaps_all, holds=ctx.source.holds(start, end), releases=ctx.source.releases(start, end),
                                    rule_history=ctx.source.rule_history(start, end), feedback=ctx.source.review_feedback(),
                                    text_map=ctx.source.outcome_text_map())
        label_build = training_labels(events, snaps_all, as_of, f"ls-{as_of:%Y%m%d}")
        ctx.store.write("ML.LabelEvent", events.assign(RunId=run_id))
        ctx.store.write("ML.TrainingLabel", label_build.labels.assign(RunId=run_id))

        parts = temporal_partitions(fweeks, gap_weeks=1)
        data["Partition"] = assign_partitions(data.reset_index(drop=True), parts).values
        report: Dict = {"run_id": run_id, "as_of": str(as_of.date()), "synthetic": ctx.source.is_synthetic,
                        "partitions": {k: [str(w.date()) for w in v] for k, v in parts.items()}, "labels": label_build.report,
                        "rows": int(len(data)), "feature_version": FEATURE_VERSION, "preprocessing_version": PREPROCESSING_VERSION}

        confirmed = label_build.labels[label_build.labels["LabelQuality"] == "CONFIRMED"] if not label_build.labels.empty else pd.DataFrame()
        min_confirmed = int(ctx.cfg.get("MinimumReviewedValidationSample"))
        use_confirmed = len(confirmed) >= min_confirmed and confirmed["Label"].nunique() == 2
        if use_confirmed:
            target = data["SnapshotKey"].map(confirmed.set_index("SnapshotKey")["Label"]).astype("Int64")
            kind = "CLASSIFIER_CONFIRMED"
        else:
            target = pd.Series(weak_hold_targets(data.reset_index(drop=True), events, as_of).values, index=data.index, dtype="Int64")
            kind = "CLASSIFIER_WEAK_EXPERIMENTAL"
        report["classifier_kind"] = kind
        report["classifier_target_note"] = ("Confirmed review outcomes." if use_confirmed else
                                            f"Only {len(confirmed)} confirmed labels (< {min_confirmed}); trained the EXPERIMENTAL weak-label model, "
                                            "which predicts historical manual-hold behaviour and cannot provide confirmed-exception probabilities.")
        # Normal-reference exclusion: confirmed positives and weak hold positives do not define "normal".
        exclude = (target == 1).fillna(False).astype(bool)

        # Benchmarks on the selection partition (weak or confirmed target, as labelled) ---------
        sel = data["Partition"] == "selection"
        train_part = data["Partition"] == "train"
        ae_bench = train_autoencoder(data[train_part], ae_feats, seed=ctx.seed, exclude_mask=exclude)
        iso = train_isolation_forest(data[train_part & ~exclude], ae_feats, seed=ctx.seed)
        y_sel = target[sel]
        mask = y_sel.notna()
        bench = {}
        if mask.sum() and y_sel[mask].nunique() == 2:
            ys = y_sel[mask].astype(int).to_numpy()
            sd = data[sel][mask.values]
            cap = max(1, int(round(len(sd) * 0.005)))
            bench = {"target": "confirmed" if use_confirmed else "weak hold behaviour (NOT confirmed exceptions)",
                     "autoencoder": evaluate(ae_bench.score(sd)["AEReconstructionError"].to_numpy(), ys, capacity=cap),
                     "peer_deviation": evaluate(peer_deviation_score(sd).fillna(0).to_numpy(), ys, capacity=cap),
                     "isolation_forest": evaluate(iso.score(sd).to_numpy(), ys, capacity=cap)}
            best = max(("autoencoder", "peer_deviation", "isolation_forest"), key=lambda k: bench[k].get("pr_auc", -1))
            bench["best_by_pr_auc"] = best
            bench["ae_promotion_supported"] = best == "autoencoder"
        report["benchmarks"] = bench
        if contamination_check and exclude.any():
            report["contamination_sensitivity"] = contamination_sensitivity(data[train_part], ae_feats, exclude[train_part], data[sel].head(5000), seed=ctx.seed)

        # Chronological out-of-sample AE features for the classifier ---------------------------
        oof = out_of_sample_ae_scores(frames, ae_feats, min_train_weeks=max(4, len(parts["train"]) // 2), block_weeks=2, gap_weeks=1,
                                      exclude=exclude, seed=ctx.seed, log=log.info)
        data = data.join(oof.drop(columns=["OOFTrainEnd"]), how="left")
        clf_data = add_ae_columns(data, data[["AEReconstructionError", "AEAnomalyPercentile", "FinancialReconstructionError",
                                              "MeasurementReconstructionError", "ReconciliationReconstructionError"]])
        has_oof = clf_data["AEAnomalyPercentile"].notna()
        labelled = target.notna() & has_oof
        tr = labelled & (data["Partition"] == "train")
        cl = labelled & (data["Partition"] == "calibration")
        te = labelled & (data["Partition"] == "test")
        best_c, best_ap, clf = None, -1.0, None
        for C in (0.1, 0.5, 2.0):
            candidate = train_classifier(clf_data[tr], target[tr], kind=kind, numeric=clf_num, categorical=CATEGORICAL, C=C, seed=ctx.seed)
            s_mask = labelled & sel
            if s_mask.sum() and target[s_mask].nunique() == 2:
                ap = evaluate(candidate.raw_score(clf_data[s_mask]), target[s_mask].astype(int).to_numpy()).get("pr_auc", -1)
            else:
                ap = -1
            if ap > best_ap:
                best_c, best_ap, clf = C, ap, candidate
        report["classifier_selection"] = {"C": best_c, "selection_pr_auc": best_ap}
        cal_info = calibrate(clf, clf_data[cl], target[cl], int(ctx.cfg.get("MinimumCalibrationSample")))
        report["calibration"] = cal_info or "insufficient calibration sample"
        threshold = None
        if kind == "CLASSIFIER_CONFIRMED" and cal_info:
            probs = clf.calibrator.predict_proba(_mx(clf, clf_data[cl]))[:, 1]
            threshold = select_threshold(probs, target[cl].astype(int).to_numpy(), min_lower_bound=float(ctx.cfg.get("MinimumPrecisionLowerBound")),
                                         min_selected=int(ctx.cfg.get("MinimumCalibrationSample")) // 10)
        report["threshold_selection"] = threshold or ("not applicable: weak-label model never publishes" if kind != "CLASSIFIER_CONFIRMED" else "no threshold met the precision lower bound")
        if te.sum() and target[te].nunique() == 2:
            scores = clf.calibrator.predict_proba(_mx(clf, clf_data[te]))[:, 1] if clf.calibrator is not None else clf.raw_score(clf_data[te])
            report["final_test"] = evaluate(scores, target[te].astype(int).to_numpy(), capacity=max(1, int(te.sum() * 0.005)),
                                            threshold=threshold["threshold"] if isinstance(threshold, dict) else None)
            report["final_test"]["target"] = "confirmed outcomes" if use_confirmed else "weak hold behaviour (not confirmed exceptions)"

        # Final AE on all weeks except the untouched test period, then one check on test ----------
        final_mask = data["Partition"].isin(["train", "selection", "calibration"])
        final_ae = train_autoencoder(data[final_mask], ae_feats, seed=ctx.seed, exclude_mask=exclude)
        test_rows = data[data["Partition"] == "test"]
        if len(test_rows):
            pct = final_ae.score(test_rows)["AEAnomalyPercentile"]
            report["final_ae_test_percentile_check"] = {"share_ge_99_5": float((pct >= 99.5).mean()), "expected_if_stable": 0.005,
                                                        "note": "Checks that frozen reference percentiles transfer to later data."}

        versions = _register(ctx, run_id, final_ae, clf, kind, report, fweeks, label_build, use_confirmed and threshold is not None, threshold)
        report["registered"] = versions
        output_dir.mkdir(parents=True, exist_ok=True)
        (output_dir / f"training_report_{run_id}.json").write_text(json.dumps(report, indent=2, default=_json_default), encoding="utf-8")
        ctx.store.finish_run(run_id, "SUCCEEDED", {"registered": versions})
        return report
    except Exception as exc:
        ctx.store.finish_run(run_id, "FAILED", error=str(exc))
        raise


def _mx(clf: ClassifierModel, frame: pd.DataFrame):
    from .classifier import prepare_matrix
    return prepare_matrix(frame, clf.numeric, clf.categorical)


def _register(ctx: Context, run_id: str, ae: AutoencoderModel, clf: ClassifierModel, kind: str, report: Dict, weeks, label_build,
              calibrated_with_threshold: bool, threshold) -> Dict[str, str]:
    stamp = utcnow().strftime("%Y%m%d%H%M%S")
    syn = ctx.source.is_synthetic
    ae_version = f"ae-{FEATURE_VERSION}-{stamp}{'-synthetic' if syn else ''}"
    clf_version = f"{'clfc' if kind == 'CLASSIFIER_CONFIRMED' else 'clfw'}-{FEATURE_VERSION}-{stamp}{'-synthetic' if syn else ''}"
    ae_art = ae.save(ctx.artifact_dir / ae_version)
    clf.meta["threshold"] = threshold
    clf.meta["ae_model_version"] = ae_version
    clf_art = clf.save(ctx.artifact_dir / clf_version)
    limitations = []
    if syn:
        limitations.append("Trained on SYNTHETIC data only; not valid for IVT.")
    if kind != "CLASSIFIER_CONFIRMED":
        limitations.append(report["classifier_target_note"])
    if not report.get("benchmarks", {}).get("ae_promotion_supported", False):
        limitations.append("Autoencoder did not outperform the simpler benchmarks on the selection period; keep it in shadow.")
    common = {"FeatureVersion": FEATURE_VERSION, "PreprocessingVersion": PREPROCESSING_VERSION, "TrainingWindowStart": str(weeks[0].date()),
              "TrainingWindowEnd": str(weeks[-1].date()), "DependenciesJson": json.dumps(dependency_versions()), "IsSyntheticOnly": syn,
              "TrainedAt": utcnow().isoformat(), "TrainRunId": run_id, "LimitationsText": " ".join(limitations) or None}
    entries = [
        {**common, "ModelVersion": ae_version, "ModelKind": "AUTOENCODER", "ArtifactUri": ae_art["uri"], "ArtifactSha256": ae_art["sha256"],
         "MetricsJson": json.dumps({"meta": ae.meta, "benchmarks": report.get("benchmarks")}, default=_json_default),
         "ProvidesCalibratedProbability": False, "LabelSetVersion": None, "ParentModelVersion": None},
        {**common, "ModelVersion": clf_version, "ModelKind": kind, "ArtifactUri": clf_art["uri"], "ArtifactSha256": clf_art["sha256"],
         "MetricsJson": json.dumps({k: report.get(k) for k in ("classifier_selection", "calibration", "threshold_selection", "final_test")}, default=_json_default),
         "ProvidesCalibratedProbability": bool(calibrated_with_threshold and kind == "CLASSIFIER_CONFIRMED"),
         "LabelSetVersion": label_build.labels["LabelSetVersion"].iloc[0] if not label_build.labels.empty else None, "ParentModelVersion": ae_version},
    ]
    for e in entries:
        ctx.registry.register(e)
        ctx.store.write("ML.ModelRegistry", pd.DataFrame([{**e, "Status": "CANDIDATE"}]))
    return {"autoencoder": ae_version, "classifier": clf_version}


# ---------------------------------------------------------------- scoring
def score_week(ctx: Context, *, week=None, output_dir: Path, run_id: Optional[str] = None, allow_candidate: bool = False,
               publish: bool = False, fail_after_staging: bool = False) -> Dict:
    """Scores one week end to end. With publish=False results are staged (or written to the dry-run store) but not
    activated. Any failure discards staged rows; earlier active results are untouched."""
    current = pd.Timestamp(ctx.source.current_week()).normalize()
    week = pd.Timestamp(week).normalize() if week is not None else current
    kind = "current" if week == current else "history"
    lookback = int(ctx.cfg.get("PeerLookbackWeeks"))
    ae_entry = ctx.registry.current("AUTOENCODER", allow_candidate=allow_candidate)
    if ae_entry is None:
        raise RuntimeError("No approved autoencoder (use --allow-candidate for shadow evaluation of a candidate).")
    clf_entry = (ctx.registry.current("CLASSIFIER_CONFIRMED", allow_candidate=allow_candidate)
                 or ctx.registry.current("CLASSIFIER_WEAK_EXPERIMENTAL", allow_candidate=True))
    run_id = run_id or new_run_id("SCORE", week)
    ctx.store.start_run({"RunId": run_id, "RunType": "SCORE", "ScoringWeek": week.date(), "ConfigVersion": ctx.cfg.config_version,
                         "ConfigSnapshotJson": ctx.cfg.snapshot_json(), "ConfigChecksum": ctx.cfg.checksum(), "ModelVersion": ae_entry["ModelVersion"],
                         "FeatureVersion": FEATURE_VERSION, "KeyVersion": KEY_VERSION, "IsShadow": int(ctx.cfg.get("ShadowMode"))})
    try:
        recon = snapshot_week(ctx, week, kind=kind)
        snap = recon.snapshots
        quality = data_quality_gate(recon.issues, snap, float(ctx.cfg.get("DataQualityFailureTolerance")))
        all_weeks = [pd.Timestamp(w) for w in ctx.source.history_weeks((week - pd.Timedelta(weeks=lookback + 1)).date(), (week - pd.Timedelta(days=1)).date())]
        feats = features_for_week(ctx, week, snap, all_weeks)
        ae = AutoencoderModel.load(ctx.registry.artifact_path(ae_entry), ae_entry["ArtifactSha256"])
        if FEATURE_VERSION != ae_entry["FeatureVersion"]:
            raise RuntimeError(f"Feature version {FEATURE_VERSION} does not match model {ae_entry['FeatureVersion']}")
        problems = validate_feature_frame(feats, ae.features)
        if problems:
            raise RuntimeError("Feature contract violation: " + "; ".join(problems))
        max_age = int(ctx.cfg.get("MaximumModelAgeDays"))
        model_age = ctx.registry.age_days(ae_entry)
        scored = pd.concat([feats.reset_index(drop=True), ae.score(feats).reset_index(drop=True)], axis=1)
        scored["ConfirmedExceptionProbability"], scored["WeakFlagScore"], contributions = np.nan, np.nan, None
        confirmed_ready = False
        if clf_entry is not None:
            clf = ClassifierModel.load(ctx.registry.artifact_path(clf_entry), clf_entry["ArtifactSha256"])
            clf_frame = add_ae_columns(scored, scored)
            if clf.kind == "CLASSIFIER_CONFIRMED":
                prob = clf.calibrated_probability(clf_frame)
                if prob is not None and clf_entry.get("ProvidesCalibratedProbability") and clf_entry["Status"] == "APPROVED":
                    scored["ConfirmedExceptionProbability"] = prob
                    confirmed_ready = model_age <= max_age and isinstance(clf.meta.get("threshold"), dict)
                    contributions = clf.contributions(clf_frame)
            else:
                scored["WeakFlagScore"] = clf.calibrator.predict_proba(_mx(clf, clf_frame))[:, 1] if clf.calibrator is not None else clf.raw_score(clf_frame)
        ae_ok = ae_entry["Status"] == "APPROVED" and model_age <= max_age
        audit = decide(scored, ctx.cfg, run_quality_ok=quality["ok"], confirmed_model_ready=confirmed_ready,
                       model_versions={"ae": ae_entry["ModelVersion"], "classifier": clf_entry["ModelVersion"] if clf_entry else None},
                       rules=ctx.source.current_rule_results() if kind == "current" else None, already_active=ctx.store.active_publication_keys(),
                       contributions=contributions)
        if not ae_ok:
            # Candidate/expired models may be evaluated, but never publish.
            pub = audit["Decision"] == "PUBLISHED"
            audit.loc[pub, "Decision"], audit.loc[pub, "DecisionReasonCodes"] = "SHADOW_ONLY", "MODEL_NOT_APPROVED_OR_EXPIRED"
        audit = mark_review_rows(audit, ctx.cfg, seed=ctx.seed)
        scored_at = utcnow()
        audit_rows = audit.assign(RunId=run_id, ScoringWeek=week.date(), ModelVersion=ae_entry["ModelVersion"],
                                  ClassifierVersion=clf_entry["ModelVersion"] if clf_entry else None, FeatureVersion=FEATURE_VERSION,
                                  ConfigVersion=ctx.cfg.config_version, ScoredAt=scored_at)
        # Persist snapshot lineage, quality issues, features and audit before staging publications.
        existing = ctx.store.existing_snapshot_keys(week)
        new_snaps = ~snap["SnapshotKey"].isin(existing)
        ctx.store.write("ML.TradingSnapshot", snap[new_snaps].assign(RunId=run_id, SnapshotCapturedAt=scored_at))
        ctx.store.write("ML.SourceRowMap", recon.source_rows[recon.source_rows["SnapshotKey"].isin(set(snap.loc[new_snaps, "SnapshotKey"]))]
                        if len(recon.source_rows) else recon.source_rows)
        ctx.store.write("ML.DataQualityIssue", pd.DataFrame([{**i, "RunId": run_id} for i in recon.issues]))
        # Full feature vectors are kept for review rows (the ones that can become training labels).
        review_keys = set(audit.loc[audit["IsReviewCandidate"], "SnapshotKey"]) - ctx.store.existing_feature_keys(week, FEATURE_VERSION)
        ctx.store.write("ML.FeatureSnapshot", feature_snapshot_rows(feats[feats["SnapshotKey"].isin(review_keys)], run_id))
        ctx.store.write("ML.ScoreAudit", audit_rows)
        exceptions = exception_rows(audit, run_id=run_id, model_version=ae_entry["ModelVersion"], feature_version=FEATURE_VERSION,
                                    config_version=ctx.cfg.config_version, scored_at=scored_at, already_active=ctx.store.active_publication_keys())
        staged = ctx.store.stage_exceptions(run_id, exceptions) if len(exceptions) else 0
        if fail_after_staging:
            raise RuntimeError("Injected failure after staging (test hook)")
        summary = run_summary(snapshots=snap, features=feats, audit=audit, issues=recon.issues, totals=recon.totals,
                              ae_threshold=float(ctx.cfg.get("AEPercentileThreshold")), model_age_days=model_age)
        summary.update(quality=quality, staged=staged, synthetic=ctx.source.is_synthetic, classifier_kind=clf_entry["ModelKind"] if clf_entry else None)
        ctx.store.finish_run(run_id, "SUCCEEDED", summary)
        if publish and staged:
            ctx.store.activate(run_id)
        output_dir.mkdir(parents=True, exist_ok=True)
        audit_rows.drop(columns=[c for c in audit_rows.columns if c in ("EvidenceJson",)]).to_csv(output_dir / f"score_audit_{run_id}.csv", index=False)
        exceptions.to_csv(output_dir / f"exceptions_{run_id}.csv", index=False)
        (output_dir / f"run_summary_{run_id}.json").write_text(json.dumps(summary, indent=2, default=_json_default), encoding="utf-8")
        return {"run_id": run_id, "summary": summary, "audit": audit_rows, "exceptions": exceptions}
    except Exception as exc:
        ctx.store.discard(run_id, str(exc))
        raise


REVIEW_TEXT_COLUMNS = ["PrimaryReasonCode", "ReasonSummary", "ExplanationText", "RecommendedAction", "EvidenceJson"]


def mark_review_rows(audit: pd.DataFrame, cfg: EffectiveConfig, seed: int = 42) -> pd.DataFrame:
    """Selects rows for the ML Findings review page.

    * Candidates: a detection route qualified, or AE percentile >= AEPercentileThreshold (standard segment only),
      capped at ReviewCandidateCap by ranking (calibrated probability, else AE percentile). SamplingProbability = 1.
    * Random check: ReviewRandomSampleSize uniformly sampled from the remaining standard rows, with
      SamplingProbability = n / N, so precision/recall can be re-weighted and misses can be found.
    Explanations are kept only for review rows.
    """
    out = audit.copy()
    standard = (out["Segment"] == "STANDARD") & (out["BuySellMatchStatus"] != "AMBIGUOUS")
    ae = pd.to_numeric(out["AEAnomalyPercentile"], errors="coerce")
    is_candidate = standard & (out["CandidateRoute"].notna() | (ae >= float(cfg.get("AEPercentileThreshold"))))
    # Rank: calibrated probability when available; otherwise more independent evidence first, then unusualness.
    prob = pd.to_numeric(out["ConfirmedExceptionProbability"], errors="coerce")
    order = pd.DataFrame({"prob": prob.fillna(-1), "evidence": pd.to_numeric(out["EvidenceCount"], errors="coerce").fillna(0), "ae": ae.fillna(0),
                          "reason": out["PrimaryReasonCode"].fillna("~")})[is_candidate].sort_values(["prob", "evidence", "ae"], ascending=False)
    cap = int(cfg.get("ReviewCandidateCap"))
    per_reason = max(1, int(cap * float(cfg.get("ReviewMaxShareSameReason"))))
    taken, chosen = {}, []
    for idx, reason in order["reason"].items():  # diversity: no single reason fills the whole review list
        if len(chosen) >= cap:
            break
        if taken.get(reason, 0) >= per_reason:
            continue
        taken[reason] = taken.get(reason, 0) + 1
        chosen.append(idx)
    chosen = pd.Index(chosen)
    out["IsReviewCandidate"] = False
    out["SamplingStratum"] = None
    out["SamplingProbability"] = np.nan
    out.loc[chosen, "IsReviewCandidate"] = True
    out.loc[chosen, "SamplingStratum"] = np.where(out.loc[chosen, "CandidateRoute"].notna(), "MODEL_CANDIDATE", "AE_CANDIDATE")
    out.loc[chosen, "SamplingProbability"] = 1.0
    pool = out.index[standard & ~out["IsReviewCandidate"]]
    n = min(int(cfg.get("ReviewRandomSampleSize")), len(pool))
    if n:
        picks = np.random.default_rng(seed).choice(pool, size=n, replace=False)
        out.loc[picks, "IsReviewCandidate"] = True
        out.loc[picks, "SamplingStratum"] = "RANDOM_CHECK"
        out.loc[picks, "SamplingProbability"] = n / len(pool)
        random_note = "Random check: this transaction was NOT flagged by the model. Please confirm whether it is correct; this measures what the model misses."
        out.loc[picks, "ExplanationText"] = [random_note + (" " + t if isinstance(t, str) and t else "") for t in out.loc[picks, "ExplanationText"]]
    for col in REVIEW_TEXT_COLUMNS:
        if col in out:
            out.loc[~out["IsReviewCandidate"], col] = None
    return out


def feature_snapshot_rows(feats: pd.DataFrame, run_id: str) -> pd.DataFrame:
    cols = [c for c in feats.columns if c not in ("AccountManager", "LoginName")]
    payload = feats[cols].to_json(orient="records", date_format="iso", default_handler=str)
    records = json.loads(payload)
    texts = [json.dumps(r, sort_keys=True) for r in records]
    return pd.DataFrame({"SnapshotKey": feats["SnapshotKey"].values, "FeatureVersion": FEATURE_VERSION, "BaselineVersion": "b1",
                         "RunId": run_id, "FeatureJson": texts, "FeatureChecksum": [hashlib.sha256(t.encode()).hexdigest() for t in texts]})


def _json_default(o):
    if isinstance(o, (np.integer,)):
        return int(o)
    if isinstance(o, (np.floating,)):
        return None if np.isnan(o) else float(o)
    if isinstance(o, (pd.Timestamp, datetime)):
        return o.isoformat()
    if isinstance(o, (set, tuple)):
        return list(o)
    return str(o)
