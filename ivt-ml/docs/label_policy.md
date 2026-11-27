# Label policy

## Definitions
| Label | Meaning |
|---|---|
| `1` | Reviewed and **confirmed actionable exception** (IncorrectBuy, IncorrectSell, IncorrectDimensions, DuplicateCharge, MissingCharge) |
| `0` | Reviewed and **confirmed valid** (ValidRateChange, ValidTransaction) |
| `NULL` | Unreviewed, pending, unresolved, unclear or InsufficientInformation |

## What exists in IVT today (verified 2026-10-06)
* **No structured, adjudicated outcome exists.** `dbo.ReleasedConsignment.Justification` is free text. The most common values are bulk releases ("Release - as per AM 26/12", 9.9k), "Release - only Advisory for CH2" (28k), "Rate Structure change at BA side. Not an exception" (11k), blank (9k) and "mentions credit" (3.5k).
* Holds (`dbo.ConsignmentForReview`): 101,815 manual "AM custom" holds (ErrCode 9999, raised from the trading screen) and 2,306 rule-based holds. They are a **mix of manual and rule-driven holds**; a hold records concern, not an outcome.
* Rule findings (`tmp.IVT_ResultHistory`) keep one rule per job and week.

Consequently **confirmed labels can only come from `ML.ReviewFeedback`**, which this release introduces.

## Sources and quality
| Source | Quality | Label |
|---|---|---|
| `ML.ReviewFeedback` (current outcome version at the cutoff) | CONFIRMED | 1 / 0 / NULL for InsufficientInformation |
| Release whose justification matches an **approved** `ML.OutcomeTextMap` pattern | WEAK | from the pattern |
| Hold not released as of the cutoff | WEAK | 1 (hold behaviour) |
| Hold followed by a release without an interpretable outcome | — | NULL (reported) |
| Rule result | EVIDENCE_ONLY | none |
| No events | — | NULL. **Unflagged is not negative.** |

Only `ApprovedBy`-signed patterns in `ML.OutcomeTextMap` are used. **Approved 2026-10-06 (migration V008):**
`%not an exception%` → ValidRateChange (0, WEAK); `%credit%received%` → IncorrectBuy (1, WEAK). Bulk "as per AM" releases stay unmapped. These remain **weak** labels: they help the experimental classifier but never count as confirmed outcomes.

## Rules
* **Precedence:** CONFIRMED > mapped WEAK release > unreleased hold. Contradictory mapped weak outcomes for one snapshot are **excluded and counted** (`excluded_conflicts`).
* **Point in time:** at training cutoff T only events with `LabelAvailableAt ≤ T` are visible. `ML.TrainingLabel` enforces `LabelAvailableAt <= TrainingCutoff` with a CHECK constraint.
* **Matching:** events match snapshots by (customer, carrier, job, week): `EXACT`, `MAPPED` (unique job-week), `AMBIGUOUS` or `UNMATCHED`. Ambiguous or unmatched events never create labels.
* **No corrected values:** labels attach to the original snapshot. Corrected amounts in feedback are stored as outcome attributes, never as features.
* **Deleted findings are not negatives:** rule rows marked deleted are ignored, not labelled 0.

## Weak-label experimental classifier
If fewer than `MinimumReviewedValidationSample` confirmed labels exist (both classes required), training produces `CLASSIFIER_WEAK_EXPERIMENTAL`:
* Its target is "was this job manually held within 14 days?": 1 for a hold, 0 for a mature job with no hold, NULL for jobs too recent.
* It predicts **historical hold behaviour**, not confirmed exceptions.
* Its output goes only to `ML.ScoreAudit.WeakFlagScore`.
* The registry refuses to approve it, and refuses `ProvidesCalibratedProbability = 1` (also enforced by `CK_ML_ModelRegistry_ProbabilityClaim`).

## Review sampling (selection-bias control)
`ivt-ml sample --audit-csv <score_audit.csv> --output sample.csv --per-stratum 50` draws a stratified sample from four strata: `RULE_FLAGGED`, `MODEL_CANDIDATE`, `AE_CANDIDATE` and `UNFLAGGED`. Each sampled row records its `SamplingProbability`. Reviewers fill in `ReviewOutcome` / `OutcomeType` and the file is loaded with `ivt-ml ingest-feedback`. The stratum and probability are stored in `ML.ReviewFeedback`, so precision and recall can be re-weighted. The unflagged stratum is essential for any recall estimate.

## Feedback history
`ML.ReviewFeedback` is append-only:
* A trigger blocks deletes and edits to outcome fields.
* A new review is a new `OutcomeVersion`, and the previous row has `IsCurrent` set to 0.
* `InsufficientInformation` outcomes remain unlabeled.
