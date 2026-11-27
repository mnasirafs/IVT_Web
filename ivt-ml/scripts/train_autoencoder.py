"""Runs the training stage: the autoencoder is trained inside `ivt-ml train` together with its benchmarks.

Usage: python scripts/train_autoencoder.py [options]   (see: python scripts/train_autoencoder.py --help)
Equivalent to: ivt-ml train [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["train", *sys.argv[1:]]))
