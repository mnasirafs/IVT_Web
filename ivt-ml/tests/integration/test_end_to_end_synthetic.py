"""End-to-end run on the SYNTHETIC fixture (not IVT data): train -> score -> gates -> stage -> publish."""
import json

import pandas as pd
import pytest

from ivt_ml.pipeline import score_week, train
from ivt_ml.registry import RegistryError


@pytest.fixture(scope="module")
def trained(tmp_path_factory, fixture_data):
    from tests.conftest import make_config
    from ivt_ml.pipeline import Context, SyntheticSource
    from ivt_ml.store import LocalStore

    root = tmp_path_factory.mktemp("e2e")
    cfg = make_config({"PeerLookbackWeeks": 8, "MinimumPeerCount": 20, "MinimumCalibrationSample": 20})
    ctx = Context(source=SyntheticSource(fixture_data), store=LocalStore(root / "store"), cfg=cfg, artifact_dir=root / "artifacts", cache_dir=root / "cache")
    report = train(ctx, as_of=fixture_data["weeks"][-1], feature_weeks=14, output_dir=root / "out", contamination_check=False)
    return root, report


def _ctx(root, fixture_data, overrides=None):
    from tests.conftest import make_config
    from ivt_ml.pipeline import Context, SyntheticSource
    from ivt_ml.store import LocalStore

    cfg = make_config({"PeerLookbackWeeks": 8, "MinimumPeerCount": 20, "MinimumCalibrationSample": 20, **(overrides or {})})
    return Context(source=SyntheticSource(fixture_data), store=LocalStore(root / "store"), cfg=cfg, artifact_dir=root / "artifacts", cache_dir=root / "cache")


def test_training_registers_weak_experimental_without_confirmed_labels(trained):
    root, report = trained
    assert report["synthetic"] is True
    assert report["classifier_kind"] == "CLASSIFIER_WEAK_EXPERIMENTAL"
    assert "weak" in report["classifier_target_note"].lower()
    assert report["labels"]["confirmed_positive"] == 0
    reg = json.loads((root / "artifacts" / "registry.json").read_text())
    assert all(e["IsSyntheticOnly"] for e in reg)
    assert all(e["Status"] == "CANDIDATE" for e in reg)
    assert not any(e["ProvidesCalibratedProbability"] for e in reg)


def test_synthetic_models_cannot_be_approved(trained, fixture_data):
    root, report = trained
    ctx = _ctx(root, fixture_data)
    with pytest.raises(RegistryError):
        ctx.registry.approve(report["registered"]["autoencoder"], "tester", evidence_note="none")


def test_shadow_scoring_publishes_nothing_and_never_claims_probability(trained, fixture_data):
    root, _ = trained
    ctx = _ctx(root, fixture_data)
    result = score_week(ctx, output_dir=root / "out", allow_candidate=True)
    audit = result["audit"]
    sc = fixture_data["scenario"]
    by_job = audit.set_index("JobNumber")
    assert len(result["exceptions"]) == 0                       # shadow mode: nothing reaches DetectedException
    assert audit["ConfirmedExceptionProbability"].isna().all()  # weak model never yields confirmed probability
    assert by_job.loc[sc["credit_reversal"], "Decision"] == "UNSUPPORTED_SEGMENT"
    assert by_job.loc[sc["ambiguous_join"], "Decision"] == "UNSUPPORTED_SEGMENT"
    anomaly = by_job.loc[sc["confirmed_anomaly"]]
    assert anomaly["AEAnomalyPercentile"] > 95
    assert "ML_HIGH_BUY_COST" in anomaly["ReasonCodes"] and "ML_WEIGHT_MISMATCH" in anomaly["ReasonCodes"]
    assert anomaly["Decision"] == "SHADOW_ONLY"
    assert by_job.loc[sc["rule_overlap"], "AlreadyRuleFlagged"]
    assert by_job.loc[sc["missing_moveit"], "MoveitMatchStatus"] == "UNMATCHED"
    assert by_job.loc[sc["sparse_peer"], "SparsePeerFlag"] == 1
    # The repeated snapshot and the multi-line job are each one row.
    assert (audit["JobNumber"] == sc["repeated_snapshot"]).sum() == 1
    multi = by_job.loc[sc["multi_line"]]
    assert multi["SellLineCount"] == 2 and multi["BuyLineCount"] == 2


def test_novelty_route_publication_is_atomic_and_idempotent(trained, fixture_data):
    root, _ = trained
    ctx = _ctx(root, fixture_data, {"PublicationEnabled": 1, "ShadowMode": 0, "NoveltyPublicationEnabled": 1, "AllowedDetectionRoutes": "NOVEL_PATTERN",
                                     "AEPercentileThreshold": 97.0})
    # Candidate models are demoted to SHADOW_ONLY, so simulate an approved AE in this isolated registry copy.
    entries = json.loads((root / "artifacts" / "registry.json").read_text())
    for e in entries:
        e["IsSyntheticOnly"] = False
        if e["ModelKind"] == "AUTOENCODER":
            e.update(Status="APPROVED", ApprovedBy="test", ApprovedAt=pd.Timestamp.utcnow().isoformat())
    (root / "artifacts" / "registry.json").write_text(json.dumps(entries))

    first = score_week(ctx, output_dir=root / "out", publish=True)
    published = first["exceptions"]
    assert len(published) >= 1
    assert set(published["DetectionRoute"]) == {"NOVEL_PATTERN"}
    assert published["ConfirmedExceptionProbability"].isna().all()
    store_rows = ctx.store.exceptions()
    active = store_rows[store_rows["IsActive"]]
    assert len(active) == len(published)
    assert active["ExplanationText"].str.contains("more unusual than").all()
    overlap = active[active["AlreadyRuleFlagged"]]
    assert (overlap["ReviewStatus"] == "ROUTED_TO_RULE_WORKFLOW").all()

    # Rerun with identical versions: no duplicates.
    second = score_week(ctx, output_dir=root / "out", publish=True)
    assert len(second["exceptions"]) == 0
    assert len(ctx.store.exceptions().query("IsActive")) == len(active)

    # Failure after staging: nothing new becomes active; previous results remain.
    ctx2 = _ctx(root, fixture_data, {"PublicationEnabled": 1, "ShadowMode": 0, "NoveltyPublicationEnabled": 1, "AllowedDetectionRoutes": "NOVEL_PATTERN",
                                      "AEPercentileThreshold": 97.0}, )
    ctx2.cfg.config_version  # same config
    with pytest.raises(RuntimeError):
        score_week(_ctx(root, fixture_data, {"PublicationEnabled": 1, "ShadowMode": 0, "NoveltyPublicationEnabled": 1,
                                             "AllowedDetectionRoutes": "NOVEL_PATTERN", "AEPercentileThreshold": 96.0}),
                   output_dir=root / "out", publish=True, fail_after_staging=True)
    after = ctx.store.exceptions()
    assert len(after.query("IsActive")) == len(active)
    assert (after["PublicationState"] != "STAGED").all()

    # Rollback withdraws the published run.
    ctx.store.rollback_publication(first["run_id"], "test rollback")
    assert len(ctx.store.exceptions().query("IsActive")) == 0
