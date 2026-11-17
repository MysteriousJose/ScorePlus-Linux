using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Scoreboard.Desktop;

/// <summary>What this build is, and whether it came from the installer.</summary>
public static class AppInfo
{
    /// <summary>The build's version, from the assembly the publish set. The installer writes the same one.</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}"
        : "1.0.0";

    /// <summary>
    /// The release the operator knows this build by — a1.1, a1.02 — written into the assembly by the
    /// release build. Assembly versions stay numeric because the updater compares them, so the label
    /// lives beside it rather than replacing it.
    /// </summary>
    public static string Release { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } label && !string.Equals(label, Version, StringComparison.OrdinalIgnoreCase)
            ? label.Split('+')[0]
            : Version;

    /// <summary>
    /// The marker install.sh and the .deb write beside the binary. It is what says "this copy may
    /// replace itself": a build run out of dist/ has none, and never checks.
    /// </summary>
    public const string MarkerFileName = "install.json";

    public static string MarkerPath => Path.Combine(AppContext.BaseDirectory, MarkerFileName);

    /// <summary>True for a copy install.sh or the .deb installed.</summary>
    public static bool IsInstalled
    {
        get
        {
            try { return File.Exists(MarkerPath); }
            catch { return false; }
        }
    }

    /// <summary>
    /// How this copy got here, which decides what an update IS: a tarball install, a .AppImage file,
    /// or a package the system owns. Detected from the marker and the environment, never assumed —
    /// defaulting every copy to the tarball would hand an AppImage user a tarball they never asked
    /// for and overwrite the file they actually run.
    /// </summary>
    public static InstallKind Kind { get; } = DetectKind();

    /// <summary>True when this copy can replace itself. A folder build cannot.</summary>
    public static bool CanUpdate => Kind != InstallKind.Folder;

    /// <summary>The .AppImage path the runtime exported, or null for every other install kind.</summary>
    public static string? AppImagePath
    {
        get
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("APPIMAGE");
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Where the binary lives, as recorded by the installer. The updater writes there, so a .deb
    /// copy (/usr/bin, not writable by the user) is reported rather than half-updated.
    /// </summary>
    public static string? InstallDir => ReadMarker("installDir");

    /// <summary>The label the installer recorded, for the Preferences line.</summary>
    public static string? InstalledLabel => ReadMarker("version");

    /// <summary>The channel the installer recorded, when it wrote one.</summary>
    public static string? Channel => ReadMarker("channel");

    static InstallKind DetectKind()
    {
        try
        {
            // The AppImage runtime exports APPIMAGE with the file it mounted, so an AppImage
            // identifies itself even though it has no writable place to keep a marker.
            if (AppImagePath is not null) return InstallKind.AppImage;
            if (!IsInstalled) return InstallKind.Folder;

            if (string.Equals(Channel, "deb", StringComparison.OrdinalIgnoreCase)) return InstallKind.SystemPackage;
            if (string.Equals(Channel, "tarball", StringComparison.OrdinalIgnoreCase)) return InstallKind.Tarball;

            // Builds published before channels were recorded say "linux" and differ only by where
            // they were put, so the install directory decides.
            var directory = InstallDir;
            return directory is not null
                && (string.Equals(directory, "/usr/bin", StringComparison.Ordinal)
                    || directory.StartsWith("/usr/", StringComparison.Ordinal))
                ? InstallKind.SystemPackage
                : InstallKind.Tarball;
        }
        catch { return InstallKind.Folder; }
    }

    static string? ReadMarker(string property)
    {
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(MarkerPath));
            return marker.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch { return null; }
    }
}

/// <summary>How this copy got onto the machine, which decides what an update is.</summary>
public enum InstallKind
{
    /// <summary>Run out of a folder or a build tree: nothing to replace.</summary>
    Folder,

    /// <summary>Per-user install.sh from the tarball.</summary>
    Tarball,

    /// <summary>A .AppImage file, replaced in place.</summary>
    AppImage,

    /// <summary>A .deb package, updated through the package manager.</summary>
    SystemPackage,
}

/// <summary>One release as described by the update feed.</summary>
public sealed record UpdateManifest(string Version, string Url, string? Sha256, string? Notes);

