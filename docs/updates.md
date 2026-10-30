# ScorePlus updates

An **installed** ScorePlus checks for a newer version when it starts. A build run straight out of
`dist/` does not: it never downloads, never replaces itself, and never nags. That difference is the
marker the installer writes — the registry key `HKCU\Software\ScorePlus\InstallDir` (and an
`install.json` beside the exe as a fallback).

## What the app reads

Set **Preferences → Updates → feed address** to a URL that returns this JSON:

```json
{
  "version": "1.4.0",
  "url": "https://downloads.example.com/ScorePlusSetup-1.4.0.exe",
  "sha256": "0AF3B5E0F7C0B9B1A1C2D3E4F5061728394A5B6C7D8E9F001122334455667788",
  "notes": "Adds baseball runner icons and a faster replay scrub."
}
```

| Field | Meaning |
| --- | --- |
| `version` | The released version, `major.minor.patch`. It must be greater than the installed build's version or nothing happens. |
| `url` | The setup exe for that release. `ScorePlusSetup.exe` from the release build is exactly what goes here. |
| `sha256` | Hex SHA-256 of that file, uppercase or lowercase. When present, a download that does not match is deleted and never run. Leave it out only if you cannot publish a checksum — the app will then rely on HTTPS. |
| `notes` | One or two sentences shown in the update prompt. Optional. |

Serve it as `application/json` with a short cache time. GitHub Releases, Cloudflare Pages, S3 or any
static host works; the app makes one HTTPS GET and needs no account.

## What happens when a new version is found

1. A themed **Update available** prompt names the new version and the notes.
2. If you accept, the setup exe is downloaded to `%TEMP%\ScorePlusUpdate` and its SHA-256 is checked.
3. The installer starts with `/SILENT /CLOSEAPPLICATIONS /NORESTART`. It closes the running ScorePlus,
   unpacks the new files over the old ones in the same folder, refreshes the registry entry and exits.
4. The app closes itself just before handing over, so nothing is left locked.

Reopen ScorePlus from the Start Menu afterwards. The version in `Preferences → Updates` tells you what
is installed.

## Publishing a release

`scripts/publish-release.sh` does the whole ritual: build, checksum, manifest, upload.

```sh
./scripts/publish-release.sh 1.4.0 --release                        # build + publish to GitHub
./scripts/publish-release.sh 1.4.0 --release --notes "Adds X, fixes Y"
./scripts/publish-release.sh 1.4.0                                  # build + manifest only
./scripts/publish-release.sh 1.4.0 --release --draft                # upload, stay unpublished
```

It runs `build-release.sh`, hashes the setup exe, writes `win.json` beside the build with that
checksum (JSON-escaped, so quotes and line breaks in the notes are safe), and — with `--release` —
creates the tag `v1.4.0` and uploads **`ScorePlusSetup.exe`** and **`win.json`** as release assets
through the GitHub CLI. Without `--notes` it lets `gh --generate-notes` write them from the commits.
If the tag already exists, its two assets are replaced instead of failing. The repository is read
from `origin`, so a fork publishes to itself.

The manifest points at the stable `latest` asset URLs, which is what makes one feed address last
forever:

```
https://github.com/lhultimate/ScorePlus/releases/latest/download/win.json
```

Set that once in **Preferences → Updates**, and every later release only needs `--release`. Drafts
and prereleases do not move `latest`, so a `--draft` upload stays invisible to installed copies
until you publish it. Asset names must not change between releases; `latest` resolves by name.
GitHub's CDN caches `latest` for a few minutes after publishing.

On another host (Cloudflare Pages, S3, your own web server) the same two files work: upload
`ScorePlusSetup.exe` and `win.json`, and set the feed to wherever `win.json` landed. The feed address
is stored per user in `preferences.json`; it is not baked into the build, so a test install can point
at a staging manifest without a rebuild. Keep the previous setup exe available for one release so
anyone mid-update can retry.

## Security notes

- The update is only started after **you** confirm it.
- HTTPS plus the SHA-256 check protects the transfer and detects a swapped file. They do not replace
  code signing: before distributing broadly, sign `ScorePlusSetup.exe` with an Authenticode
  certificate (`signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 ScorePlusSetup.exe`) and
  sign the manifest's installer. Windows and antivirus trust a signed publisher far more readily.
- Keep the manifest on a host you control. Anyone who can edit it can offer an update; the checksum
  only protects against a corrupted download, not against a malicious publisher.
- The remote control and the update feed are separate features: the remote is off by default and
  needs a password, and neither opens an inbound port for updates.
