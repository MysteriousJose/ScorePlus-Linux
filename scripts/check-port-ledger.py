#!/usr/bin/env python3
"""Fail when a tracked b1.1 source file is absent here and not named in the port ledger."""
from __future__ import annotations

import pathlib
import subprocess
import sys

REFERENCE = "b1.1"
LEDGER = pathlib.Path("docs/port-ledger.md")

try:
    subprocess.run(["git", "rev-parse", "--verify", REFERENCE], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
except subprocess.CalledProcessError:
    print(f"SKIP: reference tag {REFERENCE} is not available in this checkout")
    sys.exit(0)

ledger = LEDGER.read_text(encoding="utf-8") if LEDGER.exists() else ""
tracked = subprocess.run(
    ["git", "ls-tree", "-r", "--name-only", REFERENCE, "--", "src"],
    check=True, text=True, capture_output=True).stdout.splitlines()

def renamed(name: str) -> list[str]:
    """The port renamed WPF XAML surfaces and folded a few helper files into native windows."""
    candidates = [name]
    if name.endswith(".xaml.cs"):
        candidates.append(name[:-8] + ".axaml.cs")
        candidates.append(name[:-8] + ".cs")
    if name.endswith(".xaml"):
        candidates.append(name[:-5] + ".axaml")
        candidates.append(name[:-5] + ".cs")
    return candidates

missing = []
for name in tracked:
    if any(pathlib.Path(candidate).exists() for candidate in renamed(name)):
        continue
    # A row naming the file is enough only when that row says the file is deliberately
    # excluded or deferred; an outstanding row does not make the gate green.
    for line in ledger.splitlines():
        lower = line.lower()
        if ("deferred" not in lower and "excluded" not in lower):
            continue
        if name in line or (name.startswith("src/ScorePlus.Setup/") and "src/ScorePlus.Setup/" in line):
            break
    else:
        missing.append(name)

if missing:
    print("Missing b1.1 source files with no deferred/excluded ledger row:")
    for name in sorted(missing):
        print(f"  {name}")
    sys.exit(1)

print(f"Ledger complete for {len(tracked)} upstream source files.")
