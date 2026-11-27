"""Runs the training stage: calibration and threshold selection use the separate calibration partition inside `ivt-ml train`.

Usage: python scripts/calibrate_and_select_thresholds.py [options]   (see: python scripts/calibrate_and_select_thresholds.py --help)
Equivalent to: ivt-ml train [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["train", *sys.argv[1:]]))
