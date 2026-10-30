#!/usr/bin/env bash
# Builds a release, writes the update manifest beside it, and (optionally) publishes
# both to a GitHub release, so "create a release" is the whole update ritual.
#
#   ./scripts/publish-release.sh 1.1.0                       # build + win.json, no upload
#   ./scripts/publish-release.sh 1.1.0 --release             # build, then gh release create/upload
#   ./scripts/publish-release.sh 1.1.0 --release --notes "Adds X and fixes Y"
#   ./scripts/publish-release.sh 1.1.0 --release --draft     # upload as a draft, feed stays put
#
# The manifest points at the stable "latest" asset URLs, so the feed address set once in
# Preferences (…/releases/latest/download/win.json) keeps working for every future release.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION=""
NOTES=""
PUBLISH=0
DRAFT=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release) PUBLISH=1; shift ;;
    --draft) DRAFT=1; PUBLISH=1; shift ;;
    --notes) NOTES="${2:-}"; shift 2 ;;
    --notes=*) NOTES="${1#--notes=}"; shift ;;
    -h|--help) awk 'NR>1 && /^#/ { sub(/^# ?/, ""); print; next } NR>1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "Unknown option $1" >&2; exit 1 ;;
    *) VERSION="$1"; shift ;;
  esac
done

if [[ -z "$VERSION" ]]; then
  echo "Give the version to release, e.g. ./scripts/publish-release.sh 1.1.0" >&2
  exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 1.1.0 (got '$VERSION')." >&2
  exit 1
fi

MAJOR="${VERSION%%.*}"
OUT="$ROOT/dist/ScorePlus-win-x64-v$MAJOR"
SETUP="$OUT/ScorePlusSetup.exe"
MANIFEST="$OUT/win.json"

# The repository the feed will be served from, read from origin so a fork works unchanged.
ORIGIN="$(git -C "$ROOT" remote get-url origin 2>/dev/null || true)"
REPO="$(printf '%s' "$ORIGIN" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')"
if [[ ! "$REPO" =~ ^[^/]+/[^/]+$ ]]; then
  echo "Could not read owner/repo from origin ('$ORIGIN')." >&2
  exit 1
fi
BASE="https://github.com/$REPO/releases/latest/download"

"$ROOT/scripts/build-release.sh" "$VERSION"
[[ -f "$SETUP" ]] || { echo "Build produced no $SETUP" >&2; exit 1; }

HASH="$(sha256sum "$SETUP" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
echo "==> checksum $HASH"

# JSON is written by python so quotes and newlines in the notes cannot break the manifest.
python - "$MANIFEST" "$VERSION" "$BASE/ScorePlusSetup.exe" "$HASH" "$NOTES" <<'PY'
import json, sys
path, version, url, sha, notes = sys.argv[1:6]
manifest = {"version": version, "url": url, "sha256": sha}
if notes.strip():
    manifest["notes"] = notes.strip()
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
print(f"    manifest {path}")
PY

TAG="v$VERSION"
echo
echo "==> Feed address for Preferences → Updates:"
echo "    $BASE/win.json"
echo "    (this release: $BASE/ScorePlusSetup.exe, tagged $TAG)"

if [[ $PUBLISH -eq 0 ]]; then
  echo
  echo "Not uploaded. Re-run with --release to create the GitHub release, or upload"
  echo "$SETUP and $MANIFEST by hand as assets named ScorePlusSetup.exe and win.json."
  exit 0
fi

command -v gh >/dev/null || { echo "GitHub CLI (gh) is not installed." >&2; exit 1; }

if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  echo "==> Release $TAG already exists, replacing its assets"
  gh release upload "$TAG" "$SETUP" "$MANIFEST" --repo "$REPO" --clobber
else
  ARGS=(release create "$TAG" "$SETUP" "$MANIFEST" --repo "$REPO" --title "ScorePlus $VERSION")
  if [[ -n "$NOTES" ]]; then ARGS+=(--notes "$NOTES"); else ARGS+=(--generate-notes); fi
  [[ $DRAFT -eq 1 ]] && ARGS+=(--draft)
  echo "==> Publishing $TAG to $REPO"
  gh "${ARGS[@]}"
fi

if [[ $DRAFT -eq 1 ]]; then
  echo
  echo "Uploaded as a draft. 'latest' still serves the previous release until you publish this one."
else
  echo
  echo "Published. Installed copies see $VERSION on their next launch (GitHub's CDN can take a few"
  echo "minutes to serve the new 'latest' assets)."
fi
