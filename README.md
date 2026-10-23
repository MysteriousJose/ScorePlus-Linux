# Scoreboard — Desktop Live Production

A native Windows WPF desktop scoreboard starter focused on reliable game operation. The operator console and audience output are separate native windows; no browser, server, internet connection, or separately installed runtime is needed in the published Windows build.

## Build and run

The latest prebuilt self-contained Windows x64 application is in `dist/Scoreboard-win-x64-theme-controls-fixes/`. Run `Scoreboard.exe`. The folder also includes required bundled WPF native components; keep the folder contents together. To rebuild on a development machine with the .NET 10 SDK and Windows desktop targeting pack, run:

```sh
dotnet publish src/Scoreboard.Desktop/Scoreboard.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist/Scoreboard-win-x64-theme-controls-fixes
```

Game files are portable JSON with the `.scoreboard` extension. Recovery autosaves are written under `%LOCALAPPDATA%\Scoreboard\recovery.scoreboard` every five seconds. Diagnostics are under `%LOCALAPPDATA%\Scoreboard\logs`.

## Implemented

- Native Windows operator application, dark control console, independent resizable audience-output window, and fullscreen audience output.
- Built-in Basketball, American Football, Baseball, Soccer, Hockey, Lacrosse, Volleyball, Tennis, and Generic presets.
- Home/away scoring and score correction, team names/abbreviations, period advance, timeout decrement, possession toggle, clock start/stop/correction, monotonic elapsed-time countdown, event history, 100-step undo, portable save/load, periodic recovery autosave.
- Live in-app scoreboard preview and keyboard shortcuts (Space clock, Ctrl+Z undo, left/right score).
- Scorebug Studio: persistent free-position elements, drag move/resize handles, team logos/images, text/shapes, layer order, visibility, preset color bars, live data tokens, pulse/drift animation, and shared operator/audience rendering.
- Saved light/dark preferences and an optional detached operator control panel. Football controls include downs and yards; soccer controls track yellow/red cards per team. These values are undoable and saved with the game.
- Self-contained Windows x64 publish output; no installed .NET runtime required to run the published application.

## Not yet implemented

This is an initial working desktop foundation, not a complete production replacement for ProScoreboard. The sports presets currently share a generic score/period model; sport-specific scoring/statistics beyond football downs and soccer cards, shot-clock controls, advanced vector/animation authoring, teams/rosters/media/video, overlays, sponsor reporting, network remote controls, hardware controllers, multiple assigned outputs, broadcast transparency, installer/signing, extensive help/tutorials, configurable hotkeys, and broad integration/reliability testing remain future work. The current preset list does not imply those sport-specific features are complete. Output currently uses an original built-in layout and supports one additional audience window.

## Core tests

Run `dotnet run --project tests/Scoreboard.Core.Tests/Scoreboard.Core.Tests.csproj -c Release` to exercise scoring, undo, clock behavior, period changes, sport identifiers, and portable game-file round trips.
