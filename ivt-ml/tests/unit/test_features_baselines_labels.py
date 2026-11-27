from datetime import date, timedelta

import numpy as np
import pandas as pd
import pytest

from ivt_ml.baselines import attach_peer_features, compute_baselines
from ivt_ml.classifier import out_of_sample_ae_scores, train_classifier
from ivt_ml.features import base_metrics, build_features, model_feature_names, validate_feature_frame
from ivt_ml.labels import build_label_events, training_labels, weak_hold_targets
from ivt_ml.reconciliation import reconcile
from ivt_ml.synthetic import _line, build_fixture


def snaps_for(weeks, n=40, carrier="STE", currency=None, buy_scale=1.0, start=date(2026, 1, 2), seed=0):
    rng = np.random.default_rng(seed)
    sell, buy = [], []
    for i in range(weeks):
        w = start + timedelta(weeks=i)
        for j in range(n):
            job = f"{carrier}{i:03d}{j:04d}"
            f = float(rng.uniform(90, 110))
            sell.append(_line("SELL", w, job, "C", carrier, f * 1.2, 0, f * 0.2, 10, 12, 0.1))
            buy.append(_line("BUY", w, job, "C", carrier, f * buy_scale, 0, f * 0.18, 10, 12, 0.1))
    cm = {carrier: currency} if currency else None
    return reconcile(pd.DataFrame(sell), pd.DataFrame(buy), source_system="T", currency_map=cm).snapshots


def test_zero_denominators_give_missing_plus_flag_not_epsilon():
    s = snaps_for(1, n=1).copy()
    s.loc[:, ["SellNett", "BuyFreight", "ChargeableWeight"]] = [0.0, 0.0, 0.0]
    m = base_metrics(s).iloc[0]
    assert pd.isna(m["MarginRatio"]) and m["MarginRatioUndefinedFlag"] == 1
    assert pd.isna(m["BuyFuelRatio"]) and m["FuelRatioUndefinedFlag"] == 1
    assert pd.isna(m["BuyPerChargeableKg"])
    assert m["MarginAmount"] == pytest.approx(-m["BuyNett"])   # valid zero sell preserved: margin still computed


def test_point_in_time_baseline_excludes_cutoff_week_and_future():
    hist = base_metrics(snaps_for(6))
    cutoff = pd.Timestamp(date(2026, 1, 2) + timedelta(weeks=3))
    b = compute_baselines(hist, cutoff, lookback_weeks=26)
    assert (pd.to_datetime(b["WindowEnd"]) < cutoff).all()
    # A wildly different (corrected) value in the cutoff week or later must not move the baseline.
    hist2 = hist.copy()
    hist2.loc[pd.to_datetime(hist2["SnapshotWeek"]) >= cutoff, "BuyNett"] = 1e6
    b2 = compute_baselines(hist2, cutoff, lookback_weeks=26)
    pd.testing.assert_frame_equal(b.reset_index(drop=True), b2.reset_index(drop=True))


def test_zero_mad_uses_documented_fallback_or_missing():
    hist = base_metrics(snaps_for(4))
    hist["BuyNett"] = 100.0  # identical values -> MAD 0 and P90-P10 0
    b = compute_baselines(hist, pd.Timestamp("2026-02-27"), 26)
    target = hist.head(1).copy(); target["BuyNett"] = 150.0
    peers = attach_peer_features(target, b, min_count=10)
    assert pd.isna(peers["BuyNettRobustZ"].iloc[0]) and peers["ZeroSpreadFlag"].iloc[0] == 1


def test_currency_segments_never_share_peers():
    aud = base_metrics(snaps_for(4, carrier="STE", currency="AUD"))
    nzd = base_metrics(snaps_for(4, carrier="STE", currency="NZD", buy_scale=3.0, seed=5))
    nzd["JobNumber"] = "N" + nzd["JobNumber"]
    b = compute_baselines(pd.concat([aud, nzd]), pd.Timestamp("2026-02-27"), 26)
    keys = b[b["MetricName"] == "BuyNett"]["PeerGroupKey"]
    assert keys.str.contains("|AUD|").any() and keys.str.contains("|NZD|").any()
    assert not keys.str.contains("AUD").where(keys.str.contains("NZD"), False).any()


