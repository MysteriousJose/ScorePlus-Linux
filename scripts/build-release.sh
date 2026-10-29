#!/usr/bin/env bash
# Builds a release: the self-contained app, then a setup exe with that app embedded.
#
#   ./scripts/build-release.sh [version]
#
# The folder is named from the major version, so 1.0.0 lands in dist/ScorePlus-win-x64-v1 and a
# later 1.4.2 updates the same folder, while 2.0.0 starts dist/ScorePlus-win-x64-v2.
set -euo pipefail

VERSION="${1:-1.0.0}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 1.0.0 (got '$VERSION')." >&2
  exit 1
fi
MAJOR="${VERSION%%.*}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
OUT="$DIST/ScorePlus-win-x64-v$MAJOR"
SETUP_BUILD="$DIST/.setup-build"
PAYLOAD="$ROOT/src/ScorePlus.Setup/payload.zip"

echo "==> ScorePlus $VERSION → $OUT"
mkdir -p "$DIST"
rm -rf "$OUT" "$SETUP_BUILD"
mkdir -p "$OUT"

dotnet publish "$ROOT/src/Scoreboard.Desktop/Scoreboard.Desktop.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:Version="$VERSION" -p:AssemblyVersion="$VERSION.0" -p:FileVersion="$VERSION.0" \
  -o "$OUT" -v minimal

# The installer carries the finished app as a compressed payload, so one exe is all it needs.
python - "$OUT/ScorePlus.exe" "$PAYLOAD" <<'PY'
import sys, zipfile, os
app, payload = sys.argv[1], sys.argv[2]
if not os.path.exists(app):
    raise SystemExit(f"missing published app: {app}")
with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    archive.write(app, "ScorePlus.exe")
print(f"    payload {os.path.getsize(payload) / 1048576:.1f} MB")
PY

dotnet publish "$ROOT/src/ScorePlus.Setup/ScorePlus.Setup.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:Version="$VERSION" -p:AssemblyVersion="$VERSION.0" -p:FileVersion="$VERSION.0" \
  -o "$SETUP_BUILD" -v minimal

cp "$SETUP_BUILD/ScorePlusSetup.exe" "$OUT/ScorePlusSetup.exe"
rm -rf "$SETUP_BUILD"
rm -f "$PAYLOAD"
cp "$ROOT/README.md" "$OUT/README.md" 2>/dev/null || true
cp "$ROOT/docs/updates.md" "$OUT/UPDATES.md" 2>/dev/null || true

echo
echo "==> Done"
ls -lh "$OUT" | sed 's/^/    /'
