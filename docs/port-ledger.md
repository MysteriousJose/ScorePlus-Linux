# b1.1 → Linux port ledger

One row per upstream feature. `status` is one of **ported**, **outstanding**, **deferred**, **excluded**.
The `ledger` CI job fails when a file exists under `src/` in the pinned `b1.1` tree but is absent
here and is not named in this file with an `excluded` or `deferred` status.

| Feature | Upstream files | Status | Notes |
| --- | --- | --- | --- |
| Score engine (b1.1) | `src/Scoreboard.Core/GameEngine.cs` | ported | b1.1 Core tree and suite restored. |
| Layout/rules model | `src/Scoreboard.Core/ScorebugLayout.cs`, `RuleFiles.cs` | ported | Automation, custom icons, score videos and quick layouts travel with layouts. |
| Automation | `src/Scoreboard.Desktop/AutomationRuntime.cs`, `AutomationWindow.cs` | ported | Runtime is owned by the main window and evaluated on every game change. |
| Hardware | `src/Scoreboard.Desktop/HardwareService.cs`, `HardwareSettings.cs`, `HardwareWindow.cs` | ported | Serial, simulator, MQTT and WebSocket transports are native. |
| Ad log | `src/Scoreboard.Core/AdLog.cs`, `src/Scoreboard.Desktop/AdLogWindow.cs` | ported | Window is in the header; playback counts impressions. |
| Custom icons / icon maker | `src/Scoreboard.Core/CustomIcons.cs`, `IconFiles.cs`, `src/Scoreboard.Desktop/IconMakerWindow.cs` | ported | Create, save, import and export `.spi` icons from the header. |
| Macros | `src/Scoreboard.Core/Macros.cs`, `src/Scoreboard.Desktop/MacroRunner.cs`, `MacroDock.axaml(.cs)` | ported | Macro dock is hosted in the MACROS tab. |
| League cloud | `src/Scoreboard.Core/LeagueCloud.cs`, `src/Scoreboard.Desktop/LeagueCloudService.cs`, `LeagueCloudWindow.cs` | ported | HTTP upload/feed, import and league ticker creation. |
| Ticker rotation | `src/Scoreboard.Core/TickerRotation.cs`, `src/Scoreboard.Desktop/TickerWindow.cs` | ported | Header action opens the rotation editor. |
| About | `src/Scoreboard.Desktop/AboutWindow.cs` | ported | Header `i` action. |
| Quick media bar | `src/Scoreboard.Desktop/QuickMediaBar.cs` | ported | Twelve assignable buttons hosted in the rail. |
| Operator lock | `src/Scoreboard.Desktop/OperatorLockWindow.cs` | ported | Header lock, full-window overlay and password hash in preferences. |
| Program selection | `src/Scoreboard.Desktop/ProgramSelection.cs` | ported | Per-window display/layout staging and publication. |
| Multiview / camera wall | `src/Scoreboard.Core/MultiviewLayout.cs`, `src/Scoreboard.Desktop/MultiviewWindow.cs`, `CameraWallPage.cs` | ported | Header action; camera tiles use the ffmpeg/v4l2 capture path. |
| Replay booth + feed | `src/Scoreboard.Desktop/ReplayBooth.cs`, `ReplayBoothWindow.cs`, `ReplayFeed.cs` | ported | Header action; history is sampled from the live game clocks. |
| Horn | `src/Scoreboard.Desktop/HornPlayer.cs` and `Assets/horn-*.mp3` | ported | Three shipped horn assets; playback uses the PulseAudio router. |
| Speaker routing / meter | `src/Scoreboard.Desktop/SpeakerRoutingWindow.cs`, `AudioRouter.cs`, `SoundLevelMeter.cs` | ported | `pactl` sinks and ffplay output; meter listens to the default monitor. |
| Speaker buses | `src/Scoreboard.Desktop/MediaSession.cs`, `Media.cs`, `MediaDock.axaml(.cs)` | ported | Tag-routed output buses with add, route, on/off and level; clips, video surfaces, microphones and the background bed all pass through the matching bus. |
| Background audio bed | `src/Scoreboard.Desktop/MediaSession.cs` | ported | A second player keeps an audio clip under a video, camera or microphone; Stop and Clear end it. |
| Clip switch settings | `src/Scoreboard.Desktop/MediaSession.cs`, `MediaDock.axaml(.cs)`, `ScorebugCanvasView.cs` | ported | Per-clip Cut/Fade/Slide/Zoom and a 0–5s duration; staged hand-overs are preloaded before Update Output. |
| Media transport + playlists | `src/Scoreboard.Desktop/MediaSession.cs`, `Media.cs` | ported | Per-clip volume, speaker and loop; playlist add/remove/reorder/loop; Clear off air blanks the screen but keeps the library. |
| Phone remote | `src/Scoreboard.Desktop/RemoteControl.cs`, `RemotePage.cs` | ported | Local-network `/watch` console for game state, media transport and per-display layout; `MainWindow` hosts `IRemoteDisplayHost`. |
| Fullscreen / geometry | `src/Scoreboard.Desktop/OutputWindow.axaml.cs`, `ScreenFit.cs` | ported | F11/Escape and monitor fitting are implemented natively. |
| WPF fullscreen helper | `src/Scoreboard.Desktop/WindowFullscreen.cs` | excluded | Replaced by native F11/Escape and monitor fitting. |
| WPF icon name converter | `src/Scoreboard.Desktop/IconNameConverter.cs` | excluded | Avalonia binds the model labels directly. |
| WPF motion name converter | `src/Scoreboard.Desktop/MotionNameConverter.cs` | excluded | Avalonia binds the model labels directly. |
| Video playback | `src/Scoreboard.Desktop/LinuxMedia.cs` | ported | ffmpeg decodes frames into an Avalonia `WriteableBitmap`; ffprobe supplies duration. |
| Camera/replay recording | `src/Scoreboard.Desktop/WpfCompat.cs` (ffmpeg-backed capture/writer) | ported | `VideoCapture`/`VideoWriter` shell out to ffmpeg; OpenCV is no longer required. |
| Chroma key | `src/Scoreboard.Desktop/ChromaKeyEffect.cs`, `Assets/ChromaKey.ps` | ported | The shader is replaced by an ffmpeg `colorkey` filter in the decoder; the studio colour and tolerance reach score videos and video surfaces. |
| Telestrator | `src/Scoreboard.Desktop/AnnotationBoard.cs` | ported | Native Avalonia pointer canvas, one ink board per display, hold fade and dismiss; `T` opens it on the audience window. |
| Native file dialogs | runtime (no upstream file) | ported | zenity, then kdialog, then the app's typed-path window; every save/open/import/export site uses them, including multi-file import. |
| Microphone capture | `src/Scoreboard.Desktop/MicrophoneFeed.cs`, `MicrophonePickerWindow.cs`, `MicrophoneRoutingWindow.cs` | ported | `parec` piped to `pacat` on the chosen PulseAudio sink; loopback monitors are hidden and the dock level scales the stream. |
| Image surfaces | `src/Scoreboard.Desktop/WpfCompat.cs` image shims | ported | Real Avalonia render targets, crops, scaled stills and Skia JPEG encoding for the camera wall and scorebug transitions. |
| Windows installer | `src/ScorePlus.Setup/**` | excluded | Replaced by AppImage, `.deb` and tar.gz packaging. |
| Marketing site | `site/**`, `.github/workflows/pages.yml` | excluded | Not application code. |
| HTML surface | `src/Scoreboard.Desktop/HtmlSurface.cs` | ported | Headless Chromium/Chrome screenshots with virtual-time animation sampling; RGBA frames are captured to Avalonia images, with an explained placeholder when no browser is installed. |
| WPF animations | `BeginAnimation` call sites | excluded | Avalonia clock implementation is permanent. |

## Promotion

`linux` is protected by PR + green CI; the release workflow only runs for `a*L` and `v*` tags.
`upstream-b1.1` is pinned at `17fc7a3`. Build and install the tar.gz, `.deb` and AppImage, run the
manual video/audio/camera/remote checklist, and only then merge `linux-dev` into `linux`.
