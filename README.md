# ScorePlus

**ScorePlus** is a native Windows scoreboard for live production. The main window is the operator console — every control for the game on one page — and a separate audience output shows the score, clock, and broadcast-style graphics. No browser, server, internet connection, or separately installed .NET runtime required.

Everything runs locally on one PC. Scores and clocks publish to the audience output as they change, so the operator never has to "refresh" the display mid-game.

## Highlights

- **One console, no scrolling.** The main window is the control panel: visitors on the left, home on the right, with down & distance, miscellaneous clocks, timeouts, possession, the timeout clock, and custom stats all on the same page.
- **Steppers everywhere.** Every value has its add buttons above it and subtract buttons below it, so score, timeouts, down, distance, ball spot, and custom stats all read the same way. Click the value in the middle to type an exact number. The ball spot carries its own side — "Home 37" or "Visitors 25" — switched by a full-size **HOME ▶ / ◀ VISITORS** button under it, and it is independent of who has possession.
- **Operator + audience windows.** The main window is the operator surface, and **New Panel** pops out another copy in its own window — for a second monitor or an assistant — that always drives the same game. The audience window is independent and resizable, ready for a projector or capture card.
- **Live score, clocks and names.** Score, game clock, play clock, timeout clock, shot clock, and team names update the audience instantly. **Update Output** publishes staged details such as down and distance, possession, and timeouts when you want them to change.
- **One media library with folders, tags and playlists.** The preview sits in the top-left corner; under it is a single media dock showing thumbnail tiles for every clip, a play/pause and scrub bar, a folder filter, and a tag editor. Folders organise the library and tags route clips to the graphic — a clip tagged *Ads* only appears in the scorebug video element watching *Ads*. Playlists run their clips one after another until stopped or ended, and can repeat; any single video can also loop on its own like a stadium screen.
- **Video in the scorebug.** Add a **video** element in the studio and pick a tag; when a clip from that tag is playing it fills the box on air, and the other video boxes stay empty.
- **Layouts.** A game can hold several complete scorebug layouts. Design each one in the studio, then step through them with the arrows beside the program preview and press **Update Output** to send the chosen one to the audience. Every layout keeps reading the same live game data, so switching is instant and nothing goes stale.
- **Type and motion.** Every text element chooses from 25+ installed fonts (or imports a `.ttf`/`.otf` of its own), can be bold, italic, underlined and struck through, and can animate its value change with one of thirteen transitions — dissolve, wheel, flip, zoom, pop, slides, and more — so a score never just cuts.
- **Scorebug sizes.** The studio canvas can be HD, Full HD, QHD, 4K UHD, vertical, or square; changing the size scales the whole layout and is remembered.
- **Event log in its own window.** The log moved out of the dock to a **Event Log** button in the top row, so you can park it on a second monitor.
- **Multiple sports.** Basketball, American Football, Baseball, Soccer, Hockey, Lacrosse, Volleyball, Tennis, and Generic presets — each with its own period label, clock, scoring buttons, and timeout count.
- **Editable sport profiles.** Change period length, number of periods, timeouts per team, scoring buttons, and count-up clocks. Built-in presets can be edited (stored as overrides you can reset), and you can duplicate, rename, or delete custom sports.
- **Icons from live data.** Any text element can draw artwork instead of a number: **timeout bars** (a row of short horizontal lines, one per timeout the team has left, home or visitors) and a **possession dot** that sits on the left for the visitors and the right for the home team — with a property to mirror it. Both are chosen in the element's **ICON** section in the studio.
- **Team identity in the console.** The **…** beside each team name opens the team box: name, abbreviation, logo, primary colour and secondary colour. The logo is imported there once and is available to logo elements on every layout, and any colour on the scorebug can point at a team's primary or secondary colour instead of a fixed hex.
- **Scorebug Studio.** Design the on-air graphic: text, team logos, shapes (rectangle/square, circle, triangle), and video boxes positioned freely, layered, resized, and colored with a full custom color picker (hex, RGB/alpha sliders, palette) or a team palette slot. The layer list runs front-to-back (the top row is the highest layer) with bring-forward/send-back buttons, and the property panel only offers what applies to the selected element — a team-logo selector for logos, a media tag for video boxes, a shape picker for shapes. Bind live data with tokens like `{{HOME_SCORE}}`, `{{GAME_CLOCK}}`, `{{DOWNS}}`, `{{BALL_ON}}`, or your own stats. Each text element chooses its own quarter style (`Quarter 1`, `Q1` or `1`). Every dialog in the studio follows the current theme.
- **Custom stats.** Add any stat — Turnovers, Penalties, Shots, Saves, or something unique — and track a running value. Show it on the scorebug with `{{STAT:Turnovers}}` and in the exported game report.
- **Keybinds for everything, none by default.** Assign a key combination to any control-panel button — including **Update Output**, the **live score & clocks** switch and the ball spot's side; bindings persist between sessions, and nothing is bound until you choose it.
- **Themes that leave graphics alone.** Light and dark themes restyle the operator windows only; scorebug and audience artwork keep their designed colors.
- **Game safety.** 100-step undo, event history, portable save/load (`.spg` ScorePlus Game files), and rolling autosaves every five seconds. The launch screen lists the recoverable autosave points (one per minute of play) so you can pick one, and **New Game** clears the game and the on-air picture while leaving sport profiles, keybinds, preferences, your media library, playlists and your scorebug layouts alone — the saved design is the template every new game uses.

