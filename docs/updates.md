# ScorePlus Linux updates

An **installed** ScorePlus checks for a newer version when it starts — and it updates the way it was
installed. A copy run straight out of `dist/` does not check, download, or replace itself.

## How the updater knows how this copy was installed

| Install kind | Detected by | What an update is |
| --- | --- | --- |
| **Per-user tarball** (`install.sh`) | `install.json` beside the binary with `"channel": "tarball"` (older builds say `"linux"`) | `ScorePlusLinuxSetup.tar.gz` → unpacked, then `install.sh --silent` |
| **System package** (`.deb`) | `install.json` with `"channel": "deb"`, or an install dir under `/usr/` | `scoreplus_<version>_amd64.deb` → handed to `apt`/`dpkg` through the desktop's auth prompt |
| **AppImage** | the `APPIMAGE` environment variable the AppImage runtime exports | `ScorePlus-<label>_x86_64.AppImage` → the file is replaced in place |
| **Folder build** | no marker and no `APPIMAGE` | nothing: it never checks |

The marker is written by `install.sh` (per-user) and by the `.deb`. An AppImage has no writable
place to keep one, which is why it identifies itself through the environment instead. `install.json`
records `version`, `installDir` and `channel`; the updater reads all three.

## The built-in feed

Nothing has to be hosted or typed in. The app ships with this repository's feed compiled in:

```
https://api.github.com/repos/MysteriousJose/ScorePlus-Linux/releases?per_page=20
```

That is GitHub's **releases API**, not the `releases/latest` page, and the difference matters:
`latest` silently skips drafts *and* prereleases, while the API lists everything. So publishing a
release — including an `a`/`L` prerelease — is the entire update ritual.

For each release the app:

| Reads | From |
| --- | --- |
| Version | The tag, with any leading text stripped: `a1.4L` and `a1.5L` become `1.4.0` and `1.5.0`. A tag with no number in it is skipped. |
| Update asset | The asset that matches **this copy's install kind** (table above). A release without one is skipped, so an update can never half-exist. A tarball install never receives a `.deb`, and an AppImage never receives the tarball. |
| Checksum | The asset's own `digest`, which GitHub computes on upload (`sha256:…`). Nothing to publish by hand. |
| Notes | The release title, else the first non-empty line of its body, shortened to 180 characters. |

Drafts are ignored (their assets are not served), prereleases are offered, and the **highest**
version wins rather than the first entry listed, so a back-dated tag cannot hide a newer build.

If GitHub is unreachable, the API is rate-limited, or no release carries an asset for your channel,
the check is silent. An offline laptop must never nag mid-show.

## What happens when a new version is found

Every channel shares the same first three steps:

1. A themed **Update available** prompt names the new version and the notes.
2. If you accept, the channel's asset is downloaded to `$TMPDIR/ScorePlusUpdate` (usually
   `/tmp/ScorePlusUpdate`) and its SHA-256 is checked against the asset digest. A mismatch deletes
   the file and stops.
3. The app starts a small helper that waits for this copy to exit, then closes itself.

Then the channel decides:

- **Tarball** — the helper unpacks the archive, runs `install.sh --silent`, which replaces the
  binary under `~/.local/share/scoreplus`, rewrites `install.json` and refreshes the `.desktop` entry
  and icon, then relaunches the installed binary.
- **AppImage** — the helper installs the download beside the old file as `<name>.new`, renames it
  over the AppImage, and reopens it. The running copy keeps its old inode until it exits, so the
  swap never disturbs the running window, and the replacement appears complete rather than
  half-copied.
- **`.deb`** — the helper runs `pkexec apt-get install -y <file>.deb`, which shows the normal
  desktop authorisation prompt, then relaunches `/usr/bin/scoreplus`. Where `pkexec` is unavailable
  it opens the `.deb` in the distribution's package installer instead, and you confirm there.

Both the install and the relaunch write to `${TMPDIR:-/tmp}/scoreplus-update.log` — read that file
if an update ever appears to do nothing.

AppImages and tarball installs must live in a directory your user can write to. If the file cannot
be replaced there, the updater says so before downloading anything and nothing is changed.

## Using another host

**Preferences → Updates → feed address** overrides the built-in feed. Leave it empty for GitHub;
paste a URL to point at your own JSON manifest:

```json
{
  "version": "1.5.0",
  "url": "https://downloads.example.com/ScorePlusLinuxSetup.tar.gz",
  "sha256": "0AF3B5E0F7C0B9B1A1C2D3E4F5061728394A5B6C7D8E9F001122334455667788",
  "notes": "Adds macros and ticker rotation."
}
```

`version` must be greater than the installed build's, and `url` must serve the asset for **your**
install kind: the tarball for an `install.sh` copy, a `.deb` for a package copy, an `.AppImage` for
an AppImage. `sha256` is the hex digest of that file (uppercase or lowercase — omitted means the
download is trusted on HTTPS alone), and `notes` is optional. Serve it as `application/json`;
Cloudflare Pages, S3 or any static host works. The address is stored per user in
`preferences.json`, not baked into the build.

`./scripts/publish-linux-release.sh <label> --manifest` writes exactly this file beside the build
(`dist/ScorePlus-linux-x64-<label>/linux.json`) for hosts that want one. The default feed does not
need it.

## Publishing a release

See [releasing.md](releasing.md). In short: tag `a1.5L` and push the one tag. The workflow builds the
tarball, `.deb` and AppImage, boots the AppImage headless, and uploads all three plus
`ScorePlusLinuxSetup.tar.gz`. A release missing one of them only goes unnoticed by the channel that
uses it — so the workflow's final step fails the run if any expected asset is absent.

## Version numbers

The label is for humans; the updater compares numbers, and a trailing `L` is **not** compared:
`a1.4`, `a1.4L` and `a1.40` all parse to `1.4.0`. Two consequences:

- Do not publish a build whose number equals the one already out — installed copies will not see it
  as newer. `a1.4L` is `1.4.0`, so the next release must be at least `a1.5L`.
- `a1.60` is `1.60.0`, which sorts **above** `a1.4L`'s `1.4.0`. Keep the sequence monotonic and
  check the published tags before choosing the next label.

## Security notes

- The update is only started after **you** confirm it, and only for a copy that can replace itself.
- HTTPS plus the SHA-256 check protects the transfer and detects a swapped file. They do not replace
  code signing: before distributing broadly, consider signing the AppImage, the `.deb` and the
  tarball.
- A `.deb` update asks for system authorisation through `pkexec`; the tarball and AppImage updates
  never need root.
- The built-in feed trusts **MysteriousJose/ScorePlus-Linux**: anyone who can publish a release there
  can offer an update. The checksum proves the download matches GitHub's copy, not that the release
  is yours. Keep repository access tight.
- GitHub's API allows 60 unauthenticated requests an hour per address. One check per launch is far
  inside that; a roomful of machines behind one NAT still is.
- The remote control and the update feed are separate features: the remote is off by default and
  needs a password, and neither opens an inbound port for updates.
