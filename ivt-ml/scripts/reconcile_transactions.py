"""Extracts and reconciles one week (buy/sell aggregation, matching, totals report).

Usage: python scripts/reconcile_transactions.py [options]   (see: python scripts/reconcile_transactions.py --help)
Equivalent to: ivt-ml snapshot [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["snapshot", *sys.argv[1:]]))
