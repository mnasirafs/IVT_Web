# IVT hybrid anomaly detection (shadow-first)

Explainable detection of consignments whose charges, margin, measurements or reconciliation suggest a billing/trading exception worth an account manager's review. It combines a tabular autoencoder, logistic regression, IVT rule findings (evidence only) and account-manager review feedback (the only source of confirmed labels). **Not fraud detection. Never holds consignments or modifies source data.**

## Quick start
```powershell
py -3.11 -m venv .venv; .venv\Scripts\python.exe -m pip install -e .[dev]
.venv\Scripts\python.exe -m pytest -q                  # synthetic tests
ivt-ml profile --output-dir output/profiling           # read-only, needs IVT_ML_DB_* in .env
ivt-ml migrate                                         # prints SQL only
```

## Repository map
| Path | Contents |
|---|---|
| `src/ivt_ml/` | config, db, contracts, keys, source_adapters, reconciliation, labels, baselines, features, preprocessing, autoencoder, classifier, calibration, explanations, publication, store, registry, monitoring, profiling, synthetic, pipeline, cli |
| `scripts/` | one wrapper per pipeline stage (e.g. `score_week.py`, `train_classifier.py`) |
| `sql/migrations/` | V001–V007 idempotent, SQL Server 2012+ compatible |
| `sql/profiling/` | read-only profiling queries |
| `tests/unit`, `tests/integration` | unit tests and the synthetic end-to-end test |
| `docs/` | architecture, data contract, feature dictionary, label policy, model validation, operations runbook |

See `docs/operations_runbook.md` for every command.
