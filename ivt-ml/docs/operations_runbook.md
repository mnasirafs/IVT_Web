# Operations runbook

All commands run from the `ivt-ml` folder with the virtual environment active (`.venv\Scripts\activate`). Every command defaults to the **local store** (files under `--output-dir`). Nothing is written to SQL Server unless `--store sql` is given **without** `--dry-run`. Schema changes need `--apply --i-am-authorised`.

## 1. One-time setup
```powershell
py -3.11 -m venv .venv
.venv\Scripts\python.exe -m pip install -e .[dev]
copy .env.example .env        # set IVT_ML_DB_* and IVT_ML_ARTIFACT_DIR (use a backed-up share); no passwords in the file
.venv\Scripts\python.exe -m pytest -q                       # 44 tests, all synthetic
```

### Database objects (requires authorisation for the target environment)
```powershell
ivt-ml migrate                         # prints every migration (dry run). Review it.
ivt-ml migrate --apply --i-am-authorised
ivt-ml seed-config --config-version dev-2026-10 --output seed.sql   # review, then run seed.sql in SSMS
```
* Migrations are idempotent (`IF OBJECT_ID ... IS NULL`). `ML.SchemaVersion` records each script with its checksum, and the runner stops if an applied script has changed.
* **Least privilege (recommended):** create a dedicated `ivt_ml` login with `SELECT` on the source tables/views, `SELECT/INSERT/UPDATE` on schema `ML`, and `EXECUTE` on `ML.usp_*`. The current app login `Report_Subscriber` is `db_owner`; avoid reusing it.
* Approve currency mappings before any publication:
  `INSERT ML.CurrencyMap (ScopeType, ScopeKey, CurrencyCode, ApprovedBy, ApprovedAt) VALUES ('CARRIER', 'NZPOST', 'NZD', '<name>', SYSUTCDATETIME());`
* Approve justification patterns before they act as weak labels (see `label_policy.md`):
  `INSERT ML.OutcomeTextMap (LikePattern, OutcomeType, ProposedLabel, Priority, ApprovedBy, ApprovedAt) VALUES ('%not an exception%', 'ValidRateChange', 0, 10, '<name>', SYSUTCDATETIME());`

## 2. Profiling and stage checks (read-only)
```powershell
ivt-ml profile --recent-weeks 8 --output-dir output/profiling
ivt-ml snapshot --dry-run --output-dir output/real                       # current week: reconciliation report
ivt-ml snapshot --week 2026-09-18 --dry-run --output-dir output/real     # any historical week
ivt-ml labels --as-of 2026-09-25 --weeks 26 --dry-run --output-dir output/real
ivt-ml features --week 2026-09-18 --dry-run --output-dir output/real
```

## 3. Training (candidate models only)
```powershell
ivt-ml train --as-of 2026-09-25 --feature-weeks 20 --dry-run --output-dir output/real --artifact-dir \\fileserver\ivt-ml\artifacts
```
* Output: `training_report_<run>.json` (partitions, label coverage, benchmarks, contamination sensitivity, calibration, threshold selection and the final test), plus two **CANDIDATE** registry entries (autoencoder and classifier).
* Without enough confirmed labels the classifier is `CLASSIFIER_WEAK_EXPERIMENTAL`. That is expected today, and it can never publish.
* Use `--store sql` (without `--dry-run`) to also record `ML.Run`, `ML.LabelEvent`, `ML.TrainingLabel` and `ML.ModelRegistry`.
* Retraining schedule: monthly, or when monitoring shows drift. Training never promotes a model.

## 4. Backtest (replay weekly scoring; local and isolated)
```powershell
ivt-ml backtest --start 2026-07-03 --end 2026-09-18 --output-dir output/real --artifact-dir \\fileserver\ivt-ml\artifacts
```
Produces `backtest/backtest_summary.csv` (weekly decision counts, AE candidates, rows with 2+ independent signals) and per-week audits. Pick weeks **after** the training test period to avoid replaying training data.

## 5. Model approval, promotion and rollback
```powershell
ivt-ml approve-model --model-version <ae-version> --approved-by "<name>" --evidence "<validation report / ticket>" --store sql
ivt-ml rollback-model --kind AUTOENCODER --to-version <older-version> --approved-by "<name>" --store sql
```
Approval is refused for synthetic models, for weak-label classifiers, and when the artifact checksum does not match. Approving a model retires the previous approved model of the same kind; that retired model remains the rollback target.

## 6. Weekly scoring
Run after `WebIVT.sp_Run_All_Tasks` / `IVT_PreProcess` has finished for the invoice week (check that `dbo.Sell_Side` holds the new week).
```powershell
# Dry run: no SQL writes; everything is written under output/
ivt-ml score --current-week 2026-09-25 --dry-run --output-dir output/weekly
# Shadow in SQL (ShadowMode = 1): audit, snapshots and features are written; nothing is published
ivt-ml score --current-week 2026-09-25 --store sql --config-version prod-shadow-1
```
Order inside `score`:
1. Start the run with a configuration snapshot.
2. Snapshot and reconcile.
3. Data-quality gate.
4. Point-in-time baselines and features.
5. Load approved artifacts (checksum-verified).
6. Score.
7. Explain.
8. Apply gates and caps.
9. Rule overlap.
10. Write the audit.
11. Stage exceptions.
12. Complete the run.
13. Optional atomic activation.

