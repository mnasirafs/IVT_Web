"""Milestone A: read-only profiling of IVT sources. Usage: python scripts/profile_sources.py --output-dir output/profiling"""
import sys

from ivt_ml.cli import main

if __name__ == "__main__":
    sys.exit(main(["profile", *sys.argv[1:]]))
