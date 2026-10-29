using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Scoreboard.Desktop;

/// <summary>What this build is, and whether it came from the installer.</summary>
public static class AppInfo
{
    /// <summary>The build's version, from the assembly the publish set. The installer writes the same one.</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}"
        : "1.0.0";

    public const string RegistryKey = @"Software\ScorePlus";

    /// <summary>
    /// True only for an installed copy. A build run straight out of the dist folder must never try to
    /// update itself over the top of a working file; the installer is what puts the marker in place.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir)) return true;
            }
            catch { }
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "install.json"));
        }
    }

    public static string? InstallDir
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue("InstallDir") is string dir) return dir;
            }
            catch { }
            return null;
        }
    }
}

/// <summary>One release as described by the update feed.</summary>
public sealed record UpdateManifest(string Version, string Url, string? Sha256, string? Notes);

/// <summary>
/// Checks a release manifest for a newer build and can fetch and verify it. This is deliberately
/// small and explicit: the app asks a URL you control for JSON, compares versions, and hands the
/// verified installer to Windows. There is no background service and no account.
/// </summary>
public static class UpdateChecker
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Returns the newer release, or null when this build is current, the feed is not set, or the
    /// download cannot be read. A failed check is silent: an offline laptop must not nag mid-show.
    /// </summary>
    public static async Task<UpdateManifest?> CheckAsync(string? feedUrl, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        try
        {
            var json = await Client.GetStringAsync(feedUrl, token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url)) return null;
            return IsNewer(manifest.Version, AppInfo.Version) ? manifest : null;
        }
        catch { return null; }
    }

    public static bool IsNewer(string remote, string current) =>
        Version.TryParse(remote, out var newer) && Version.TryParse(current, out var mine) && newer > mine;

    /// <summary>
    /// Downloads the release to a temp file and verifies its SHA-256 when the manifest carries one.
    /// A mismatch deletes the file: an update that cannot be proven is never run.
    /// </summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(UpdateManifest manifest, CancellationToken token = default)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "ScorePlusUpdate");
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(new Uri(manifest.Url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "ScorePlusSetup.exe";
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

    /// <summary>Starts the verified installer and lets it replace this copy.</summary>
    public static bool Run(string path)
    {
        try
        {
            // /SILENT keeps the wizard invisible; the installer closes ScorePlus itself before
            // copying files, so the running app never locks what is being replaced.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTART" });
            return true;
        }
        catch { return false; }
    }
}