/// <summary>
/// The feed an installed copy asks by default: this project's own releases, newest first. It is the
/// GitHub API rather than a hand-uploaded linux.json so that publishing a release — a prerelease
/// included — is the entire update ritual, and nothing can go stale or be forgotten.
/// </summary>
public static class UpdateFeed
{
    public const string BuiltIn = "https://api.github.com/repos/MysteriousJose/ScorePlus-Linux/releases?per_page=20";

    /// <summary>
    /// The one stable name every Linux release carries: a copy of the tarball, which contains
    /// install.sh. Matched on the whole name because <c>.tar.gz</c> is two extensions and a
    /// base-name match would look for <c>ScorePlusLinuxSetup.tar</c>.
    /// </summary>
    public const string SetupAssetName = "ScorePlusLinuxSetup.tar.gz";

    /// <summary>
    /// The asset a given install kind updates from. Each channel ships under its own name, so the
    /// updater has to ask for the one that matches how this copy was installed: the stable tarball
    /// for install.sh, the versioned AppImage for an AppImage, and the versioned .deb for a package.
    /// </summary>
    public static bool IsAssetFor(InstallKind kind, string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return false;
        var name = assetName.Trim();
        return kind switch
        {
            InstallKind.Tarball => string.Equals(name, SetupAssetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "ScorePlusLinuxSetup.tgz", StringComparison.OrdinalIgnoreCase),
            InstallKind.AppImage => name.StartsWith("ScorePlus-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase),
            InstallKind.SystemPackage => name.StartsWith("scoreplus_", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith("_amd64.deb", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static string Resolve(string? preferred) => string.IsNullOrWhiteSpace(preferred) ? BuiltIn : preferred.Trim();
}

/// <summary>
/// Checks a release feed for a newer build and can fetch and verify it. Two shapes are understood:
/// a GitHub releases array (prereleases included, which is what /releases/latest skips) and a plain
/// manifest object for any other host. This is deliberately small and explicit: the app asks one
/// URL for JSON, compares versions, and hands the verified tarball to the Linux installer. There is
/// no background service and no account.
/// </summary>
public static class UpdateChecker
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static UpdateChecker()
    {
        // GitHub's API refuses a request with no User-Agent, and asks for its own media type.
        try
        {
            Client.DefaultRequestHeaders.UserAgent.ParseAdd($"ScorePlus/{AppInfo.Version}");
            Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json, application/json");
        }
        catch { }
    }

    /// <summary>
    /// Returns the newer release, or null when this build is current, the feed cannot be read, or
    /// nothing suitable is published. A failed check is silent: an offline laptop must not nag
    /// mid-show.
    /// </summary>
    public static async Task<UpdateManifest?> CheckAsync(string? feedUrl, InstallKind kind, CancellationToken token = default)
    {
        try
        {
            var json = await Client.GetStringAsync(UpdateFeed.Resolve(feedUrl), token);
            var manifest = json.TrimStart().StartsWith('[') ? ParseReleases(json, kind) : ParseManifest(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url)) return null;
            return IsNewer(manifest.Version, AppInfo.Version) ? manifest : null;
        }
        catch { return null; }
    }

    /// <summary>A single-object manifest, for a feed hosted outside GitHub.</summary>
    public static UpdateManifest? ParseManifest(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Options);
            return manifest is { Version: not null, Url: not null } ? manifest : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads a GitHub releases array and returns the newest published release that actually carries
    /// an asset for this install kind. Drafts are ignored (their assets are not served), prereleases
    /// are kept — that is
    /// the whole point — and the highest version wins rather than the first entry, so a back-dated
    /// tag cannot hide a newer build. The checksum comes from the asset's own digest, which GitHub
    /// computes on upload, so there is no manifest to publish or forget.
    /// </summary>
    public static UpdateManifest? ParseReleases(string json, InstallKind kind)
    {
        if (kind == InstallKind.Folder) return null;
        List<GithubRelease>? releases;
        try { releases = JsonSerializer.Deserialize<List<GithubRelease>>(json, Options); }
        catch { return null; }
        if (releases is null) return null;

        UpdateManifest? best = null;
        Version? bestVersion = null;

        foreach (var release in releases)
        {
            if (release.Draft) continue;
            var version = VersionFromTag(release.TagName);
            if (version is null) continue;

            var asset = release.Assets.FirstOrDefault(candidate =>
                UpdateFeed.IsAssetFor(kind, candidate.Name)
                && !string.Equals(candidate.State, "starter", StringComparison.OrdinalIgnoreCase));
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)) continue;

            var parsed = Version.Parse(version);
            if (bestVersion is not null && parsed <= bestVersion) continue;

            bestVersion = parsed;
            best = new UpdateManifest(version, asset.BrowserDownloadUrl, Sha256Of(asset.Digest), NotesOf(release));
        }

        return best;
    }

    /// <summary>
    /// Turns a tag into a comparable version: "v1.2.0", "1.2", "release-1.2.3" and "a1.01" all work.
    /// Anything with no number in it cannot be compared, so it is not offered as an update.
    /// </summary>
    public static string? VersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var match = Regex.Match(tag, @"\d+(?:\.\d+)*");
        if (!match.Success) return null;

        var parts = match.Value.Split('.');
        if (parts.Length > 3) parts = parts[..3];
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var value) || value < 0) return null;
            numbers[i] = value;
        }
        return $"{numbers[0]}.{numbers[1]}.{numbers[2]}";
    }

    public static bool IsNewer(string remote, string current) =>
        Version.TryParse(remote, out var newer) && Version.TryParse(current, out var mine) && newer > mine;

    /// <summary>
    /// Downloads the release to a temp file and verifies its SHA-256 when the feed carries one.
    /// A mismatch deletes the file: an update that cannot be proven is never run.
    /// </summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(UpdateManifest manifest, CancellationToken token = default)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "ScorePlusUpdate");
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(new Uri(manifest.Url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = UpdateFeed.SetupAssetName;
            var path = Path.Combine(directory, fileName);

            using (var response = await Client.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file, token);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    return (null, "The download did not match the published checksum, so it was discarded.");
                }
            }
            return (path, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    /// <summary>
    /// Starts the handover for this copy's install kind: a tarball is unpacked and installed, an
    /// AppImage is replaced in place, a .deb goes back through the package manager. Returns null
    /// when the helper started, or a sentence for the operator when it cannot.
    /// </summary>
    public static string? Run(string path)
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return "This build can only update itself on Linux.";
            return AppInfo.Kind switch
            {
                InstallKind.Tarball => InstallTarball(path),
                InstallKind.AppImage => InstallAppImage(path),
                InstallKind.SystemPackage => InstallPackage(path),
                _ => "This copy runs from a folder, so it does not update itself.",
            };
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Unpacks the tarball and lets install.sh swap the per-user copy in place.</summary>
    static string? InstallTarball(string archivePath)
    {
        var installDir = AppInfo.InstallDir;
        if (string.IsNullOrWhiteSpace(installDir))
            return "This copy has no install.json, so the updater cannot tell where it was installed.";
        if (!IsWritableDirectory(installDir))
            return $"{installDir} is not writable by this user. Update the package the way it was installed.";

        var work = NewWorkDirectory();
        var (unpacked, error) = Extract(archivePath, work);
        if (!unpacked) return error;

        var installer = Directory.EnumerateFiles(work, "install.sh", SearchOption.AllDirectories).FirstOrDefault();
        if (installer is null) return "The download did not contain install.sh, so nothing was installed.";

        var target = Path.Combine(installDir, "scoreplus");
        var script = new StringBuilder();
        StartHelper(script);
        script.AppendLine($"if ! bash {ShellQuote(installer)} --silent >>\"$log\" 2>&1; then echo \"install failed, see $log\" >&2; exit 1; fi");
        script.AppendLine($"[[ -x {ShellQuote(target)} ]] && setsid {ShellQuote(target)} >>\"$log\" 2>&1 &");
        return LaunchHelper(work, "apply-tarball.sh", script);
    }

    /// <summary>Replaces the .AppImage file this process was started from, then reopens it.</summary>
    static string? InstallAppImage(string replacement)
    {
        var image = AppInfo.AppImagePath;
        if (string.IsNullOrWhiteSpace(image)) return "This copy does not know which AppImage it was started from.";
        var directory = Path.GetDirectoryName(image);
        if (string.IsNullOrWhiteSpace(directory) || !IsWritableDirectory(directory))
            return $"{directory} is not writable by this user, so the AppImage cannot be replaced there.";

        var staged = image + ".new";
        var script = new StringBuilder();
        StartHelper(script);
        // install, then rename: the running AppImage keeps its old inode, and the new file appears
        // complete rather than half-copied.
        script.AppendLine($"install -m 0755 {ShellQuote(replacement)} {ShellQuote(staged)} || exit 1");
        script.AppendLine($"mv -f {ShellQuote(staged)} {ShellQuote(image)} || exit 1");
        script.AppendLine($"setsid {ShellQuote(image)} >>\"$log\" 2>&1 &");
        return LaunchHelper(NewWorkDirectory(), "apply-appimage.sh", script);
    }

    /// <summary>Hands the .deb back to the package manager, which is how this copy was installed.</summary>
    static string? InstallPackage(string package)
    {
        const string target = "/usr/bin/scoreplus";
        var script = new StringBuilder();
        StartHelper(script);
        // pkexec is the desktop's own authorisation prompt; without it, the file is opened in the
        // distribution's package installer, which asks the same question in its own window.
        script.AppendLine("if command -v pkexec >/dev/null 2>&1; then");
        script.AppendLine($"  pkexec apt-get install -y {ShellQuote(package)} >>\"$log\" 2>&1 || exit 1");
        script.AppendLine($"  setsid {ShellQuote(target)} >>\"$log\" 2>&1 &");
        script.AppendLine("else");
        script.AppendLine($"  xdg-open {ShellQuote(package)} >>\"$log\" 2>&1 &");
        script.AppendLine("fi");
        return LaunchHelper(NewWorkDirectory(), "apply-package.sh", script);
    }

    /// <summary>The two lines every helper needs: the PID to wait for and one log file.</summary>
    static void StartHelper(StringBuilder script)
    {
        script.AppendLine("#!/usr/bin/env bash");
        script.AppendLine("set -u");
        script.AppendLine("pid=\"$1\"");
        script.AppendLine("log=\"${TMPDIR:-/tmp}/scoreplus-update.log\"");
        // Every channel waits for this copy to exit: install.sh refuses to run while it is alive,
        // and an AppImage cannot be renamed over while its mount is still held.
        script.AppendLine("while kill -0 \"$pid\" 2>/dev/null; do sleep 0.2; done");
    }

    static string NewWorkDirectory()
    {
        var work = Path.Combine(Path.GetTempPath(), "ScorePlusUpdate", "apply-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        return work;
    }

    /// <summary>Writes the helper and starts it detached; it outlives this process by design.</summary>
    static string? LaunchHelper(string directory, string name, StringBuilder script)
    {
        var helper = Path.Combine(directory, name);
        File.WriteAllText(helper, script.ToString());

        var start = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        start.ArgumentList.Add(helper);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Process.Start(start);
        return null;
    }

    static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>tar, not a managed reader: the tarball is what install.sh is written against.</summary>
    static (bool Unpacked, string? Error) Extract(string archivePath, string directory)
    {
        var start = new ProcessStartInfo("/bin/tar")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-xzf");
        start.ArgumentList.Add(archivePath);
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);

        using var tar = Process.Start(start);
        if (tar is null) return (false, "tar could not be started.");
        var error = tar.StandardError.ReadToEnd();
        tar.WaitForExit();
        return tar.ExitCode == 0
            ? (true, null)
            : (false, $"The download could not be unpacked: {error.Trim()}");
    }

    static bool IsWritableDirectory(string path)
    {
        try
        {
            var probe = Path.Combine(path, ".scoreplus-write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>GitHub's asset digest reads "sha256:HEX"; the manifest wants the bare hex.</summary>
    static string? Sha256Of(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        var value = digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? digest[prefix.Length..] : digest;
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }

    /// <summary>One or two sentences for the prompt: the release title, else its first line.</summary>
    static string? NotesOf(GithubRelease release)
    {
        var name = release.Name?.Trim();
        if (!string.IsNullOrEmpty(name)) return name;
        var line = release.Body?.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0);
        if (line is null) return null;
        return line.Length > 180 ? line[..180] : line;
    }

    sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GithubAsset> Assets { get; set; } = [];
    }

    sealed class GithubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
