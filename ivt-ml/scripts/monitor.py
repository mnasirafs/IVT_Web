"""Stage wrapper for `ivt-ml monitor`.

Usage: python scripts/monitor.py [options]   (see: python scripts/monitor.py --help)
Equivalent to: ivt-ml monitor [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["monitor", *sys.argv[1:]]))
