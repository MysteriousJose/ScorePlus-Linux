using System.Text.Json;

namespace Scoreboard.Core;

public sealed class SportCatalog
{
    readonly string _path;
    readonly List<SportDefinition> _custom = [];
    public SportCatalog(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "sports.json");
        try
        {
            if (File.Exists(_path))
                _custom.AddRange(JsonSerializer.Deserialize<List<SportDefinition>>(File.ReadAllText(_path)) ?? []);
        }
        catch { /* A corrupt user catalog must not prevent the game application from starting. */ }
        _custom.RemoveAll(s => !IsValid(s) || SportDefinition.BuiltIns.Any(b => b.Id == s.Id));
    }

    public IReadOnlyList<SportDefinition> All => SportDefinition.BuiltIns.Concat(_custom).OrderBy(s => s.Name).ToArray();
    public IReadOnlyList<SportDefinition> Custom => _custom.AsReadOnly();
    public SportDefinition? Find(string? id) => All.FirstOrDefault(s => s.Id == id);

    public SportDefinition AddOrUpdate(SportDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile = profile with { Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim(), Name = profile.Name.Trim(), Unit = profile.Unit.Trim(), ScoringOptions = profile.ScoringOptions.Distinct().Order().ToArray() };
        if (!IsValid(profile)) throw new ArgumentException("Enter a name, period label, 1–99 periods, a clock of 0–24 hours, and positive scoring values.");
        if (SportDefinition.BuiltIns.Any(s => s.Id == profile.Id)) throw new ArgumentException("Built-in sports cannot be edited. Duplicate a preset to customize it.");
        var index = _custom.FindIndex(s => s.Id == profile.Id);
        if (index < 0) _custom.Add(profile); else _custom[index] = profile;
        Persist(); return profile;
    }

    public void Duplicate(string id, string name)
    {
        var source = Find(id) ?? throw new ArgumentException("Choose a sport profile to duplicate.");
        AddOrUpdate(source with { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() });
    }

    public bool Delete(string id)
    {
        var removed = _custom.RemoveAll(s => s.Id == id) > 0;
        if (removed) Persist();
        return removed;
    }

    static bool IsValid(SportDefinition s) => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Unit) && s.Periods is >= 1 and <= 99 && s.PeriodSeconds is >= 0 and <= 86400 && s.ScoringOptions is { Length: > 0 and <= 20 } && s.ScoringOptions.All(x => x is > 0 and <= 100);
    void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_custom, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