def test_training_and_scoring_share_identical_features():
    hist = snaps_for(5)
    week = pd.Timestamp(date(2026, 1, 2) + timedelta(weeks=4))
    current = hist[pd.to_datetime(hist["SnapshotWeek"]) == week]
    prior = hist[pd.to_datetime(hist["SnapshotWeek"]) < week]
    f_train, _ = build_features(current, prior, scoring_week=week, lookback_weeks=26, min_peer_count=10)
    f_score, _ = build_features(current.copy(), prior.copy(), scoring_week=week, lookback_weeks=26, min_peer_count=10)
    names = model_feature_names(use_moveit=False, purpose="classifier")
    assert validate_feature_frame(f_train, names) == []
    pd.testing.assert_frame_equal(f_train[names], f_score[names])
    assert validate_feature_frame(f_train.assign(AccountManager="x"), names + ["AccountManager"]) != []


# ---- labels ----------------------------------------------------------------------------------------------
def _events_fixture():
    snaps = snaps_for(3, n=3)
    snaps = snaps.assign(SnapshotWeek=pd.to_datetime(snaps["SnapshotWeek"]))
    job = snaps.iloc[0]
    hold = pd.DataFrame([{"SourceRecordId": "h1", "JobNumberRaw": job["JobNumber"], "Customer": "C", "Carrier": "STE", "ErrCode": "9999",
                          "ShortErrDesc": "x", "Reviewer": "am", "ActionTime": pd.Timestamp("2026-01-05"), "InvoiceWeek": job["SnapshotWeek"], "IsDeleted": 1}])
    rel = pd.DataFrame([{"SourceRecordId": "r1", "JobNumberRaw": job["JobNumber"], "Customer": "C", "Carrier": "STE", "ErrCode": "9999",
                         "Justification": "Release - Rate Structure change at BA side. Not an exception", "Reviewer": "am",
                         "ActionTime": pd.Timestamp("2026-01-20"), "InvoiceWeek": job["SnapshotWeek"]}])
    tmap = pd.DataFrame([{"LikePattern": "%not an exception%", "OutcomeType": "ValidRateChange", "ProposedLabel": 0, "LabelQuality": "WEAK", "Priority": 1}])
    fb = pd.DataFrame([{"FeedbackId": 1, "SnapshotKey": snaps.iloc[1]["SnapshotKey"], "ReviewOutcome": "CONFIRMED_EXCEPTION", "OutcomeType": "IncorrectBuy",
                        "ReviewedBy": "am", "ReviewedAt": pd.Timestamp("2026-01-25"), "OutcomeVersion": 1, "IsCurrent": 1}])
    return snaps, build_label_events(snaps, holds=hold, releases=rel, feedback=fb, text_map=tmap)


def test_label_availability_cutoff_and_future_outcomes():
    snaps, events = _events_fixture()
    early = training_labels(events, snaps, pd.Timestamp("2026-01-10"), "v").labels
    late = training_labels(events, snaps, pd.Timestamp("2026-02-01"), "v").labels.set_index("SnapshotKey")
    first = snaps.iloc[0]["SnapshotKey"]
    assert early.set_index("SnapshotKey").loc[first, "Label"] == 1          # unreleased hold as of cutoff (weak)
    assert early.set_index("SnapshotKey").loc[first, "LabelQuality"] == "WEAK"
    assert late.loc[first, "Label"] == 0 and late.loc[first, "OutcomeType"] == "ValidRateChange"
    assert snaps.iloc[1]["SnapshotKey"] not in set(early["SnapshotKey"])    # review on 25 Jan invisible on 10 Jan
    assert late.loc[snaps.iloc[1]["SnapshotKey"], "LabelQuality"] == "CONFIRMED"
    assert snaps.iloc[2]["SnapshotKey"] not in late.index                     # no events -> unlabeled, not negative


