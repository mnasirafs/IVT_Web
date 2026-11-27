"""Model registry: artifacts on durable storage (IVT_ML_ARTIFACT_DIR), metadata in registry.json and, when a SQL
store is used, ML.ModelRegistry. Promotion is a separate, explicit, audited step (never automatic after training).
"""
from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

from .autoencoder import sha256_file


class RegistryError(RuntimeError):
    pass


class LocalRegistry:
    def __init__(self, artifact_dir: Path):
        self.root = Path(artifact_dir)
        self.root.mkdir(parents=True, exist_ok=True)
        self.path = self.root / "registry.json"

    def entries(self) -> List[Dict]:
        return json.loads(self.path.read_text(encoding="utf-8")) if self.path.exists() else []

    def _save(self, entries: List[Dict]) -> None:
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(entries, indent=2, default=str), encoding="utf-8")
        tmp.replace(self.path)

    def register(self, entry: Dict) -> Dict:
        entries = self.entries()
        if any(e["ModelVersion"] == entry["ModelVersion"] for e in entries):
            raise RegistryError(f"Model version {entry['ModelVersion']} already registered")
        if entry.get("ProvidesCalibratedProbability") and entry["ModelKind"] != "CLASSIFIER_CONFIRMED":
            raise RegistryError("Only confirmed-outcome classifiers may claim calibrated probabilities")
        entry = {**entry, "Status": "CANDIDATE", "RegisteredAt": datetime.utcnow().isoformat()}
        entries.append(entry)
        self._save(entries)
        return entry

    def get(self, version: str) -> Dict:
        for e in self.entries():
            if e["ModelVersion"] == version:
                return e
        raise RegistryError(f"Unknown model version {version}")

    def approve(self, version: str, approved_by: str, *, evidence_note: str) -> Dict:
        """Approve a candidate. The previously approved model of the same kind is retired (kept for rollback)."""
        entries = self.entries()
        target = next((e for e in entries if e["ModelVersion"] == version), None)
        if target is None:
            raise RegistryError(f"Unknown model version {version}")
        if target.get("IsSyntheticOnly"):
            raise RegistryError("Synthetic-only models can never be approved")
        if target["ModelKind"] == "CLASSIFIER_WEAK_EXPERIMENTAL":
            raise RegistryError("Weak-label experimental classifiers cannot be approved for publication")
        if not sha256_file(self.artifact_path(target)) == target["ArtifactSha256"]:
            raise RegistryError("Artifact checksum mismatch; refusing approval")
        for e in entries:
            if e["ModelKind"] == target["ModelKind"] and e["Status"] == "APPROVED":
                e["Status"] = "RETIRED"
        target.update(Status="APPROVED", ApprovedBy=approved_by, ApprovedAt=datetime.utcnow().isoformat(), ApprovalEvidence=evidence_note)
        self._save(entries)
        return target

    def rollback(self, kind: str, to_version: str, approved_by: str) -> Dict:
        """Re-approve an earlier (retired) model of the same kind."""
        entries = self.entries()
        target = next((e for e in entries if e["ModelVersion"] == to_version and e["ModelKind"] == kind), None)
        if target is None or target["Status"] not in ("RETIRED", "APPROVED"):
            raise RegistryError("Rollback target must be a previously approved model of the same kind")
        for e in entries:
            if e["ModelKind"] == kind and e["Status"] == "APPROVED":
                e["Status"] = "RETIRED"
        target.update(Status="APPROVED", ApprovedBy=approved_by, ApprovedAt=datetime.utcnow().isoformat(), ApprovalEvidence=f"Rollback to {to_version}")
        self._save(entries)
        return target

    def current(self, kind: str, *, allow_candidate: bool = False) -> Optional[Dict]:
        entries = [e for e in self.entries() if e["ModelKind"] == kind]
        approved = [e for e in entries if e["Status"] == "APPROVED"]
        if approved:
            return sorted(approved, key=lambda e: e["ApprovedAt"])[-1]
        if allow_candidate:
            cands = [e for e in entries if e["Status"] == "CANDIDATE"]
            return sorted(cands, key=lambda e: e["RegisteredAt"])[-1] if cands else None
        return None

    def artifact_path(self, entry: Dict) -> Path:
        """Artifact file for an entry. Registries copied to another machine keep the original absolute ArtifactUri,
        so fall back to <artifact_dir>/<ModelVersion>/<file name>. The SHA-256 is still verified on load."""
        original = Path(entry["ArtifactUri"])
        if original.exists():
            return original
        relocated = self.root / entry["ModelVersion"] / original.name
        if relocated.exists():
            return relocated
        raise RegistryError(f"Artifact for {entry['ModelVersion']} not found at {original} or {relocated}")

    def age_days(self, entry: Dict, now: Optional[datetime] = None) -> int:
        trained = datetime.fromisoformat(str(entry.get("TrainedAt") or entry["RegisteredAt"]))
        return ((now or datetime.utcnow()) - trained).days