A failure at any step discards the staged rows; the previous results remain active.

## 7. Publication (only after controlled enablement, section 10)
```powershell
ivt-ml score --current-week 2026-09-25 --store sql --config-version prod-pub-1 --publish   # stage + activate in one go
ivt-ml publish --run-id <run-id> --store sql                                                 # or activate a SUCCEEDED staged run later
ivt-ml rollback-publication --run-id <run-id> --reason "<why>" --store sql                   # withdraw; restores superseded rows
```
Account managers read `ML.vReviewQueue`. Rule-flagged items appear only in `ML.vRuleOverlapExport`.

## 8. Feedback
```powershell
ivt-ml sample --audit-csv output/weekly/score_audit_<run>.csv --output review_sample.csv --per-stratum 50
# reviewers complete ReviewOutcome / OutcomeType / ReviewedBy / ReviewedAt / Comments
ivt-ml ingest-feedback --file review_sample.csv --store sql
```
Outcomes are validated: the outcome and outcome type must be consistent. Each one is appended as a new `OutcomeVersion`; history is never overwritten.

## 8a. ML Findings page (assessment → retraining loop)
* **Web page:** `IVT → ML Findings` (`/MLReview`). It reads `ML.vReviewCandidates`: the review rows of the latest successful, non-dry-run SCORE run per week.
  * Account managers see the rows routed to their login; admins see all, or one AM when impersonating (same rule as the chat).
* **Review rows per week**, chosen by `mark_review_rows` at scoring time:
  * up to `ReviewCandidateCap` (300) **model/autoencoder candidates**, ranked by calibrated probability, then the number of independent signals, then unusualness
  * no single reason code may take more than `ReviewMaxShareSameReason` (40%) of the list
  * `ReviewRandomSampleSize` (50) **random checks** from unflagged standard rows, recorded with `SamplingProbability = n/N`, so precision and recall can be re-weighted and misses found
* **Assessment:** Confirmed exception (incorrect buy/sell/dimensions, duplicate, missing charge), Valid (rate change / valid transaction) or Not enough information, with optional corrected amounts and a comment.
  * Each save appends a new `ML.ReviewFeedback` version (append-only, enforced by a trigger) and updates `ML.DetectedException.ReviewStatus` when a published row exists.
* **Retraining:** `ivt-ml train` reads `ML.ReviewFeedback` automatically.
  * Once confirmed outcomes reach `MinimumReviewedValidationSample` (300, with both classes), the classifier is trained as `CLASSIFIER_CONFIRMED` (calibrated) instead of the weak experimental model.
  * Until then, assessments are still used as confirmed labels inside the label report.
* **Scoring time:** score after the week's sell load has completed. In the 25 Sep test, 29k buy-only jobs (sell not loaded yet) dominated the "buy/sell mismatch" findings.

## 8b. Running scoring from Admin Controls (web)
Admins open **Admin Controls → Weekly ML scoring** in the IVT admin menu.
* The page shows the current invoice week, recent `ML.Run` results, and the latest log.
* **Run scoring** starts `ivt-ml score --store sql --allow-candidate` in the background on the IIS server.
* Access: real admins only (UserType 1/2; impersonation is ignored), one scoring run at a time (runs older than 3 hours are treated as stale).

One-time server setup (on the IIS host, e.g. `afs-sql01`):
1. Copy the `ivt-ml` folder to the server, e.g. `D:\ivt-ml`. Then run `py -3.11 -m venv .venv` and `.venv\Scripts\python.exe -m pip install -e .`
2. Copy the trained artifacts, e.g. `D:\ivt-ml\artifacts`, containing `registry.json` and the model folders.
3. Make sure **ODBC Driver 17 for SQL Server** is installed. If not, install it, or set `IVT_ML_ODBC_DRIVER` to an installed driver.
4. In the site's `Web.config` (or `App_Data\ivt-chat.secrets.config`), set:
   ```xml
   <add key="IVT_ML_PYTHON" value="D:\ivt-ml\.venv\Scripts\python.exe"/>
   <add key="IVT_ML_WORKDIR" value="D:\ivt-ml"/>
   <add key="IVT_ML_ARTIFACT_DIR" value="D:\ivt-ml\artifacts"/>
   <add key="IVT_ML_CONFIG_VERSION" value="dev-defaults"/>
   ```
5. Give the app pool identity (`IIS AppPool\IVT_TEST`) read/execute on `D:\ivt-ml` and modify on `D:\ivt-ml\output`.

