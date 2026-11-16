#!/usr/bin/env bash
# Builds the Linux releases: one publish, then every package format beside it.
#
#   ./scripts/build-linux.sh a1.1                 # tar.gz + deb + AppImage
#   ./scripts/build-linux.sh a1.1 --local         # just the bare binary in dist/ (developer path)
#   ./scripts/build-linux.sh a1.1 --tar --deb     # only these two
#   ./scripts/build-linux.sh a1.1 --appimage      # only the AppImage
#   ./scripts/build-linux.sh a1.1 --no-download   # never fetch appimagetool; skip if absent
#
# Releases are labelled like the Windows ones (a1.1) and get their own folder, so a new build never
# replaces the one you are still testing:
#
#   dist/ScorePlus-linux-x64-a1.1/
#   ├── scoreplus                 the self-contained single-file binary — run it from here, nothing to install
#   ├── scoreplus_a1.1.tar.gz     portable tarball + install.sh (per-user, no root, writes install.json)
#   ├── scoreplus_1.1.0_amd64.deb system package (needs dpkg, or falls back to ar when it is absent)
#   └── ScorePlus-a1.1-x86_64.AppImage   one file, runs on any x86_64 distro
#
# Tool requirements: dotnet 10 SDK only for --local/--tar. `dpkg-deb` for the .deb (a pure ar/tar
# fallback builds a valid .deb without it) and appimagetool + mksquashfs for the AppImage — the
# script downloads appimagetool into tools/bin when it is missing and curl is present. A missing
# packager is reported and skipped, never a hard failure, so one absent tool cannot lose the build.

set -euo pipefail

VERSION=""
WANT_TAR=0; WANT_DEB=0; WANT_APPIMAGE=0; WANT_ANY=0; LOCAL_ONLY=0; DOWNLOAD=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --local) LOCAL_ONLY=1; shift ;;
    --tar) WANT_TAR=1; WANT_ANY=1; shift ;;
    --deb) WANT_DEB=1; WANT_ANY=1; shift ;;
    --appimage|--appimag) WANT_APPIMAGE=1; WANT_ANY=1; shift ;;
    --no-download) DOWNLOAD=0; shift ;;
    -h|--help) awk 'NR>1 && /^#/ { sub(/^# ?/, ""); print; next } NR>1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "Unknown option $1" >&2; exit 1 ;;
    *) VERSION="$1"; shift ;;
  esac
done
VERSION="${VERSION:-a1.0}"
if [[ $WANT_ANY -eq 0 && $LOCAL_ONLY -eq 0 ]]; then WANT_TAR=1; WANT_DEB=1; WANT_APPIMAGE=1; fi

# Same version ritual as build-release.sh: the label names the folder and lands in InformationalVersion,
# while the assembly version stays numeric because the updater compares versions, not labels.
NUMERIC="$(printf '%s' "$VERSION" | sed -E 's/[^0-9.]+//g' | sed -E 's/^\.+//; s/\.+$//')"
if [[ -z "$NUMERIC" ]]; then
  echo "Release must carry a number (a1.1, 1.0.0, got '$VERSION')." >&2
  exit 1
