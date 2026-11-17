# ScorePlus Linux releases

Four files do the whole job: `scripts/build-linux.sh` builds every package from one publish,
`.github/workflows/ci.yml` proves them on every push, `.github/workflows/release-linux.yml` ships them
from a tag, and `scripts/publish-linux-release.sh` is the single command that all of it funnels into.
A release is therefore **a tag** — not an upload you assemble by hand.

Building and publishing are deliberately separate. `build-linux.sh` writes files into
`dist/ScorePlus-linux-x64-<label>/` and stops there; it never talks to GitHub. The `tests` workflow
holds `contents: read`, uploads the packages as workflow artifacts for 14 days, and publishes
nothing. Only `release-linux.yml` holds `contents: write`, and only it calls
`publish-linux-release.sh … --release`.

## Before you tag

1. **The gate has to be green.** In Actions → **tests**, the run for the commit you are about to tag
   must show `suites (ubuntu-24.04)`, `suites (windows-2022)` and then `build, package, boot` all
   passing. In `build, package, boot`, look for `ScorePlus-<label>-x86_64.AppImage` printed with a
   size; "appimagetool produced no AppImage" is the failure that this gate exists to catch.
2. **Tag the commit you tested.** A tag builds the tree at *that* tag, so an old tag rebuilds old
   code. Tags left over from earlier attempts are especially misleading here — they point at
   commits from before the packaging scripts were fixed.
3. **Pick a label that is numerically higher than anything published.** See
   [Version numbers](#version-numbers) before choosing.

Push one tag by name. `git push --tags` is a trap: every tag pushed for the first time starts its
own `release-linux` run, so a pile of stale tags means a pile of builds, most of them of old code.

## Option 1 — push a tag (the normal path)

```sh
git tag -a b1.1 -m "ScorePlus b1.1 (Linux)"
git push origin b1.1
```

`release-linux.yml` then runs, in this order, and stops at the first failure:

| Step | What it proves |
| --- | --- |
| `all three suites first` | The engine, UI and HTML suites pass on Ubuntu before a single package is built. |
| `build` | `build-linux.sh <label> --tar --deb --appimage` produces the tarball, the `.deb` and the AppImage. |
| `the AppImage boots before anything is published` | It stays up for 45 seconds under Xvfb. The pass condition is exit status **124** from `timeout`, not 0: a window still drawing means it got past `XOpenDisplay`, missing assemblies and a bad `AppRun`. |
| `the tarball installs per-user and runs` | `install.sh --silent` into a throwaway `HOME`, then the installed copy boots. |
| `ship it` | `publish-linux-release.sh <label> --release --skip-build` uploads with the run's own `GITHUB_TOKEN`, so no login step is needed. |
| `the release page serves what it claims` | Asks the API for the release and fails unless `ScorePlusLinuxSetup.tar.gz` and `SHA256SUMS` are present and every asset finished uploading. |

Prerelease is automatic on a tag: `a*` and `b*` publish as pre-release, `v*` as a full release.
That matches the project's own status — these are alpha and beta drops, and the updater offers
prereleases on purpose.

## Option 2 — run the workflow by hand

Actions → **release-linux** → **Run workflow**. Same pipeline, same gates, with three inputs:

| Input | Meaning |
| --- | --- |
| `version` | The label to publish, e.g. `b1.1`. Create and push the tag first. If the tag does not exist, `gh release create` will make one from the default branch, and it can end up pointing at a different commit than the one the run built. |
| `prerelease` | Boolean, defaults to `true`. This only applies to a manual run — a tag run takes its answer from the tag pattern. |
| `notes` | Release notes. Blank lets GitHub generate them from the commits. |

## Option 3 — publish from your own machine

Needs the .NET 10 SDK, `squashfs-tools`, and the [GitHub CLI](https://cli.github.com/) logged in
(`gh auth login`). Add `APPIMAGE_EXTRACT_AND_RUN=1` if the machine has no libfuse2 — the AppImage
then unpacks to a temporary directory instead of mounting.

```sh
./scripts/publish-linux-release.sh b1.1 --release --prerelease --notes "Adds X, fixes Y"
```

It builds the packages, copies the tarball onto the unchanging asset name the feed expects, and
creates or updates the release. Every option:

| Option | Effect |
| --- | --- |
| *(label)* | Required, first positional. It becomes the tag, the folder name, and the numeric assembly version (`b1.1` → `1.1.0`). |
| *(no flag)* | Build and print the asset list, then stop with "Not uploaded". Good for a dry run. |
| `--release` | Create or upload the release. Without it nothing reaches GitHub. |
| `--draft` | Implies `--release`, creates the release as a draft. Local only — the workflow never passes it, and drafts are not served to the updater. |
| `--prerelease`, `--pre` | Flag it as an alpha/beta. |
| `--notes "…"`, `--notes=…` | Release notes. Otherwise GitHub generates them from the commits. |
| `--formats tar,deb,appimage` | Which packages to build. The workflow always asks for all three. |
| `--skip-build` | Publish what `build-linux.sh` already made in `dist/` instead of rebuilding. |
| `--manifest` | Also write `dist/ScorePlus-linux-x64-<label>/linux.json` (version, url, sha256). Not needed for the default feed, which reads the releases API; it is for a host that wants a static manifest. |

If the release already exists, the script runs `gh release upload --clobber` instead of failing, so
re-running it replaces that label's Linux assets in place.

## What a release carries

| Asset | Why |
| --- | --- |
| `scoreplus_<label>_linux-x64.tar.gz` | Tarball with `install.sh`; per-user, no root. |
| `ScorePlusLinuxSetup.tar.gz` | The same tarball under a name that never changes, which is what an installed copy's update check asks for. Created whenever the tarball exists. |
| `scoreplus_<numeric>_amd64.deb` | Debian/Ubuntu package. `b1.1` ships as `scoreplus_1.1.0_amd64.deb`. |
| `ScorePlus-<label>_x86_64.AppImage` | One file, any x86_64 distro. |
| `SHA256SUMS` | Checksums of the tarball(s), `.deb` and AppImage. |
| `linux.json` | Only with `--manifest`. |

The bare `scoreplus` binary is not uploaded: at roughly 46 MB it is already inside all three
packages.

## Version numbers

The label is for humans; the updater compares numbers. `a1.03` and `a1.3` both parse to `1.3.0`, so
neither looks newer than the other to an installed copy, and `a1.60` is `1.60.0` — higher than
`b1.0`'s `1.0.0`, not lower. Keep the sequence monotonic and check the published tags before
picking the next label.

## Replacing or withdrawing a release

```sh
gh release upload b1.1 ScorePlus-b1.1-x86_64.AppImage --clobber   # swap one asset
gh release edit b1.1 --prerelease=false                          # promote out of prerelease
gh release delete b1.1 --cleanup-tag                             # withdraw it entirely
gh workflow run release-linux.yml -f version=b1.1                # re-cut it from the workflow
```

Because the feed takes the **highest** version rather than the most recent upload, withdrawing the
newest release makes the next-highest tag current again. Nothing has to be rebuilt for that; the
feed is the releases API itself.
