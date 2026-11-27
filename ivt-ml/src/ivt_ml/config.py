"""Runtime settings (environment) and SQL-controlled parameters (ML.ControlParameter).

Precedence for control parameters: the most specific active scope wins
(CUSTOMER_CARRIER -> CARRIER_GROUP -> CURRENCY -> GLOBAL). Overlapping active rows for the
same (scope, key, parameter) are rejected rather than silently picking one.
"""
from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass, field
from datetime import date, datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

# Development defaults only. Production activation requires validation evidence (see docs/model_validation.md).
DEFAULTS: Dict[str, Any] = {
    "SupervisedProbabilityThreshold": 0.95,
    "AEPercentileThreshold": 99.5,
    "TargetPublishedPrecision": 0.95,
    "PeerLookbackWeeks": 26,
    "MinimumPeerCount": 50,
    "MinimumEvidenceCount": 2,
    "NoveltyPublicationEnabled": 0,
    "WeeklyAlertCap": 200,
    "PerAccountManagerAlertCap": 25,
    "MinimumMaterialityAmount": 50.0,
    "MinimumCalibrationSample": 200,
    "MinimumReviewedValidationSample": 300,
    "MinimumPrecisionLowerBound": 0.90,
    "MaximumModelAgeDays": 120,
    "AllowedDetectionRoutes": "CONFIRMED_PATTERN",
    "ModelVersion": "",
    "FeatureVersion": "f1",
    "PublicationEnabled": 0,
    "ShadowMode": 1,
    "RetrainingLookbackWeeks": 52,
    "DataQualityFailureTolerance": 0.05,
    "RobustZEvidenceThreshold": 3.5,
    "UseMoveitFeaturesForTraining": 0,
    "RequireKnownCurrencyForPublication": 1,
    "ReviewCandidateCap": 300,          # ML Findings page: max model/AE candidates per week (highest scores first)
    "ReviewMaxShareSameReason": 0.4,    # ML Findings page: max share of the candidate list for one primary reason code
    "ReviewRandomSampleSize": 50,      # ML Findings page: random unflagged "random check" rows per week (recall / bias control)
}

SCOPE_ORDER = ["CUSTOMER_CARRIER", "CARRIER_GROUP", "CURRENCY", "GLOBAL"]


@dataclass
class Settings:
    """Process settings from environment variables. No credentials are hard-coded."""

    db_conn: Optional[str]
    artifact_dir: Path
    output_dir: Path
    log_level: str = "INFO"

    @staticmethod
    def from_env() -> "Settings":
        try:
            from dotenv import load_dotenv  # optional

            load_dotenv()
        except ImportError:
            pass
        conn = os.environ.get("IVT_ML_DB_CONN")
        if not conn and os.environ.get("IVT_ML_DB_SERVER"):
            driver = os.environ.get("IVT_ML_DB_DRIVER", "ODBC Driver 17 for SQL Server")
            conn = f"Driver={{{driver}}};Server={os.environ['IVT_ML_DB_SERVER']};Database={os.environ.get('IVT_ML_DB_NAME', 'IVT')};"
            if os.environ.get("IVT_ML_DB_TRUSTED", "1") == "1":
                conn += "Trusted_Connection=yes;"
            else:
                conn += f"UID={os.environ['IVT_ML_DB_USER']};PWD={os.environ['IVT_ML_DB_PASSWORD']};"
        return Settings(
            db_conn=conn,
            artifact_dir=Path(os.environ.get("IVT_ML_ARTIFACT_DIR", "artifacts")),
            output_dir=Path(os.environ.get("IVT_ML_OUTPUT_DIR", "output")),
            log_level=os.environ.get("IVT_ML_LOG_LEVEL", "INFO"),
        )


@dataclass
class ControlRow:
    config_version: str
    scope_type: str
    scope_key: str
    parameter_name: str
    numeric_value: Optional[float]
    text_value: Optional[str]
    effective_from: date
    effective_to: Optional[date]
    is_enabled: bool = True
    approved_by: Optional[str] = None
    approved_at: Optional[datetime] = None

    @property
    def value(self) -> Any:
        return self.numeric_value if self.numeric_value is not None else self.text_value


class ConfigError(ValueError):
    pass


@dataclass
class EffectiveConfig:
    """Resolved configuration for one run, snapshotted into ML.Run for reproducibility."""

    config_version: str
    as_of: date
    rows: List[ControlRow] = field(default_factory=list)

    def __post_init__(self) -> None:
        self._validate()

    def _active(self) -> List[ControlRow]:
        return [r for r in self.rows if r.is_enabled and r.effective_from <= self.as_of and (r.effective_to is None or r.effective_to > self.as_of)]

    def _validate(self) -> None:
        seen: Dict[tuple, ControlRow] = {}
        for r in self._active():
            if r.scope_type not in SCOPE_ORDER:
                raise ConfigError(f"Unknown scope type {r.scope_type!r} for {r.parameter_name}")
            key = (r.scope_type, (r.scope_key or "").upper(), r.parameter_name)
            if key in seen:
                raise ConfigError(f"Overlapping active values for {key}: rows effective {seen[key].effective_from} and {r.effective_from}")
            seen[key] = r
        # Validate types of known parameters.
        for r in self._active():
            if r.parameter_name in DEFAULTS and isinstance(DEFAULTS[r.parameter_name], (int, float)) and r.numeric_value is None:
                raise ConfigError(f"{r.parameter_name} needs a numeric value")

    def get(self, name: str, *, customer_carrier: Optional[str] = None, carrier_group: Optional[str] = None, currency: Optional[str] = None) -> Any:
        scopes = [("CUSTOMER_CARRIER", customer_carrier), ("CARRIER_GROUP", carrier_group), ("CURRENCY", currency), ("GLOBAL", "")]
        active = self._active()
        for scope_type, scope_key in scopes:
            if scope_key is None:
                continue
            for r in active:
                if r.parameter_name == name and r.scope_type == scope_type and (r.scope_key or "").upper() == scope_key.upper():
                    return r.value
        if name in DEFAULTS:
            return DEFAULTS[name]
        raise ConfigError(f"Unknown parameter {name}")

    def snapshot(self) -> Dict[str, Any]:
        resolved = {k: self.get(k) for k in DEFAULTS}
        overrides = [
            {"scope": r.scope_type, "key": r.scope_key, "name": r.parameter_name, "value": r.value, "approvedBy": r.approved_by}
            for r in self._active()
            if r.scope_type != "GLOBAL"
        ]
        return {"configVersion": self.config_version, "asOf": self.as_of.isoformat(), "global": resolved, "overrides": overrides}

    def snapshot_json(self) -> str:
        return json.dumps(self.snapshot(), sort_keys=True, default=str)

    def checksum(self) -> str:
        return hashlib.sha256(self.snapshot_json().encode("utf-8")).hexdigest()


def defaults_as_rows(config_version: str = "dev-defaults", effective_from: date = date(2000, 1, 1)) -> List[ControlRow]:
    rows = []
    for name, value in DEFAULTS.items():
        numeric = float(value) if isinstance(value, (int, float)) else None
        rows.append(ControlRow(config_version, "GLOBAL", "", name, numeric, None if numeric is not None else str(value), effective_from, None))
    return rows


def load_effective_config(rows: Iterable[ControlRow], config_version: str, as_of: date) -> EffectiveConfig:
    return EffectiveConfig(config_version=config_version, as_of=as_of, rows=[r for r in rows if r.config_version == config_version])