fi
IFS='.' read -r -a PARTS <<< "$NUMERIC"
while [[ ${#PARTS[@]} -lt 3 ]]; do PARTS+=("0"); done
NUMERIC="${PARTS[0]}.${PARTS[1]}.${PARTS[2]}"
if [[ ! "$NUMERIC" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release must look like a1.1 or 1.0.0 (got '$VERSION')." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
OUT="$DIST/ScorePlus-linux-x64-$VERSION"
APP_BUILD="$DIST/.app-build-linux"
STAGE="$DIST/.stage-linux"
APPDIR="$DIST/.appdir-linux"
CSPROJ="$ROOT/src/Scoreboard.Desktop/Scoreboard.Desktop.csproj"
LOGO="$ROOT/src/Scoreboard.Desktop/Assets/logo.png"

# dotnet is frequently not on PATH on Arch/Devuan boxes where the SDK was unpacked by hand.
find_dotnet() {
  if [[ -n "${DOTNET:-}" ]]; then printf '%s\n' "$DOTNET"; return; fi
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return; fi
  for guess in "$HOME/.local/share/dotnet-sdk/dotnet" "$HOME/.dotnet/dotnet" /usr/share/dotnet/dotnet \
               /usr/lib/dotnet/dotnet /opt/dotnet/dotnet; do
    [[ -x "$guess" ]] && { printf '%s\n' "$guess"; return; }
  done
  echo "No dotnet SDK found. Install the .NET 10 SDK, or point DOTNET= at the binary." >&2
  exit 1
}
DOTNET_BIN="$(find_dotnet)"

echo "==> ScorePlus $VERSION (assembly $NUMERIC) → $OUT"
mkdir -p "$DIST"; rm -rf "$OUT" "$APP_BUILD" "$STAGE" "$APPDIR"; mkdir -p "$OUT"

# One publish feeds every format. The csproj already sets PublishSingleFile / self-extract, so this
# lands as a single ELF with the Avalonia + Skia natives inside it.
"$DOTNET_BIN" publish "$CSPROJ" -c Release -r linux-x64 --self-contained true \
  -p:Version="$NUMERIC" -p:AssemblyVersion="$NUMERIC.0" -p:FileVersion="$NUMERIC.0" \
  -p:InformationalVersion="$VERSION" \
  -o "$APP_BUILD" -v minimal

BIN="$APP_BUILD/ScorePlus"
[[ -f "$BIN" ]] || { echo "publish produced no binary at $BIN" >&2; exit 1; }
chmod +x "$BIN"

# ---------------------------------------------------------------- shared desktop/icon/appdata bits

desktop_file() {   # $1 = destination .desktop path
  cat > "$1" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=ScorePlus
GenericName=Scoreboard and broadcast graphics
Comment=Live production scoreboard with your own scorebug on a second screen
Exec=scoreplus %U
Icon=scoreplus
Terminal=false
StartupNotify=true
StartupWMClass=ScorePlus
Categories=Utility;AudioVideo;Video;
Keywords=scoreboard;scorebug;broadcast;scorekeeper;clock;
DESKTOP
}

# ---------------------------------------------------------------- 1. the bare binary (developer path)

install -m 0755 "$BIN" "$OUT/scoreplus"
echo "    scoreplus  $(du -h "$OUT/scoreplus" | cut -f1)  (self-contained, needs glibc >= 2.27)"
cp "$ROOT/README.md" "$OUT/README.md" 2>/dev/null || true
cp "$ROOT/docs/updates.md" "$OUT/UPDATES.md" 2>/dev/null || true

if [[ $LOCAL_ONLY -eq 1 ]]; then
  echo; echo "==> Done (bare binary, no packaging)"; ls -lh "$OUT" | sed 's/^/    /'; exit 0
fi

if [[ ! -f "$LOGO" ]]; then echo "missing icon source $LOGO" >&2; exit 1; fi

# ---------------------------------------------------------------- 2. tar.gz + install.sh (no root needed)

build_tar() {
  local dir="$STAGE/tar/scoreplus-$VERSION-linux-x64"
  mkdir -p "$dir"
  install -m 0755 "$BIN" "$dir/scoreplus"
  install -m 0644 "$LOGO" "$dir/scoreplus.png"
  desktop_file "$dir/scoreplus.desktop"
  cp "$ROOT/README.md" "$dir/README.md" 2>/dev/null || true

  cat > "$dir/install.sh" <<'INSTALL'
#!/usr/bin/env bash
# Per-user install: no root, no system directories, nothing outside XDG.
#   ./install.sh                 install (or update in place) under $XDG_DATA_HOME/scoreplus
#   ./install.sh --silent        same, non-interactive (what the in-app updater would call)
#   ./install.sh --prefix DIR    keep the files under DIR instead
#   ./install.sh --remove        uninstall
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/scoreplus"
BINHOME="${XDG_BIN_HOME:-$HOME/.local/bin}"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
SILENT=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --silent) SILENT=1; shift ;;
    --prefix) DEST="$2"; shift 2 ;;
    --remove) REMOVE=1; shift ;;
    *) echo "Unknown option $1" >&2; exit 1 ;;
  esac
done

if [[ "${REMOVE:-0}" == 1 ]]; then
  rm -f "$BINHOME/scoreplus" "$APPS/scoreplus.desktop" "$ICONS/scoreplus.png"
  rm -rf "$DEST"
  echo "Removed ScorePlus from $DEST (your saved games, layouts and preferences in"
  echo "~/.local/share/ScorePlus were left alone)."
  exit 0
fi

# Replacing the binary of a running copy would leave it running from a deleted inode; refuse instead.
if pgrep -x scoreplus >/dev/null 2>&1; then
  echo "ScorePlus is running. Quit it first, then install again." >&2
  exit 1
fi

mkdir -p "$DEST" "$BINHOME" "$APPS" "$ICONS"
install -m 0755 "$HERE/scoreplus" "$DEST/scoreplus"
install -m 0644 "$HERE/scoreplus.png" "$ICONS/scoreplus.png"
sed "s|Exec=scoreplus|Exec=$DEST/scoreplus|" "$HERE/scoreplus.desktop" > "$APPS/scoreplus.desktop"
ln -sf "$DEST/scoreplus" "$BINHOME/scoreplus"

# The marker is what tells the app it may update itself: a copy run out of a downloads folder never
# replaces itself over a working file. install.json sits beside the binary, which is where the app looks.
VERSION="$(sed -E 's/.*scoreplus-([^-]+)-linux-x64.*/\1/' <<< "$HERE")"
printf '{ "version": "%s", "installDir": "%s", "channel": "linux" }\n' "${VERSION:-unknown}" "$DEST" > "$DEST/install.json"
cp "$HERE/README.md" "$DEST/README.md" 2>/dev/null || true

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "${ICONS%/hicolor*}/hicolor" 2>/dev/null || true

echo "Installed ScorePlus ${VERSION:-} to $DEST"
echo "  run it:        scoreplus        (or from your applications menu)"
echo "  if PATH lacks $BINHOME, add it to your shell profile."
echo "  uninstall:     $HERE/install.sh --remove"
INSTALL
  chmod +x "$dir/install.sh"

  # --mode keeps the archive root-owned so extracting anywhere looks clean.
  tar -C "$STAGE/tar" --owner=0 --group=0 --numeric-owner --mode='go+rx,go-w' \
      -czf "$OUT/scoreplus_${VERSION}_linux-x64.tar.gz" "scoreplus-$VERSION-linux-x64"
  echo "    scoreplus_${VERSION}_linux-x64.tar.gz  $(du -h "$OUT/scoreplus_${VERSION}_linux-x64.tar.gz" | cut -f1)"
}

# ---------------------------------------------------------------- 3. .deb

build_deb() {
  local root data ctrl
  root="$STAGE/deb"; data="$root/data"; ctrl="$root/DEBIAN"
  rm -rf "$root"; mkdir -p "$data/usr/bin" "$data/usr/share/applications" \
    "$data/usr/share/icons/hicolor/256x256/apps" "$data/usr/share/doc/scoreplus" "$ctrl"
  install -m 0755 "$BIN" "$data/usr/bin/scoreplus"
  # The updater's marker, shipped in the package: an installed-from-deb copy may update itself too.
  printf '{ "version": "%s", "installDir": "/usr/bin", "channel": "linux" }\n' "$NUMERIC" \
    > "$data/usr/bin/install.json"
  install -m 0644 "$LOGO" "$data/usr/share/icons/hicolor/256x256/apps/scoreplus.png"
  install -m 0644 "$LOGO" "$data/usr/share/pixmaps/scoreplus.png" 2>/dev/null || true
  desktop_file "$data/usr/share/applications/scoreplus.desktop"
  cp "$ROOT/README.md" "$data/usr/share/doc/scoreplus/README.md" 2>/dev/null || true
  gzip -9nc "$ROOT/README.md" > "$data/usr/share/doc/scoreplus/README.md.gz" 2>/dev/null || true

  local maintainer size
  maintainer="$(git -C "$ROOT" config user.name 2>/dev/null || true)"; [[ -n "$maintainer" ]] || maintainer="ScorePlus Linux"
  size="$(du -sk "$data" | cut -f1)"
  # ffmpeg/pulse/v4l2 are how video, audio routing and cameras work; without them the app still runs
  # and scorekeeps, it just reports the missing feature instead of drawing it.
  cat > "$ctrl/control" <<CONTROL
Package: scoreplus
Version: $NUMERIC
Section: misc
Priority: optional
Architecture: amd64
Maintainer: $maintainer
Depends: ffmpeg
Recommends: pulseaudio-utils, v4l-utils
Installed-Size: $size
Homepage: https://github.com/MysteriousJose/ScorePlus-Linux
Description: Live production scoreboard and scorebug graphics
 Operator console plus audience output in one program: score, clocks, down and
 distance, timeouts and rosters on one page, with a scorebug you design yourself
 pushed to a second screen, projector or capture card the instant a value changes.
 .
 Video cues, camera feeds, replay export and audio routing shell out to ffmpeg
 and PulseAudio, so those features report their status instead of failing when
 the tools are missing.
CONTROL
  ( cd "$data" && find . -type f -printf '%P\0' | sort -z | xargs -0 md5sum ) > "$ctrl/md5sums"

  local deb="$OUT/scoreplus_${NUMERIC}_amd64.deb"
  if command -v dpkg-deb >/dev/null 2>&1; then
    dpkg-deb --build --root-owner-group -Zxz "$root" "$deb" >/dev/null
  else
    # A .deb is just an ar archive of three members. Built by hand it installs with dpkg/apt on any
    # Debian-family box even when the build host (Arch, Fedora) has no dpkg at all.
    echo "    (no dpkg-deb here — assembling the .deb with ar instead)"
    local tmp="$root/tarballs"; mkdir -p "$tmp"
    tar -C "$ctrl" --owner=0 --group=0 --numeric-owner -czf "$tmp/control.tar.gz" control md5sums
    tar -C "$data" --owner=0 --group=0 --numeric-owner -czf "$tmp/data.tar.gz" ./usr
    printf '2.0\n' > "$tmp/debian-binary"
    rm -f "$deb"; ( cd "$tmp" && ar rc "$deb" debian-binary control.tar.gz data.tar.gz >/dev/null )
  fi
  echo "    scoreplus_${NUMERIC}_amd64.deb  $(du -h "$deb" | cut -f1)"
}

# ---------------------------------------------------------------- 4. AppImage

# appimagetool lives in tools/bin so a release build does not depend on a distro package.
find_appimagetool() {
  if [[ -n "${APPIMAGETOOL:-}" && -x "${APPIMAGETOOL:-}" ]]; then printf '%s\n' "$APPIMAGETOOL"; return 0; fi
  if command -v appimagetool >/dev/null 2>&1; then command -v appimagetool; return 0; fi
  local cached="$ROOT/tools/bin/appimagetool-x86_64.AppImage"
  if [[ -x "$cached" ]]; then printf '%s\n' "$cached"; return 0; fi
  if [[ $DOWNLOAD -eq 1 ]] && command -v curl >/dev/null 2>&1; then
    mkdir -p "$ROOT/tools/bin"
    echo "    fetching appimagetool ..." >&2
    if curl -fL --retry 3 -o "$cached" \
        https://github.com/AppImage/appimagetool/releases/latest/download/appimagetool-x86_64.AppImage; then
      chmod +x "$cached"; printf '%s\n' "$cached"; return 0
    fi
    rm -f "$cached"
  fi
  return 1
}

build_appimage() {
  if ! command -v mksquashfs >/dev/null 2>&1; then
    echo "    SKIPPED AppImage: appimagetool needs mksquashfs (squashfs-tools)."
    echo "      sudo pacman -S squashfs-tools   /   sudo apt install squashfs-tools"
    echo "      then re-run with --appimage"
    return 0
  fi
  # AppRun + a .desktop + icon in the root is the whole contract. Everything real lives under usr/.
  mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/256x256/apps"
  install -m 0755 "$BIN" "$APPDIR/usr/bin/scoreplus"
  install -m 0644 "$LOGO" "$APPDIR/scoreplus.png"
  install -m 0644 "$LOGO" "$APPDIR/usr/share/icons/hicolor/256x256/apps/scoreplus.png"
  desktop_file "$APPDIR/scoreplus.desktop"
  cp "$APPDIR/scoreplus.desktop" "$APPDIR/usr/share/applications/scoreplus.desktop"

  cat > "$APPDIR/AppRun" <<'APPRUN'
#!/usr/bin/env bash
# The AppImage mount is read-only, so the single-file bundle unpacks its natives under ~/.net
# (IncludeNativeLibrariesForSelfExtract) rather than beside the binary — which is also why
# AppContext.BaseDirectory stays the mounted path and install.json lookups still behave.
self="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"
export LD_LIBRARY_PATH="$self/usr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
# A bundled ffmpeg would be found ahead of the system one here; see the port plan, phase 7.
export SCOREPLUS_APPDIR="$self"
exec "$self/usr/bin/scoreplus" "$@"
APPRUN
  chmod +x "$APPDIR/AppRun"

  # ffmpeg is 90 MB of the download and every distro ships it; the plan keeps it a runtime
  # dependency rather than bloating the AppImage. --bundle-ffmpeg opts in if field reports
  # ever say otherwise (the app would need the MediaProbe bundled-first lookup to use it).
  if [[ "${BUNDLE_FFMPEG:-0}" == 1 ]] && command -v ffmpeg >/dev/null 2>&1; then
    mkdir -p "$APPDIR/usr/bin/resources/ffmpeg"
    install -m 0755 "$(command -v ffmpeg)" "$APPDIR/usr/bin/resources/ffmpeg/ffmpeg"
    install -m 0755 "$(command -v ffprobe)" "$APPDIR/usr/bin/resources/ffmpeg/ffprobe" 2>/dev/null || true
    echo "    bundled ffmpeg + ffprobe into the AppImage"
  fi

  local tool; if ! tool="$(find_appimagetool)"; then
    echo "    SKIPPED AppImage: no appimagetool."
    echo "      install the parts it needs:  sudo pacman -S squashfs-tools   (Ubuntu/Debian: sudo apt install squashfs-tools)"
    echo "      then re-run with --appimage, or set APPIMAGETOOL=/path/to/appimagetool"
    return 0
  fi
  # appimagetool does not reliably name its output after the AppDir folder: newer builds apply their
  # own version rules and drop a label like "0.0.3-ci" ("should be packaged as
  # ScorePlus-x86_64.AppImage"), writing that file into the current directory. So the output path is
  # passed explicitly — that is the only guarantee of the shipped name — and the stray name is still
  # accepted as a fallback for builds that ignore the second argument.
  local named="$DIST/ScorePlus-$VERSION-x86_64.AppDir"; rm -rf "$named"; mv "$APPDIR" "$named"
  local produced="$OUT/ScorePlus-${VERSION}-x86_64.AppImage" log="$DIST/.appimagetool.log"
  echo "    building AppImage with $(basename "$tool") ..."
  if ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$named" "$produced" > "$log" 2>&1; then
    grep -vE '^(Determining|Skipping)' "$log" | sed 's/^/      /' || true
  else
    tail -12 "$log" | sed 's/^/      /'; echo "    (full log: $log)" >&2
  fi
  if [[ ! -f "$produced" ]]; then
    local stray
    for stray in "$named.AppImage" "$PWD/ScorePlus-x86_64.AppImage" "$PWD/ScorePlus-$VERSION-x86_64.AppImage"; do
      if [[ -f "$stray" ]]; then mv "$stray" "$produced"; break; fi
    done
  fi
  if [[ -f "$produced" ]]; then
    rm -rf "$named"
    echo "    ScorePlus-${VERSION}-x86_64.AppImage  $(du -h "$produced" | cut -f1)"
  else
    echo "    appimagetool produced no AppImage; leaving $named for inspection" >&2
    return 1
  fi
}

# ---------------------------------------------------------------- run it

[[ $WANT_TAR -eq 1 ]] && build_tar
[[ $WANT_DEB -eq 1 ]] && build_deb
[[ $WANT_APPIMAGE -eq 1 ]] && build_appimage
rm -rf "$STAGE" "$APP_BUILD"

if command -v sha256sum >/dev/null 2>&1; then
  ( cd "$OUT" && sha256sum scoreplus_* ScorePlus-*.AppImage 2>/dev/null > SHA256SUMS || true )
fi

echo
echo "==> Done — run one of these to check it before uploading:"
echo "    $OUT/scoreplus"
[[ $WANT_APPIMAGE -eq 1 ]] && echo "    $OUT/ScorePlus-${VERSION}-x86_64.AppImage"
echo
ls -lh "$OUT" | sed 's/^/    /'
