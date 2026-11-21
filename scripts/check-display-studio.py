#!/usr/bin/env python3
"""Fail when the Display Studio surface loses its names or its Motions grouping.

The suite in tests/UiSmoke drives the real window; this check is the cheaper static half that
also covers text the headless run cannot reach, such as the README and the live-layout picker.
"""
from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
XAML = ROOT / "src/Scoreboard.Desktop/ScorebugEditorWindow.axaml"
CODE = ROOT / "src/Scoreboard.Desktop/ScorebugEditorWindow.axaml.cs"

# The old window name and the labels it used before the Windows build settled on these words.
# Built from pieces so this file is not itself a match for the text scan below.
OLD_WINDOW_NAME = "scorebug" + " studio"
OLD_LABELS = [
    "▲ Bring forward",
    "▼ Send back",
    "Transition maker…",
    "Embed layout",
    "Embed a live layout",
    "Copy text",
    "Paste text size",
]
CURRENT_LABELS = [
    "▲ To front",
    "▼ To back",
    "Transition builder…",
    "Add live layout",
    "Clear key",
]

problems: list[str] = []


def need(text: str, needle: str, label: str) -> None:
    if needle not in text:
        problems.append(f"{label}: missing {needle!r}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        problems.append(f"{label}: still contains {needle!r}")


xaml = XAML.read_text(encoding="utf-8-sig")
code = CODE.read_text(encoding="utf-8-sig")

# 1. Display Studio is the one name, in the window, its header and the operator docs.
need(xaml, 'Title="ScorePlus — Display Studio"', "studio title")
need(xaml, "SCOREPLUS · DISPLAY STUDIO", "studio header")
surface = [*list((ROOT / "src").rglob("*.cs")), *list((ROOT / "src").rglob("*.axaml")),
           ROOT / "README.md", *list((ROOT / "docs").rglob("*.md"))]
for path in surface:
    if any(part in {"bin", "obj", "dist"} for part in path.parts) or not path.is_file():
        continue
    try:
        body = path.read_text(encoding="utf-8-sig")
    except (UnicodeDecodeError, OSError):
        continue
    if OLD_WINDOW_NAME in body.lower():
        problems.append(f"{path.relative_to(ROOT)}: says {OLD_WINDOW_NAME.title()}")

# 2. Transitions and score celebrations are one hidden host behind the Motions… button.
need(xaml, 'Content="Motions…"', "display panel")
need(xaml, 'Click="MotionWindow_Click"', "Motions button wiring")
need(xaml, 'x:Name="MotionRows" IsVisible="False"', "hidden motion host")
need(code, 'void MotionWindow_Click', "Motions handler")
need(code, "MotionRows.Parent is Panel owner", "motion host reparenting")
need(code, "MotionRows.IsVisible=false", "motion host hiding")
need(code, "_motionParent.Children.Insert", "motion host restoration")
need(code, 'Title="Transitions & score celebrations"', "Motions window title")
for control in ('x:Name="TransitionBox"', 'x:Name="ScoreVideoRows"'):
    need(xaml, control, "motion host")
motion_at = xaml.find('x:Name="MotionRows"')
transition_at = xaml.find('x:Name="TransitionBox"', motion_at)
scores_at = xaml.find('x:Name="ScoreVideoRows"', motion_at)
link_at = xaml.find('Content="Clear link"', motion_at)
if not 0 <= motion_at < transition_at < scores_at < link_at:
    problems.append("transitions and score celebrations are not grouped ahead of the layout link row")

# 3. Buttons keep the names the Windows build uses.
for label in CURRENT_LABELS:
    if label not in xaml and label not in code:
        problems.append(f"button label missing: {label!r}")
for label in OLD_LABELS:
    forbid(xaml, label, "studio markup")
    forbid(code, label, "studio code")

if problems:
    print("Display Studio surface drifted:")
    for problem in problems:
        print(f"  {problem}")
    sys.exit(1)

print("Display Studio naming, Motions grouping and button labels are current.")
