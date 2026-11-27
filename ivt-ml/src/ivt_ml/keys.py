"""Versioned key construction (KEY_VERSION k1).

TransactionKey = sha256("k1|" + Customer + "|" + Carrier + "|" + JobNumber)
    Business identity across time. Customer + carrier are included because jno alone is not guaranteed
    globally unique; PrimaryKeyField is never used because it is regenerated on refresh.
SnapshotKey    = sha256("k1|" + TransactionKey + "|" + SnapshotWeek + "|" + SourceSystem)
    The entity at one scoring cutoff. A re-invoiced job in a later week gets a new SnapshotKey.

Normalisation is deliberately non-destructive: trim surrounding whitespace and upper-case (IVT collation is
case-insensitive, so this cannot merge identities SQL Server already treats as distinct). Leading zeros,
internal characters and connote suffixes are preserved. Raw values are kept alongside.
"""
from __future__ import annotations

import hashlib
from typing import Optional

import pandas as pd

from .contracts import KEY_VERSION


def normalize_identifier(value: object) -> Optional[str]:
    if value is None or (isinstance(value, float) and pd.isna(value)):
        return None
    text = str(value).strip().upper()
    return text or None


def strip_connote_suffix(value: object) -> Optional[str]:
    """Python port of IVT's dbo.fn_strip_connote_suffix, used only for MoveIT matching (MAPPED status).

    Removes trailing letters from a non-numeric connote ('ABC123FC' -> 'ABC123'); returns None when there is
    no letter suffix or the value is numeric, exactly like the SQL function. The original connote is kept.
    """
    text = normalize_identifier(value)
    if not text:
        return None
    stripped = text.rstrip("ABCDEFGHIJKLMNOPQRSTUVWXYZ")
    if stripped == text or not stripped:
        return None
    try:
        float(text)
        return None
    except ValueError:
        return stripped


def _sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def transaction_key(customer: object, carrier: object, job_number: object) -> str:
    parts = [normalize_identifier(customer) or "", normalize_identifier(carrier) or "", normalize_identifier(job_number) or ""]
    if not parts[2]:
        raise ValueError("JobNumber is required for a TransactionKey")
    return _sha(f"{KEY_VERSION}|" + "|".join(parts))


def snapshot_key(txn_key: str, snapshot_week: object, source_system: str) -> str:
    week = pd.Timestamp(snapshot_week).strftime("%Y-%m-%d")
    return _sha(f"{KEY_VERSION}|{txn_key}|{week}|{source_system}")


def row_hash(values: dict) -> str:
    """Stable hash of a raw source row, used to detect repeated snapshots of the same record."""
    return _sha("|".join(f"{k}={'' if v is None or (isinstance(v, float) and pd.isna(v)) else v}" for k, v in sorted(values.items())))


def publication_key(snapshot: str, model_version: str, feature_version: str, config_version: str) -> str:
    return _sha(f"{snapshot}|{model_version}|{feature_version}|{config_version}")
