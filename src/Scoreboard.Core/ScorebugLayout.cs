using System.Text.Json;

namespace Scoreboard.Core;

public enum ScorebugElementKind { Text, Logo, Shape, Video }
public enum ScorebugAnimation { None, Pulse, Drift }
public enum ScorebugShape { Rectangle, Circle, Triangle }

/// <summary>Artwork drawn by an element in addition to (or instead of) its bound text.</summary>
public enum ScorebugIcon
{
    None,
    /// <summary>One little bar per timeout the team has left.</summary>
    TimeoutBars,
    /// <summary>A dot on the left or right edge, on the side of the team with the ball.</summary>
    PossessionDot
}

public sealed class ScorebugElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Element";
    public ScorebugElementKind Kind { get; set; } = ScorebugElementKind.Text;
    public ScorebugShape Shape { get; set; } = ScorebugShape.Rectangle;
    public string Content { get; set; } = "TEXT";
    public string? ImagePath { get; set; }

    /// <summary>For video elements: only media assigned to this tag plays inside the element.</summary>
    public string MediaTag { get; set; } = "";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 70;
    public double FontSize { get; set; } = 32;
    public string Foreground { get; set; } = "#FFFFFFFF";
    public string Background { get; set; } = "#CC141D28";
    public bool TransparentBackground { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public ScorebugAnimation Animation { get; set; }

    /// <summary>How this element prints a quarter: "Full", "Short", "Number", or null for the game default.</summary>
    public string? PeriodFormat { get; set; }

    /// <summary>Optional artwork from live data: timeout bars or a possession dot.</summary>
    public ScorebugIcon Icon { get; set; } = ScorebugIcon.None;

    /// <summary>For timeout bars: count the visitors' timeouts instead of the home team's.</summary>
    public bool IconVisitors { get; set; }

    /// <summary>For the possession dot: mirror it, putting the home side on the left.</summary>
    public bool IconFlipped { get; set; }
}

public sealed class ScorebugLayout
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Broadcast Classic";

    /// <summary>Design canvas size in pixels. Elements are positioned in this space and scaled to fit any output.</summary>
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;

    public static readonly (string Name, int Width, int Height)[] SizePresets =
    [
        ("HD · 1280 × 720", 1280, 720),
        ("Full HD · 1920 × 1080", 1920, 1080),
        ("QHD · 2560 × 1440", 2560, 1440),
        ("4K UHD · 3840 × 2160", 3840, 2160),
        ("Vertical · 1080 × 1920", 1080, 1920),
        ("Square · 1080 × 1080", 1080, 1080)
    ];
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
        _path = path ?? AppPaths.Combine("scorebug.json");
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
        Layout.Width = (int)Math.Clamp(Layout.Width, 320, 7680);
        Layout.Height = (int)Math.Clamp(Layout.Height, 240, 7680);
        foreach (var item in Layout.Elements)
        {
            item.X = Math.Clamp(double.IsFinite(item.X) ? item.X : 0, 0, Layout.Width);
            item.Y = Math.Clamp(double.IsFinite(item.Y) ? item.Y : 0, 0, Layout.Height);
            item.Width = Math.Clamp(double.IsFinite(item.Width) ? item.Width : 100, 24, Layout.Width);
            item.Height = Math.Clamp(double.IsFinite(item.Height) ? item.Height : 50, 24, Layout.Height);
            item.FontSize = Math.Clamp(double.IsFinite(item.FontSize) ? item.FontSize : 24, 8, 480);
            item.Opacity = Math.Clamp(double.IsFinite(item.Opacity) ? item.Opacity : 1, 0, 1);
            if (item.PeriodFormat is not ("Full" or "Short" or "Number")) item.PeriodFormat = null;
        }
    }

    /// <summary>Re-sizes the canvas and scales every element with it, so a layout survives a move to 4K.</summary>
    public void Resize(int width, int height)
    {
        width = (int)Math.Clamp(width, 320, 7680);
        height = (int)Math.Clamp(height, 240, 7680);
        if (width == Layout.Width && height == Layout.Height) return;
        var scaleX = (double)width / Layout.Width;
        var scaleY = (double)height / Layout.Height;
        var scale = Math.Min(scaleX, scaleY);
        Update(layout =>
        {
            foreach (var item in layout.Elements)
            {
                item.X *= scaleX;
                item.Y *= scaleY;
                item.Width *= scaleX;
                item.Height *= scaleY;
                item.FontSize *= scale;
            }
            layout.Width = width;
            layout.Height = height;
        });
    }
}
