# ScorePlus

**ScorePlus** is a native Windows scoreboard for live production. It gives an operator a fast control panel for running a game and a separate audience output that displays the score, clock, and broadcast-style graphics — no browser, server, internet connection, or separately installed .NET runtime required.

Everything runs locally on one PC. Scores and clocks publish to the audience output as they change, so the operator never has to "refresh" the display mid-game.

## Highlights

- **Operator + audience windows.** A control panel for the operator and an independent, resizable audience window you can send fullscreen to a projector or capture card.
- **Live score and clock.** Score, game clock, and shot clock update the audience instantly. **Update Output** publishes staged details such as down and distance when you want them to change.
- **Multiple sports.** Basketball, American Football, Baseball, Soccer, Hockey, Lacrosse, Volleyball, Tennis, and Generic presets — each with its own period label, clock, scoring buttons, and timeout count.
- **Editable sport profiles.** Change period length, number of periods, timeouts per team, scoring buttons, and count-up clocks. Built-in presets can be edited (stored as overrides you can reset), and you can duplicate, rename, or delete custom sports.
- **Scorebug Studio.** Design the on-air graphic: text, logos, and shapes (rectangle/square, circle, triangle) positioned freely, layered, resized, and colored with a full custom color picker (hex, RGB/alpha sliders, palette). Bind live data with tokens like `{{HOME_SCORE}}`, `{{GAME_CLOCK}}`, `{{DOWNS}}`, or your own stats.
- **Custom stats.** Add any stat — Turnovers, Penalties, Shots, Saves, or something unique — and track a running value. Show it on the scorebug with `{{STAT:Turnovers}}` and in the exported game report.
- **Keybinds for everything.** Assign a key combination to any control-panel button; bindings persist between sessions.
- **Multiple control panels.** Open as many as you need (second monitor, assistant operator) — they all drive the same game and stay in sync.
- **Themes that leave graphics alone.** Light and dark themes restyle the operator windows only; scorebug and audience artwork keep their designed colors.
- **Game safety.** 100-step undo, event history, portable save/load (`.scoreboard` JSON), and automatic recovery autosave every five seconds.

## Getting started

ScorePlus builds to a **single executable** — there are no loose DLLs to sort through. Build it (Windows with the .NET 10 SDK and desktop targeting pack):

```sh
dotnet publish src/Scoreboard.Desktop/Scoreboard.Desktop.csproj -c Release -r win-x64 --self-contained true -o dist/ScorePlus-win-x64
```

The result is `dist/ScorePlus-win-x64/ScorePlus.exe`; double-click it to launch. The desktop project is configured for single-file publishing (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, embedded PDBs), so the output folder contains just that one file. No .NET runtime needs to be installed on the machine that runs it.

## A typical game

1. Pick the sport in the main window (or load a saved game).
2. Open **Control Panel**. Score from the Score section, run the clock from Clock & Period, and use the sport-specific section for downs, cards, fouls, or at-bats.
3. Open **Audience Output** and send it fullscreen to your display.
4. Optionally open **Scorebug Studio** to lay out the on-air graphic and **Sport Profiles** to tune the rules.
5. Assign shortcuts in **Keybinds…** so the actions you use most are one keystroke away.

The clock can be set by typing `MM:SS` (for example `10:30`) or a plain number of seconds. The period label can read `Quarter 1`, `Q1`, or `1`.

## Where files live

| What | Location |
| --- | --- |
| Game files | Anywhere you choose — portable JSON with the `.scoreboard` extension |
| Recovery autosave | `%LOCALAPPDATA%\Scoreboard\recovery.scoreboard` |
| Preferences | `%LOCALAPPDATA%\Scoreboard\preferences.json` |
| Sport profiles | `%LOCALAPPDATA%\Scoreboard\sports.json` |
| Scorebug layout | `%LOCALAPPDATA%\Scoreboard\scorebug.json` |
| Keybinds | `%LOCALAPPDATA%\Scoreboard\keybinds.json` |
| Diagnostics | `%LOCALAPPDATA%\Scoreboard\logs` |

## Project layout

```
src/Scoreboard.Core     Game state, engine, sport catalog, scorebug model, tokens, reports
src/Scoreboard.Desktop  WPF application: main window, control panel, audience, studio
tests/Scoreboard.Core.Tests   Headless checks for scoring, clocks, stats, profiles, reports
tests/UiSmoke                 Loads every window and exercises the operator flows
```

## Tests

```sh
dotnet run --project tests/Scoreboard.Core.Tests/Scoreboard.Core.Tests.csproj -c Release
dotnet run --project tests/UiSmoke/UiSmoke.csproj -c Release
```

The core suite covers scoring, undo, clock behavior, period formats, sport profiles and overrides, custom stats and tokens, and report output. The UI smoke harness loads every window, edits a profile, drives keybinds, confirms multiple control panels stay in sync, and verifies theme changes never recolor scorebug artwork.

## Status

ScorePlus is an actively evolving production tool, not yet a complete replacement for a full broadcast package. Still ahead: advanced animation authoring, rosters and media, overlays, sponsor reporting, network and hardware control, multiple assigned outputs, broadcast transparency, an installer, and broader reliability testing. The audience output currently uses one built-in layout and supports a single additional output window.
