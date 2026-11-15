#!/usr/bin/env bash
# The Linux half of publish-release.sh: build the packages, write the update manifest, and
# (optionally) push every asset onto a GitHub release.
#
#   ./scripts/publish-linux-release.sh a1.2                        # build + linux.json, no upload
#   ./scripts/publish-linux-release.sh a1.2 --release              # then gh release create/upload
#   ./scripts/publish-linux-release.sh a1.2 --release --prerelease --notes "Linux: fixes X"
#   ./scripts/publish-linux-release.sh a1.2 --release --formats tar,appimage
#   ./scripts/publish-linux-release.sh a1.2 --skip-build --release # ship what build-linux.sh already made
#   ./scripts/publish-linux-release.sh a1.2 --release --manifest   # also write a win.json-style linux.json
#
# CI runs this same script from .github/workflows/release-linux.yml, so "cut a Linux release" is one
# command whether it comes from a tag, from Actions, or from your own box. gh is only needed to upload;
# in Actions GITHUB_TOKEN is already in the environment, so no login step is required.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION=""
NOTES=""
PUBLISH=0
DRAFT=0
PRE=0
SKIP_BUILD=0
WANT_MANIFEST=0
FORMATS="tar,deb,appimage"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release) PUBLISH=1; shift ;;
    --draft) DRAFT=1; PUBLISH=1; shift ;;
    --prerelease|--pre) PRE=1; shift ;;
    --notes) NOTES="${2:-}"; shift 2 ;;
    --notes=*) NOTES="${1#--notes=}"; shift ;;
    --formats) FORMATS="${2:-}"; shift 2 ;;
    --formats=*) FORMATS="${1#--formats=}"; shift ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --manifest) WANT_MANIFEST=1; shift ;;
    -h|--help) awk 'NR>1 && /^#/ { sub(/^# ?/, ""); print; next } NR>1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "Unknown option $1" >&2; exit 1 ;;
    *) VERSION="$1"; shift ;;
  esac
done
[[ -n "$VERSION" ]] || { echo "Give the release, e.g. ./scripts/publish-linux-release.sh a1.2" >&2; exit 1; }

NUMERIC="$(printf '%s' "$VERSION" | sed -E 's/[^0-9.]+//g' | sed -E 's/^\.+//; s/\.+$//')"
IFS='.' read -r -a PARTS <<< "$NUMERIC"
while [[ ${#PARTS[@]} -lt 3 ]]; do PARTS+=("0"); done
NUMERIC="${PARTS[0]}.${PARTS[1]}.${PARTS[2]}"
if [[ ! "$NUMERIC" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release must look like a1.2 (got '$VERSION')." >&2
  exit 1
fi

# Ask build-linux.sh for exactly the formats we were asked to ship, then look for those files.
if [[ $SKIP_BUILD -eq 0 ]]; then
  IFS=',' read -r -a WANT_LIST <<< "$FORMATS"
  FORMAT_ARGS=()
  for want in "${WANT_LIST[@]}"; do
    case "$want" in tar|deb|appimage) FORMAT_ARGS+=("--$want") ;; esac
  done
  [[ ${#FORMAT_ARGS[@]} -gt 0 ]] || FORMAT_ARGS=(--tar --deb --appimage)
  "$ROOT/scripts/build-linux.sh" "$VERSION" "${FORMAT_ARGS[@]}"
fi

OUT="$ROOT/dist/ScorePlus-linux-x64-$VERSION"
TARBALL="$OUT/scoreplus_${VERSION}_linux-x64.tar.gz"
DEB="$OUT/scoreplus_${NUMERIC}_amd64.deb"
APPIMAGE="$OUT/ScorePlus-${VERSION}-x86_64.AppImage"
MANIFEST="$OUT/linux.json"
[[ -f "$OUT/scoreplus" ]] || { echo "build produced nothing in $OUT" >&2; exit 1; }

# The updater wants one asset name that never changes, the same reason the Windows feed always asks
# for ScorePlusSetup.exe. Every release therefore carries a second, stable-named copy of the tarball
# beside the version-named one; install.sh inside it is what actually swaps the files.
STABLE="$OUT/ScorePlusLinuxSetup.tar.gz"
[[ -f "$TARBALL" ]] && cp -f "$TARBALL" "$STABLE"

ORIGIN="$(git -C "$ROOT" remote get-url origin 2>/dev/null || true)"
REPO="$(printf '%s' "$ORIGIN" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')"
if [[ ! "$REPO" =~ ^[^/]+/[^/]+$ ]]; then
  echo "Could not read owner/repo from origin ('$ORIGIN')." >&2
  exit 1
fi
BASE="https://github.com/$REPO/releases/latest/download"

if [[ $WANT_MANIFEST -eq 1 && -f "$STABLE" ]]; then
  HASH="$(sha256sum "$STABLE" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
  echo "==> checksum $HASH"
  # JSON is written by python so quotes and newlines in the notes cannot break the manifest.
  python3 - "$MANIFEST" "$NUMERIC" "$BASE/ScorePlusLinuxSetup.tar.gz" "$HASH" "$NOTES" <<'PY'
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
else
  # UpdateChecker reads a GitHub releases array and takes the checksum off the asset itself, so there
  # is no manifest to publish. --manifest writes one anyway for a host that needs a linux.json.
  echo "==> no linux.json (the feed is this repo's releases API; --manifest writes a file anyway)"
  rm -f "$MANIFEST"
fi

ASSETS=()
for asset in "$TARBALL" "$STABLE" "$DEB" "$APPIMAGE" "$MANIFEST" "$OUT/SHA256SUMS"; do
  [[ -f "$asset" ]] && ASSETS+=("$asset")
done
echo
echo "==> Assets for $VERSION:"
printf '    %s\n' "${ASSETS[@]##*/}"
echo "    feed: https://api.github.com/repos/$REPO/releases?per_page=20"
if [[ $WANT_MANIFEST -eq 1 ]]; then echo "    manifest: $BASE/linux.json"; fi

if [[ $PUBLISH -eq 0 ]]; then
  echo
  echo "Not uploaded. Re-run with --release to push these onto the release."
  exit 0
fi

command -v gh >/dev/null || { echo "GitHub CLI (gh) is not installed." >&2; exit 1; }
TAG="$VERSION"
if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  echo "==> Release $TAG already exists, replacing its Linux assets"
  gh release upload "$TAG" "${ASSETS[@]}" --repo "$REPO" --clobber
else
  ARGS=(release create "$TAG" "${ASSETS[@]}" --repo "$REPO" --title "ScorePlus $VERSION (Linux)")
  [[ -n "$NOTES" ]] && ARGS+=(--notes "$NOTES") || ARGS+=(--generate-notes)
  [[ $DRAFT -eq 1 ]] && ARGS+=(--draft)
  [[ $PRE -eq 1 ]] && ARGS+=(--prerelease)
  echo "==> Publishing $TAG to $REPO"
  gh "${ARGS[@]}"
fi
echo
echo "==> Uploaded."
