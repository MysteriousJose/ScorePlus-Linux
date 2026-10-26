# ScorePlus

**ScorePlus** is a native Windows scoreboard for live production. The main window is the operator console — every control for the game on one page — and a separate audience output shows the score, clock, and broadcast-style graphics. No browser, server, internet connection, or separately installed .NET runtime required.

Everything runs locally on one PC. Scores and clocks publish to the audience output as they change, so the operator never has to "refresh" the display mid-game.

## Highlights

- **One console, no scrolling.** The main window is the control panel: visitors on the left, home on the right, with down & distance, miscellaneous clocks, timeouts, possession, the timeout clock, and custom stats all on the same page.
- **Steppers everywhere.** Every value has its add buttons above it and subtract buttons below it, so score, timeouts, down, distance, ball spot, and custom stats all read the same way. Click the value in the middle to type an exact number.
- **Operator + audience windows.** The main window is the operator surface, and **New Panel** pops out another copy in its own window — for a second monitor or an assistant — that always drives the same game. The audience window is independent and resizable, ready for a projector or capture card.
- **Live score, clocks and names.** Score, game clock, play clock, timeout clock, shot clock, and team names update the audience instantly. **Update Output** publishes staged details such as down and distance, possession, and timeouts when you want them to change.
- **One media library with tags.** The preview sits in the top-left corner; under it is a single media dock showing thumbnail tiles for every clip, a play/pause and scrub bar, and a tag editor. Tags are folder-like buckets — a clip tagged *Ads* only appears in the scorebug video element watching *Ads*.
- **Video in the scorebug.** Add a **video** element in the studio and pick a tag; when a clip from that tag is playing it fills the box on air, and the other video boxes stay empty.
- **Scorebug sizes.** The studio canvas can be HD, Full HD, QHD, 4K UHD, vertical, or square; changing the size scales the whole layout and is remembered.
- **Event log in its own window.** The log moved out of the dock to a **Event Log** button in the top row, so you can park it on a second monitor.
- **Multiple sports.** Basketball, American Football, Baseball, Soccer, Hockey, Lacrosse, Volleyball, Tennis, and Generic presets — each with its own period label, clock, scoring buttons, and timeout count.
- **Editable sport profiles.** Change period length, number of periods, timeouts per team, scoring buttons, and count-up clocks. Built-in presets can be edited (stored as overrides you can reset), and you can duplicate, rename, or delete custom sports.
- **Scorebug Studio.** Design the on-air graphic: text, logos, shapes (rectangle/square, circle, triangle), and video boxes positioned freely, layered, resized, and colored with a full custom color picker (hex, RGB/alpha sliders, palette). Bind live data with tokens like `{{HOME_SCORE}}`, `{{GAME_CLOCK}}`, `{{DOWNS}}`, or your own stats. Every dialog in the studio follows the current theme.
- **Custom stats.** Add any stat — Turnovers, Penalties, Shots, Saves, or something unique — and track a running value. Show it on the scorebug with `{{STAT:Turnovers}}` and in the exported game report.
- **Keybinds for everything.** Assign a key combination to any control-panel button — including **Update Output** and the **live score & clocks** switch; bindings persist between sessions.
- **Themes that leave graphics alone.** Light and dark themes restyle the operator windows only; scorebug and audience artwork keep their designed colors.
- **Game safety.** 100-step undo, event history, portable save/load (`.scoreboard` JSON), and automatic recovery autosave every five seconds. The last autosave is restored silently at launch — no recovery popup — and **New Game** starts fresh.

## Getting started

ScorePlus builds to a **single executable** — there are no loose DLLs to sort through. Build it (Windows with the .NET 10 SDK and desktop targeting pack):

```sh
dotnet publish src/Scoreboard.Desktop/Scoreboard.Desktop.csproj -c Release -r win-x64 --self-contained true -o dist/ScorePlus-win-x64
```

The result is `dist/ScorePlus-win-x64/ScorePlus.exe`; double-click it to launch. The desktop project is configured for single-file publishing (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, embedded PDBs), so the output folder contains just that one file. No .NET runtime needs to be installed on the machine that runs it.

## A typical game

1. Pick the sport in the main window (or load a saved game).
2. Work the console: team names, score steppers, down & distance, clocks, timeouts, and possession are all on the page. Typing over any value sets it exactly; Enter commits it.
3. Press **Update Output** (or your keybind for it) when the staged details (down, distance, possession, timeouts) should go on air — scores, clocks, and team names are already live.
4. Open **Audience Output** and send it fullscreen to your display. Use **New Panel** if you want a second control window.
5. Optionally open **Scorebug Studio** to lay out the on-air graphic and **Sport Profiles** to tune the rules.
6. Assign shortcuts in **Keybinds…** so the actions you use most are one keystroke away.

The clock can be set by typing `MM:SS` (for example `10:30`) or a plain number of seconds. The period label can read `Quarter 1`, `Q1`, or `1`.

Preferences start on the **dark** theme with **Open the control panel in its own window** enabled, and the little heading tips ("scoring, timing, actions") are off until you turn them on in **Preferences**.

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

The core suite covers scoring, undo, clock behavior, period formats, sport profiles and overrides, custom stats and tokens, live-versus-staged publishing (including live team names), and report output. The UI smoke harness loads every window, edits a profile, drives keybinds (including the live-publish toggle), confirms multiple control panels stay in sync, verifies typed stepper overrides and add-above/subtract-below ordering, checks the unified media dock and its tag editor, proves scorebug video elements only accept media from their own tag, exercises canvas size presets, checks that new studio artwork starts with no background box, and verifies theme changes never recolor scorebug artwork.

## Status

ScorePlus is an actively evolving production tool, not yet a complete replacement for a full broadcast package. Still ahead: advanced animation authoring, rosters, sponsor reporting, network and hardware control, multiple assigned outputs, broadcast transparency, an installer, and broader reliability testing. Video sound plays through the audience output window while the preview and studio stay muted, so you never hear a cue twice; per-element playback controls are on the roadmap.
