import logging
from datetime import date

import pytest

from ivt_ml.config import ControlRow, EffectiveConfig, defaults_as_rows
from ivt_ml.pipeline import Context, SyntheticSource
from ivt_ml.store import LocalStore
from ivt_ml.synthetic import build_fixture

logging.basicConfig(level=logging.WARNING)


def make_config(overrides=None, version="test") -> EffectiveConfig:
    rows = defaults_as_rows(version)
    for name, value in (overrides or {}).items():
        rows = [r for r in rows if r.parameter_name != name]
        numeric = float(value) if isinstance(value, (int, float)) else None
        rows.append(ControlRow(version, "GLOBAL", "", name, numeric, None if numeric is not None else str(value), date(2000, 1, 1), None))
    return EffectiveConfig(version, date(2026, 10, 1), rows)


@pytest.fixture(scope="session")
def fixture_data():
    return build_fixture(weeks=24, jobs_per_week=140)


@pytest.fixture()
def synthetic_ctx(tmp_path, fixture_data):
    def _make(overrides=None, artifact_dir=None):
        cfg = make_config({"PeerLookbackWeeks": 8, "MinimumPeerCount": 20, "MinimumCalibrationSample": 20, **(overrides or {})})
        return Context(source=SyntheticSource(fixture_data), store=LocalStore(tmp_path / "store"), cfg=cfg,
                       artifact_dir=artifact_dir or (tmp_path / "artifacts"), cache_dir=tmp_path / "cache")
    return _make
