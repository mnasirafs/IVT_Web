"""Stage wrapper for `ivt-ml ingest-feedback`.

Usage: python scripts/ingest_feedback.py [options]   (see: python scripts/ingest_feedback.py --help)
Equivalent to: ivt-ml ingest-feedback [options]
"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["ingest-feedback", *sys.argv[1:]]))
