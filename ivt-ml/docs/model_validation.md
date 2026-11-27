# Model validation

## Method
* **Chronological partitions** over feature weeks: train (55%) → selection (15%) → calibration/threshold (15%) → untouched test (15%), with a 1-week gap between partitions. Any TransactionKey seen in an earlier partition is dropped from later ones.
* **Benchmarks** on the selection partition: autoencoder vs robust peer deviation (max |robust z|) vs Isolation Forest. The autoencoder is only recommended for promotion if it ranks best (`ae_promotion_supported`).
* **Contamination sensitivity**: the AE is retrained with suspected anomalies included; rank correlation and top-1% overlap are reported.
* **Out-of-sample AE features**: expanding chronological folds (2-week blocks, 1-week gap). Classifier training never sees in-sample AE scores.
* **Classifier**: L2 logistic regression, C chosen on the selection partition by PR-AUC, `class_weight='balanced'` (recorded; raw scores are therefore uncalibrated).
* **Calibration**: sigmoid on the calibration partition only, and only with ≥ `MinimumCalibrationSample` labelled rows.
* **Threshold**: lowest threshold whose Wilson 95% lower precision bound ≥ `MinimumPrecisionLowerBound`, chosen on the calibration partition. The test partition is evaluated once.
* **Metrics** are computed on reviewed/labelled rows only: PR-AUC, Brier, reliability bins, precision at threshold and at capacity with Wilson intervals, recall among reviewed positives. Recall over the whole population is never reported.

## What the current evidence can and cannot support
* No confirmed review outcomes exist yet, so every real-data classifier is **weak-label experimental**: its metrics measure how well it reproduces historical manual-hold behaviour, not exception precision.
* AE percentiles describe unusualness relative to the frozen reference set. They are not probabilities.
* Production readiness requires: confirmed labels (both classes, ≥ `MinimumReviewedValidationSample`), validated calibration, a precision lower bound meeting policy, and business-approved materiality, caps and currency mapping. **None of these is met today.**

## Results
See `training_report_<run>.json` for every run. The real-data shadow run results are summarised in the handover and appended below when available.