**afs-sql01 test site, as deployed 2026-10-07:**
* Code: `D:\IVT_TEST\ivt-ml`, inside the web root. HTTP access is blocked by `ivt-ml\web.config` (request filtering denies every file and hides `output`, `src`, `.venv`, etc.). Prefer moving it outside the web root (e.g. `D:\ivt-ml`) and updating the settings.
* Python: machine-wide `D:\Python\python.exe` (3.12). The venv was created on the server with `D:\Python\python.exe -m venv D:\IVT_TEST\ivt-ml\.venv` and `pip install -e D:\IVT_TEST\ivt-ml`. **Never copy a `.venv` between machines**: it points at the original machine's Python.
* ODBC: only **ODBC Driver 11 for SQL Server** is installed, so `IVT_ML_ODBC_DRIVER = ODBC Driver 11 for SQL Server`.
* Settings: in `D:\IVT_TEST\App_Data\ivt-chat.secrets.config` (`IVT_ML_PYTHON`, `IVT_ML_WORKDIR`, `IVT_ML_ARTIFACT_DIR = D:\IVT_TEST\ivt-ml\output\real\artifacts`, `IVT_ML_ODBC_DRIVER`). Recycle the `IVT_TEST` app pool after editing it.
* Permissions: `IIS AppPool\IVT_TEST` has RX on `D:\IVT_TEST\ivt-ml` and `D:\Python`, and Modify on `D:\IVT_TEST\ivt-ml\output`.
* Model artifacts copied from another machine still load: when the registry's absolute `ArtifactUri` doesn't exist, `<artifact dir>\<ModelVersion>\<file>` is used, and the SHA-256 is verified.
* Updating code: `robocopy <repo>\ivt-ml\src \\afs-sql01\D$\IVT_TEST\ivt-ml\src /MIR /XD __pycache__`. No reinstall is needed, because the package is installed in editable mode.

The database connection is passed to the process in memory as `IVT_ML_DB_CONN`, built from the site's `IVTEntities` connection string. It never appears on the command line or in files. Logs are written to `<IVT_ML_WORKDIR>\output\web\score_<week>_<time>.log`.

## 9. Monitoring
```powershell
ivt-ml monitor --store sql --maturity-days 28
```
* Every scoring run writes `run_summary_<run>.json` (also stored in `ML.Run.MetricsJson`). It contains:
  * volumes and reconciliation totals; match states and segments; MoveIT coverage
  * peer coverage and fallback levels; null rates
  * the AE score distribution vs the expected share above the threshold
  * decisions, rule overlap, possible carrier rate changes, and model age
* `monitor` reports published precision on **mature** reviewed outcomes, with a Wilson 95% interval, review coverage and per-route precision.
* Alert and stop conditions:

| Condition | Action |
|---|---|
| `RECONCILIATION_TOTAL_MISMATCH` or `DUPLICATE_SNAPSHOT_KEY` (BLOCKER) | Run fails the quality gate; nothing publishes; investigate the source refresh |
| AE share above threshold > 3x expected | Data drift or operational change; check `possible_rate_changes` and PSI before trusting scores |
| Sparse peer share rising | New customers/lanes; consider broader fallbacks, or retrain |
| Mature precision lower bound < `MinimumPrecisionLowerBound` | Disable publication (`PublicationEnabled = 0`); investigate |
| Model age > `MaximumModelAgeDays` | Scores continue in shadow only; retrain and approve |

Do not retrain or publish through severe quality failures.

## 10. Controlled enablement checklist
1. Apply the migrations in the target environment (authorised change).
2. Approve `ML.CurrencyMap` for every carrier group, or keep `RequireKnownCurrencyForPublication = 1` and accept that nothing publishes.
3. Run shadow scoring for at least 6–8 weeks.
4. Sample and review using all four strata, and ingest the outcomes.
5. Collect at least `MinimumReviewedValidationSample` confirmed outcomes with both classes.
6. Retrain. The classifier becomes `CLASSIFIER_CONFIRMED` automatically once enough confirmed labels exist.
7. Confirm calibration on the separate period. `threshold_selection` must report a Wilson lower bound ≥ `MinimumPrecisionLowerBound` at a threshold you then set in `SupervisedProbabilityThreshold`.
8. Approve the models with a reference to the evidence.
9. Insert an approved configuration version:
   - `ShadowMode = 0`
   - `PublicationEnabled = 1`
   - `AllowedDetectionRoutes = 'CONFIRMED_PATTERN'`
   - business-approved `WeeklyAlertCap`, `PerAccountManagerAlertCap` and `MinimumMaterialityAmount` (per currency)
10. Keep `NoveltyPublicationEnabled = 0` until reviewed novelty-route samples show acceptable precision.

## 11. Rollback summary
| What | Command |
|---|---|
| Published results of one run | `ivt-ml rollback-publication --run-id <id> --reason "..." --store sql` |
| Model | `ivt-ml rollback-model --kind AUTOENCODER --to-version <v> --approved-by "<name>" --store sql` |
| Configuration | Close the current version's rows (`EffectiveTo`), insert or re-enable the earlier `ConfigVersion`, and pass `--config-version` |
| Disable everything | Insert `ShadowMode = 1` / `PublicationEnabled = 0` for the active configuration version |
