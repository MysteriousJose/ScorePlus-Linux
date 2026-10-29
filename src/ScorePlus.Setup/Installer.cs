using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace ScorePlus.Setup;

/// <summary>
/// Puts the published app on the machine and takes it off again. Everything lives under the user's
/// own profile, so installing needs no administrator rights: the app, its shortcuts, and the two
/// registry keys Windows reads to list it in Settings → Apps.
/// </summary>
public static class Installer
{
    public const string ProductName = "ScorePlus";
    public const string Publisher = "ScorePlus";
    public const string AppExecutable = "ScorePlus.exe";

    /// <summary>The setup's own version, set by the release build.</summary>
    public static string Version { get; } = typeof(Installer).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}"
        : "1.0.0";

    static readonly string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScorePlus";
    static readonly string AppKey = @"Software\ScorePlus";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    public sealed record Result(bool Ok, string Message);

    /// <summary>Unpacks the app, writes shortcuts and the install marker.</summary>
    /// <param name="shortcuts">False for a silent or portable install that must not touch the shell.</param>
    /// <param name="register">False keeps the registry untouched, for a folder-only install.</param>
    public static Result Install(string directory, bool desktopShortcut, Action<string>? progress = null, bool shortcuts = true, bool register = true)
    {
        try
        {
            directory = Path.GetFullPath(directory.Trim());
            if (string.IsNullOrWhiteSpace(directory)) return new(false, "Choose where ScorePlus should be installed.");

            CloseRunningApp(progress);
            Directory.CreateDirectory(directory);
            progress?.Invoke("Copying ScorePlus…");
            ExtractPayload(directory);

            var executable = Path.Combine(directory, AppExecutable);
            if (!File.Exists(executable)) return new(false, "The installer payload is missing its application.");

            if (shortcuts)
            {
                progress?.Invoke("Creating shortcuts…");
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(startMenu);
                CreateShortcut(Path.Combine(startMenu, $"{ProductName}.lnk"), executable, string.Empty, directory, "ScorePlus live production console");
                if (desktopShortcut)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktop, $"{ProductName}.lnk"), executable, string.Empty, directory, "ScorePlus live production console");
                }
            }

            var uninstaller = Path.Combine(directory, "Uninstall.exe");
            try { File.Copy(Environment.ProcessPath!, uninstaller, true); } catch { }

            // The marker is what tells the app it is allowed to check for updates.
            File.WriteAllText(Path.Combine(directory, "install.json"),
                $"{{\n  \"installed\": true,\n  \"version\": \"{Version}\",\n  \"installedAt\": \"{DateTimeOffset.Now:O}\"\n}}\n");

            if (register)
            {
                progress?.Invoke("Registering ScorePlus…");
                using (var key = Registry.CurrentUser.CreateSubKey(AppKey))
                {
                    key?.SetValue("InstallDir", directory);
                    key?.SetValue("Version", Version);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key?.SetValue("DisplayName", ProductName);
                    key?.SetValue("DisplayVersion", Version);
                    key?.SetValue("Publisher", Publisher);
                    key?.SetValue("InstallLocation", directory);
                    key?.SetValue("DisplayIcon", executable);
                    key?.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
                    key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            progress?.Invoke("Installed.");
            return new(true, $"ScorePlus {Version} is installed in {directory}.");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }

    /// <summary>Removes the app, its shortcuts and its registry entries. Game files stay untouched.</summary>
    public static Result Uninstall(Action<string>? progress = null)
    {
        try
        {
            var directory = InstalledDirectory();
            if (string.IsNullOrWhiteSpace(directory))
            {
                RemoveRegistry();
                return new(true, "ScorePlus is not registered on this computer, so there was nothing to remove.");
            }
            CloseRunningApp(progress);
            progress?.Invoke("Removing shortcuts…");
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", $"{ProductName}.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk"));

            progress?.Invoke("Removing files…");
            // The uninstaller may be running from this folder; the copy in %TEMP% is what finishes
            // the job, so a locked file can never stop the removal.
            var payload = Path.Combine(directory, AppExecutable);
            TryDelete(payload);
            foreach (var file in Directory.Exists(directory) ? Directory.GetFiles(directory) : [])
                TryDelete(file);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }

            RemoveRegistry();
            progress?.Invoke("Removed.");
            return new(true, "ScorePlus was removed. Your saved games, media and preferences are still in your profile.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public static string? InstalledDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir)) return dir;
        }
        catch { }
        return null;
    }

    static void RemoveRegistry()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(AppKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
    }

    static void ExtractPayload(string directory)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (stream is null) throw new InvalidOperationException("This setup was built without the app payload. Run scripts/build-release.sh to make a real installer.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update payload contains an unexpected path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    /// <summary>An update can be installed while ScorePlus is on air; it is asked to close first.</summary>
    static void CloseRunningApp(Action<string>? progress)
    {
        foreach (var process in Process.GetProcessesByName("ScorePlus"))
        {
            try
            {
                progress?.Invoke("Closing ScorePlus…");
                if (!process.CloseMainWindow()) { process.Kill(); continue; }
                if (!process.WaitForExit(4000)) process.Kill();
            }
            catch { }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Makes a real .lnk through the Windows shell, so it looks and behaves like any other.</summary>
    public static void CreateShortcut(string shortcutPath, string target, string arguments, string workingDirectory, string description)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = target;
            link.Arguments = arguments;
            link.WorkingDirectory = workingDirectory;
            link.Description = description;
            link.IconLocation = target;
            link.Save();
        }
        catch { /* A missing shortcut must never fail the install itself. */ }
    }
}
