# Architecture

## Purpose and boundaries
The pipeline identifies consignments whose buy charges, sell charges, margin, measurements, charge composition or dispatch reconciliation suggest a **billing/trading exception worth an account manager's review**. It is **not fraud detection**. It never holds consignments and never modifies source transactions. Existing IVT rule detection is unchanged; ML results are a separate, additional evidence source.

## Components

```
            IVT (read-only)                                      ML schema (written only when authorised)
 ┌──────────────────────────────┐   source_adapters.py   ┌─────────────────────────────────────────────┐
 │ dbo.Sell_Side[_History]      │──────────────────────▶ │ ML.Run (config snapshot, versions, status)  │
 │ dbo.Buy_Side[_History]       │   reconciliation.py    │ ML.TradingSnapshot + ML.SourceRowMap         │
 │ tmp.Moveit_Consignment       │   (aggregate each side │ ML.DataQualityIssue                          │
 │ AFSXchange Customers/Carriers│    then join, totals)  │ ML.LabelEvent / ML.TrainingLabel             │
 │ dbo.vAccountManager          │                        │ ML.PeerBaseline / ML.FeatureSnapshot         │
 │ dbo.ConsignmentForReview     │   labels.py            │ ML.ModelRegistry (+ artifacts on disk)       │
 │ dbo.ReleasedConsignment      │   baselines.py         │ ML.ScoreAudit (every scored row)             │
 │ tmp.IVT_ResultHistory        │   features.py          │ ML.DetectedException (staged -> active)      │
 │ tmp.CurrentIVTResult         │   autoencoder.py       │ ML.ReviewFeedback (append-only outcomes)     │
 └──────────────────────────────┘   classifier.py        │ ML.ControlParameter / ML.CurrencyMap         │
                                    calibration.py       │ ML.OutcomeTextMap                            │
                                    explanations.py      └─────────────────────────────────────────────┘
                                    publication.py  ──▶  ML.vReviewQueue / ML.vRuleOverlapExport (read-only views)
```

* **One code path** builds snapshots and features for history and for the current week (`reconcile` → `build_features`). Training and scoring therefore cannot drift apart. `tests/unit/test_features_baselines_labels.py::test_training_and_scoring_share_identical_features` guards this.
* **Point in time**: baselines use only weeks strictly before the scoring week; labels are visible only after `LabelAvailableAt`; autoencoder features for classifier training are produced out of sample by expanding chronological folds.
* **Two detection routes** (`publication.py`):
  * `CONFIRMED_PATTERN`: calibrated probability from a classifier trained on *confirmed* review outcomes. Disabled until such a model is approved.
  * `NOVEL_PATTERN`: autoencoder percentile ≥ threshold, plus sufficient peers and at least N independent factual signals. Disabled by default (`NoveltyPublicationEnabled = 0`).
* **Shadow first**: by default (`ShadowMode = 1`, `PublicationEnabled = 0`) every decision is recorded in `ML.ScoreAudit` and nothing reaches `ML.DetectedException`.

## Publication and atomicity
1. Scoring writes all audit rows, then inserts the run's exceptions with `PublicationState = 'STAGED'` and `IsActive = 0` in one transaction.
2. `ML.usp_ActivateRun` takes an application lock, supersedes earlier active rows for the same snapshot, activates the staged rows and marks the run `PUBLISHED`. This all happens in **one transaction**: readers see either the previous set or the new set.
3. Any failure calls `ML.usp_DiscardStagedRun` (staged rows become `DISCARDED`, the run becomes `FAILED`). Earlier active results stay as they were.
4. Reruns are idempotent. `PublicationKey = hash(SnapshotKey, ModelVersion, FeatureVersion, ConfigVersion)` is unique among staged/active rows, so an identical rerun stages nothing new.
5. `ML.usp_RollbackPublication` withdraws a published run and re-activates what it superseded.
6. Concurrency: a filtered unique index allows only one `STARTED` run per `(RunType, ScoringWeek)`.

## Rule overlap
A candidate whose job already has a current IVT rule result (`tmp.CurrentIVTResult`, not deleted) is still published for evidence:
* `AlreadyRuleFlagged = 1`
* `RuleCodes` and `ExistingRuleResultId` are recorded
* `ReviewStatus = 'ROUTED_TO_RULE_WORKFLOW'`

`ML.vReviewQueue` (the account-manager task list) excludes these rows, so there is no second investigation task. `ML.vRuleOverlapExport` links the ML evidence to the existing rule result, read-only.

## Technology choices
* SQL Server **2014** (12.0.6179, compatibility 120) was verified. Migrations are 2012-compatible: no `CREATE OR ALTER`, no JSON functions (JSON is stored in `NVARCHAR(MAX)`), and `IF OBJECT_ID ... IS NULL` guards.
* Python 3.11, pandas 2.2.0, numpy 1.26.4, scikit-learn 1.5.2, pyodbc 5.2.0, joblib (pinned in `pyproject.toml`).
* The autoencoder is a scikit-learn `MLPRegressor` trained to reconstruct its inputs (a dense 32-16-8-16-32 bottleneck). It is deterministic with a seed, uses early stopping, and needs no GPU or TensorFlow on the IIS/SQL servers.
* Model binaries are joblib files in `IVT_ML_ARTIFACT_DIR`. The registry stores the URI, SHA-256, dependency versions and limitations; checksums are verified when a model is loaded and when it is approved.
