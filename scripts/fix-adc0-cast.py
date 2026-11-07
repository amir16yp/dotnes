#!/usr/bin/env python3
"""Normalize SoftCpu ADC#0 sugar that fails CS0266 (byte += int ternary)."""
from __future__ import annotations
import sys
from pathlib import Path

OLD = "a += carry ? 1 : 0;"
NEW = "a += (byte)(carry ? 1 : 0);"


def main() -> int:
    if len(sys.argv) != 2:
        print(f"usage: {sys.argv[0]} Program.cs", file=sys.stderr)
        return 2
    path = Path(sys.argv[1])
    text = path.read_text(encoding="utf-8")
    n = text.count(OLD)
    if n == 0:
        print(f"{path}: nothing to fix")
        return 0
    path.write_text(text.replace(OLD, NEW), encoding="utf-8")
    print(f"{path}: fixed {n} ADC#0 line(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
