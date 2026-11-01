#!/usr/bin/env python3
"""Copies the plan JSON out of PracticePlanData.swift into the Windows embedded resource."""
from pathlib import Path

root = Path(__file__).resolve().parent.parent
source = (root / "Sources/LineworkCore/PracticePlanData.swift").read_text(encoding="utf-8")
start = source.index('##"""\n') + len('##"""\n')
end = source.rindex('\n"""##')
(root / "windows/Linework.Core/Plan/plan.json").write_text(source[start:end], encoding="utf-8", newline="\n")
