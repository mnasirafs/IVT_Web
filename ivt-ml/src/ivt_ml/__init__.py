"""IVT hybrid anomaly detection (autoencoder + logistic regression) for AFS Logistics.

Detects consignments whose charges, margin, measurements or reconciliation suggest a billing/trading
exception worth an account manager's review. It is NOT fraud detection and never modifies source data.
"""

__version__ = "0.1.0"
