using System.Text.Json;

namespace Scoreboard.Core;

public enum ScorebugElementKind { Text, Logo, Shape }
public enum ScorebugAnimation { None, Pulse, Drift }

public sealed class ScorebugElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Element";
    public ScorebugElementKind Kind { get; set; } = ScorebugElementKind.Text;
    public string Content { get; set; } = "TEXT";
    public string? ImagePath { get; set; }
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 70;
    public double FontSize { get; set; } = 32;
    public string Foreground { get; set; } = "#FFFFFFFF";
    public string Background { get; set; } = "#CC141D28";
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public ScorebugAnimation Animation { get; set; }
}

public sealed class ScorebugLayout
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Broadcast Classic";
    public List<ScorebugElement> Elements { get; set; } = [];

    public static ScorebugLayout Default() => new()
    {
        Elements =
        [
            new() { Name="Home team", Content="{{HOME_TEAM}}", X=74, Y=236, Width=300, Height=54, FontSize=36 },
            new() { Name="Home score", Content="{{HOME_SCORE}}", X=74, Y=300, Width=300, Height=126, FontSize=102, Foreground="#FF38D7A3" },
            new() { Name="Away team", Content="{{AWAY_TEAM}}", X=906, Y=236, Width=300, Height=54, FontSize=36 },
            new() { Name="Away score", Content="{{AWAY_SCORE}}", X=906, Y=300, Width=300, Height=126, FontSize=102 },
            new() { Name="Period", Content="{{PERIOD}}", X=470, Y=225, Width=340, Height=52, FontSize=28 },
            new() { Name="Game clock", Content="{{GAME_CLOCK}}", X=430, Y=300, Width=420, Height=92, FontSize=72 },
            new() { Name="Home logo", Kind=ScorebugElementKind.Logo, Content="HOME LOGO", X=78, Y=160, Width=120, Height=64, FontSize=20 },
            new() { Name="Away logo", Kind=ScorebugElementKind.Logo, Content="AWAY LOGO", X=1082, Y=160, Width=120, Height=64, FontSize=20 }
        ]
    };
}

public sealed class ScorebugCatalog
{
    readonly string _path;
    public ScorebugLayout Layout { get; private set; }
    public string? Warning { get; private set; }
    public event Action? Changed;
    public ScorebugCatalog(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scoreboard", "scorebug.json");
        try { Layout = File.Exists(_path) ? JsonSerializer.Deserialize<ScorebugLayout>(File.ReadAllText(_path)) ?? ScorebugLayout.Default() : ScorebugLayout.Default(); }
        catch (Exception ex) { Layout = ScorebugLayout.Default(); Warning = $"Could not load scorebug layout: {ex.Message}"; }
        Validate();
    }
    public void Update(Action<ScorebugLayout> edit)
    {
        edit(Layout); Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Layout, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true); Changed?.Invoke();
    }
    void Validate()
    {
        Layout.Elements ??= [];
        if (Layout.Elements.Count > 100) Layout.Elements = Layout.Elements.Take(100).ToList();
        foreach (var item in Layout.Elements)
        {
            item.X = Math.Clamp(double.IsFinite(item.X) ? item.X : 0, 0, 1280);
            item.Y = Math.Clamp(double.IsFinite(item.Y) ? item.Y : 0, 0, 720);
            item.Width = Math.Clamp(double.IsFinite(item.Width) ? item.Width : 100, 24, 1280);
            item.Height = Math.Clamp(double.IsFinite(item.Height) ? item.Height : 50, 24, 720);
            item.FontSize = Math.Clamp(double.IsFinite(item.FontSize) ? item.FontSize : 24, 8, 180);
            item.Opacity = Math.Clamp(double.IsFinite(item.Opacity) ? item.Opacity : 1, 0, 1);
        }
    }
}