def test_unmapped_release_and_conflicts_are_unlabeled():
    snaps, events = _events_fixture()
    ev = events.copy()
    ev.loc[ev["EventSource"] == "RELEASE", ["ProposedLabel", "OutcomeType", "LabelQuality"]] = [pd.NA, None, "EVIDENCE_ONLY"]
    res = training_labels(ev, snaps, pd.Timestamp("2026-02-01"), "v")
    assert snaps.iloc[0]["SnapshotKey"] not in set(res.labels["SnapshotKey"])
    assert res.report["released_without_outcome"] == 1
    conflict = pd.concat([events, events[events["EventSource"] == "RELEASE"].assign(SourceRecordId="r2", ProposedLabel=1, OutcomeType="IncorrectBuy")])
    res2 = training_labels(conflict, snaps, pd.Timestamp("2026-02-01"), "v")
    assert res2.report["excluded_conflicts"] == 1


def test_weak_targets_need_maturity_and_never_use_ambiguous():
    snaps, events = _events_fixture()
    t = weak_hold_targets(snaps.reset_index(drop=True), events, pd.Timestamp("2026-01-20"))
    assert t.iloc[0] == 1
    recent = pd.to_datetime(snaps["SnapshotWeek"]).reset_index(drop=True) + pd.Timedelta(days=14) > pd.Timestamp("2026-01-20")
    assert t[recent & (t.index != 0)].isna().all()


# ---- out-of-sample AE ------------------------------------------------------------------------------------
def test_oof_autoencoder_scores_only_use_earlier_weeks():
    fx = build_fixture(weeks=10, jobs_per_week=60, seed=3)
    snaps = reconcile(fx["sell"], fx["buy"], source_system="T", carrier_groups=fx["carrier_groups"]).snapshots
    frames = {}
    weeks = sorted(pd.to_datetime(snaps["SnapshotWeek"]).unique())
    for w in weeks[2:]:
        cur = snaps[pd.to_datetime(snaps["SnapshotWeek"]) == w]
        f, _ = build_features(cur, snaps[pd.to_datetime(snaps["SnapshotWeek"]) < w], scoring_week=w, lookback_weeks=8, min_peer_count=10)
        f.index = f["SnapshotKey"]
        frames[pd.Timestamp(w)] = f
    feats = model_feature_names(use_moveit=False, purpose="ae")
    oof = out_of_sample_ae_scores(frames, feats, min_train_weeks=3, block_weeks=2, gap_weeks=1, max_rows=5000, log=lambda *_: None)
    week_of = pd.concat(frames.values())["SnapshotWeek"]
    scored_week = pd.to_datetime(week_of.reindex(oof.index))
    assert (pd.to_datetime(oof["OOFTrainEnd"]) < scored_week - pd.Timedelta(weeks=1)).all()   # gap respected
    first_weeks = list(frames)[:3]
    assert not set(pd.concat([frames[w] for w in first_weeks]).index) & set(oof.index)       # no in-sample scores


def test_classifier_handles_unseen_categories():
    rng = np.random.default_rng(1)
    df = pd.DataFrame({"BuyNett": rng.normal(100, 10, 400), "CarrierGroup": rng.choice(["A", "B"], 400), "WorkCode": "GEN", "CustomerGroup": "G"})
    y = pd.Series((df["BuyNett"] > 105).astype(int))
    m = train_classifier(df, y, kind="CLASSIFIER_WEAK_EXPERIMENTAL", numeric=["BuyNett"], categorical=["CarrierGroup", "WorkCode", "CustomerGroup"], min_category_frequency=5)
    new = pd.DataFrame({"BuyNett": [130.0], "CarrierGroup": ["NEVER_SEEN"], "WorkCode": ["X"], "CustomerGroup": [None]})
    assert 0 <= m.raw_score(new)[0] <= 1
    assert m.calibrated_probability(new) is None          # weak model can never provide a confirmed probability
