"""Builds point-in-time peer baselines and features for one week (baselines are part of the shared feature step).

Usage: python scripts/build_peer_baselines.py [options]   (see: python scripts/build_peer_baselines.py --help)
Equivalent to: ivt-ml features [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["features", *sys.argv[1:]]))
