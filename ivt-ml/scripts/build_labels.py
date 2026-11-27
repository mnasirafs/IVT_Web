"""Stage wrapper for `ivt-ml labels`.

Usage: python scripts/build_labels.py [options]   (see: python scripts/build_labels.py --help)
Equivalent to: ivt-ml labels [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["labels", *sys.argv[1:]]))
