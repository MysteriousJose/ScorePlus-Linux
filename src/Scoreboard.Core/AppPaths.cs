using System.IO;

namespace Scoreboard.Core;

/// <summary>
/// Every file ScorePlus keeps for the operator — preferences, keybinds, sport profiles, the scorebug
/// layout, imported logo assets and autosaves — under one folder named after the app.
/// </summary>
public static class AppPaths
{
    /// <summary>The current folder name: <c>%LOCALAPPDATA%\ScorePlus</c>.</summary>
    public const string FolderName = "ScorePlus";

    /// <summary>The folder earlier builds used. Only read, to migrate it once.</summary>
    const string PreviousFolderName = "Scoreboard";

    static string? _override;
    static string? _resolved;

    /// <summary>Test hook: point every default path at a throwaway folder.</summary>
    public static void UseRoot(string? root)
    {
        _override = root;
        _resolved = null;
    }

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Where earlier builds kept their files (<c>%LOCALAPPDATA%\Scoreboard</c>).</summary>
    public static string PreviousRoot => Path.Combine(LocalAppData, PreviousFolderName);

    /// <summary>False when a test has redirected <see cref="Root"/> at a throwaway folder.</summary>
    public static bool IsUsingDefaultRoot => _override is null;

    public static string Root
    {
        get
        {
            if (_override is not null) return _override;
            if (_resolved is not null) return _resolved;
            var root = Path.Combine(LocalAppData, FolderName);
            Migrate(PreviousRoot, root);
            _resolved = root;
            return root;
        }
    }

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>
    /// Carries an existing installation across the folder rename. Files are copied, never moved, so
    /// an older build still works and nothing can be lost if the copy is interrupted.
    /// </summary>
    static void Migrate(string previous, string current)
    {
        // Once only. Without this marker, autosaves pruned by rotation would be copied back from the
        // old folder on every launch and the list would never settle.
        var marker = Path.Combine(current, ".migrated-from-scoreboard");
        if (File.Exists(marker)) return;
        try
        {
            if (!Directory.Exists(previous)) return;
            foreach (var folder in new[] { "", "autosaves", "ScorebugAssets" })
            {
                var from = folder.Length == 0 ? previous : Path.Combine(previous, folder);
                if (!Directory.Exists(from)) continue;
                var to = folder.Length == 0 ? current : Path.Combine(current, folder);
                Directory.CreateDirectory(to);
                CopyMissing(from, to);
            }
            Directory.CreateDirectory(current);
            File.WriteAllText(marker, DateTimeOffset.Now.ToString("u"));
        }
        catch
        {
            // A failed migration must not stop the app; the new folder simply starts empty.
        }
    }

    static void CopyMissing(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(target)) continue;
            try { File.Copy(file, target); } catch { }
        }
    }
}
