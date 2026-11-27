"""Runs the training stage: the hybrid classifier is trained on out-of-sample AE features inside `ivt-ml train`.

Usage: python scripts/train_classifier.py [options]   (see: python scripts/train_classifier.py --help)
Equivalent to: ivt-ml train [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["train", *sys.argv[1:]]))
