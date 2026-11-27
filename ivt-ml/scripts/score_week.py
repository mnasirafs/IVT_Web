"""Stage wrapper for `ivt-ml score`.

Usage: python scripts/score_week.py [options]   (see: python scripts/score_week.py --help)
Equivalent to: ivt-ml score [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["score", *sys.argv[1:]]))