## Getting started

ScorePlus builds to a **single executable** — there are no loose DLLs to sort through. Build it (Windows with the .NET 10 SDK and desktop targeting pack):

```sh
dotnet publish src/Scoreboard.Desktop/Scoreboard.Desktop.csproj -c Release -r win-x64 --self-contained true -o dist/ScorePlus-win-x64-v12
```

Each build goes into its own versioned folder (`dist/ScorePlus-win-x64-v12` here) so an earlier build stays available for comparison. The result is `dist/ScorePlus-win-x64-v12/ScorePlus.exe`; double-click it to launch. The desktop project is configured for single-file publishing (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, embedded PDBs), so the output folder contains just that one file. No .NET runtime needs to be installed on the machine that runs it.

## A typical game

1. Start from the launcher: **New Game** for a clean sheet, **Open Game** for a saved `.spg`, or an autosave from the list. Pick the sport in the main window (or switch it, which also starts fresh).
2. Work the console: team names, score steppers, down & distance, clocks, timeouts, and possession are all on the page. Typing over any value sets it exactly; Enter commits it.
3. Press **Update Output** (or your keybind for it) when the staged details (down, distance, possession, timeouts) should go on air — scores, clocks, and team names are already live.
4. Open **Audience Output** and send it fullscreen to your display. Use **New Panel** if you want a second control window.
5. Optionally open **Scorebug Studio** to lay out the on-air graphic and **Sport Profiles** to tune the rules.
6. Assign shortcuts in **Keybinds…** so the actions you use most are one keystroke away.

The clock can be set by typing `MM:SS` (for example `10:30`) or a plain number of seconds. How the quarter prints (`Quarter 1`, `Q1`, or `1`) is a property of the scorebug element in the studio, not a console setting, so two graphics can print it differently.

Preferences start on the **dark** theme, and the little heading tips ("scoring, timing, actions") are off until you turn them on in **Preferences**. The console lives in the main window; **New Panel** opens an extra synced one when you actually want a second window — starting or opening a game never pops one up by itself.

## Where files live

| What | Location |
| --- | --- |
| Game files | Anywhere you choose — portable `.spg` (ScorePlus Game) files; older `.scoreboard` / `.json` games still open |
| Autosaves | `%LOCALAPPDATA%\ScorePlus\autosaves` — the newest twelve points, listed on the launch screen |
| Preferences | `%LOCALAPPDATA%\ScorePlus\preferences.json` |
| Sport profiles | `%LOCALAPPDATA%\ScorePlus\sports.json` |
| Scorebug layouts | `%LOCALAPPDATA%\ScorePlus\scorebug.json` (every layout and the one on air) |
| Imported fonts | `%LOCALAPPDATA%\ScorePlus\Fonts` |
| Team logos | `%LOCALAPPDATA%\ScorePlus\TeamLogos` |
| Keybinds | `%LOCALAPPDATA%\ScorePlus\keybinds.json` |
| Diagnostics | `%LOCALAPPDATA%\ScorePlus\logs` |

An existing installation is carried across the folder rename once: the files in `%LOCALAPPDATA%\Scoreboard` are copied into `%LOCALAPPDATA%\ScorePlus` on first run (copied, not moved, so an older build still works).

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

The core suite covers scoring, undo, clock behavior, period formats, sport profiles and overrides, custom stats and tokens, live-versus-staged publishing (including live team names, logos and colours), multiple layouts and publishing the chosen one, element typography and transitions, and report output. The UI smoke harness loads every window, edits a profile, drives keybinds (including the live-publish toggle), confirms multiple control panels stay in sync, verifies typed stepper overrides and add-above/subtract-below ordering, checks the media dock's folders, playlists and loop controls, proves scorebug video elements only accept media from their own tag, exercises canvas size presets, switches layouts through the preview arrows and publishes them, checks the font picker (25+ families), bold/italic/underline/strike and team colour slots, checks that new studio artwork starts with no background box, and verifies theme changes never recolor scorebug artwork.

## Status

ScorePlus is an actively evolving production tool, not yet a complete replacement for a full broadcast package. Still ahead: rosters, sponsor reporting, network and hardware control, multiple assigned outputs, broadcast transparency, an installer, and broader reliability testing. Video sound plays through the audience output window while the preview and studio stay muted, so you never hear a cue twice.
