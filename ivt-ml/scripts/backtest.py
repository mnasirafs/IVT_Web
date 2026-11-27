"""Stage wrapper for `ivt-ml backtest`.

Usage: python scripts/backtest.py [options]   (see: python scripts/backtest.py --help)
Equivalent to: ivt-ml backtest [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["backtest", *sys.argv[1:]]))
