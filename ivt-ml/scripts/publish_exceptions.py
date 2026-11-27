"""Stage wrapper for `ivt-ml publish`.

Usage: python scripts/publish_exceptions.py [options]   (see: python scripts/publish_exceptions.py --help)
Equivalent to: ivt-ml publish [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["publish", *sys.argv[1:]]))
