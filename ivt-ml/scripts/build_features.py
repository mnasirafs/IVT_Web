"""Stage wrapper for `ivt-ml features`.

Usage: python scripts/build_features.py [options]   (see: python scripts/build_features.py --help)
Equivalent to: ivt-ml features [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["features", *sys.argv[1:]]))
