"""Runs the training stage: chronological out-of-sample AE scores are produced inside `ivt-ml train`.

Usage: python scripts/generate_oof_ae_scores.py [options]   (see: python scripts/generate_oof_ae_scores.py --help)
Equivalent to: ivt-ml train [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["train", *sys.argv[1:]]))
