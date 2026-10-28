using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scoreboard.Core;

public enum ScorebugElementKind { Text, Logo, Shape, Video }

/// <summary>Ambient motion that runs all the time, whatever the value is.</summary>
public enum ScorebugAnimation { None, Pulse, Drift }

/// <summary>
/// What happens to a text element when its value changes. Twelve ways to cross from the old
/// number to the new one, so the score never just cuts.
/// </summary>
public enum ScorebugTransition
{
    Cut,
    Fade,
    FadeScale,
    SlideUp,
    SlideDown,
    SlideLeft,
    SlideRight,
    WheelUp,
    WheelDown,
    Flip,
    Zoom,
    Pop,
    Drop
}

public enum ScorebugShape { Rectangle, Circle, Triangle }

/// <summary>Where an element's colour comes from: a fixed colour, or one of the team palette slots.</summary>
public enum ScorebugColorSource
{
    Custom,
    HomePrimary,
    HomeSecondary,
    AwayPrimary,
    AwaySecondary
}

/// <summary>Which team's logo a logo element shows; set in the control panel, not the studio.</summary>
public enum ScorebugLogoSide { None, Home, Away }

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

    /// <summary>Font family name, or a file URI for an imported font.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }

    public string Foreground { get; set; } = "#FFFFFFFF";
    public string Background { get; set; } = "#CC141D28";

    /// <summary>Clockwise rotation of the element's content, in degrees. Mainly for slanted text.</summary>
    public double Rotation { get; set; }

    /// <summary>Outline drawn around the element. Zero hides the border entirely.</summary>
    public double BorderThickness { get; set; }

    /// <summary>The outline's fixed colour, used unless <see cref="BorderSource"/> names a team slot.</summary>
    public string BorderColor { get; set; } = "#FFFFFFFF";

    /// <summary>Where the border colour comes from. A team slot wins over <see cref="BorderColor"/>.</summary>
    public ScorebugColorSource BorderSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Where the text colour comes from. A team slot wins over <see cref="Foreground"/>.</summary>
    public ScorebugColorSource ForegroundSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Where the background comes from. A team slot wins over <see cref="Background"/>.</summary>
    public ScorebugColorSource BackgroundSource { get; set; } = ScorebugColorSource.Custom;

    public bool TransparentBackground { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public ScorebugAnimation Animation { get; set; }

    /// <summary>How the value changes: dissolve, wheel, flip, and so on.</summary>
    public ScorebugTransition Transition { get; set; } = ScorebugTransition.Cut;

    /// <summary>How long a value change takes, in seconds: 0 snaps, 5 is a slow dissolve.</summary>
    public double TransitionSeconds { get; set; } = 0.2;

    /// <summary>Which team logo a logo element draws. Configured in the control panel.</summary>
    public ScorebugLogoSide LogoSide { get; set; } = ScorebugLogoSide.None;

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
    public int Version { get; set; } = 3;
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
            new() { Name="Home score", Content="{{HOME_SCORE}}", X=74, Y=300, Width=300, Height=126, FontSize=102, ForegroundSource=ScorebugColorSource.HomePrimary },
            new() { Name="Away team", Content="{{AWAY_TEAM}}", X=906, Y=236, Width=300, Height=54, FontSize=36 },
            new() { Name="Away score", Content="{{AWAY_SCORE}}", X=906, Y=300, Width=300, Height=126, FontSize=102, ForegroundSource=ScorebugColorSource.AwayPrimary },
            new() { Name="Period", Content="{{PERIOD}}", X=470, Y=225, Width=340, Height=52, FontSize=28 },
            new() { Name="Game clock", Content="{{GAME_CLOCK}}", X=430, Y=300, Width=420, Height=92, FontSize=72, Transition=ScorebugTransition.WheelUp },
            new() { Name="Home logo", Kind=ScorebugElementKind.Logo, LogoSide=ScorebugLogoSide.Home, Content="HOME LOGO", X=78, Y=160, Width=120, Height=64, FontSize=20 },
            new() { Name="Away logo", Kind=ScorebugElementKind.Logo, LogoSide=ScorebugLogoSide.Away, Content="AWAY LOGO", X=1082, Y=160, Width=120, Height=64, FontSize=20 }
        ]
    };

    /// <summary>A deep copy with brand new element ids, so a duplicated layout edits independently.</summary>
    public ScorebugLayout Copy(string? name = null)
    {
        var copy = JsonSerializer.Deserialize<ScorebugLayout>(JsonSerializer.Serialize(this))!;
        copy.Name = name ?? $"{Name} copy";
        foreach (var element in copy.Elements) element.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}

/// <summary>What is written to scorebug.json: every layout, plus which one is being edited.</summary>
public sealed class ScorebugDocument
{
    public int Version { get; set; } = 3;
    public List<ScorebugLayout> Layouts { get; set; } = [];
    public int ActiveIndex { get; set; }
    public int PublishedIndex { get; set; }
}

public sealed class ScorebugCatalog
{
    readonly string _path;
    public List<ScorebugLayout> Layouts { get; private set; } = [];
    public int ActiveIndex { get; private set; }
    public int PublishedIndex { get; private set; }
    public string? Warning { get; private set; }
    public event Action? Changed;

    public ScorebugCatalog(string? path = null)
    {
        _path = path ?? AppPaths.Combine("scorebug.json");
        try { Load(); }
        catch (Exception ex)
        {
            Layouts = [ScorebugLayout.Default()];
            Warning = $"Could not load scorebug layout: {ex.Message}";
        }
        Validate();
    }

    /// <summary>The layout being designed and previewed.</summary>
    public ScorebugLayout Layout => Layouts[Math.Clamp(ActiveIndex, 0, Layouts.Count - 1)];

    /// <summary>The layout currently on air. It changes only when Update Output is pressed.</summary>
    public ScorebugLayout PublishedLayout => Layouts[Math.Clamp(PublishedIndex, 0, Layouts.Count - 1)];

    void Load()
    {
        Warning = null;
        if (!File.Exists(_path))
        {
            Layouts = [ScorebugLayout.Default()];
            ActiveIndex = PublishedIndex = 0;
            return;
        }
        var text = File.ReadAllText(_path);
        var node = JsonNode.Parse(text);
        if (node is JsonObject root && root["Layouts"] is not null)
        {
            var document = root.Deserialize<ScorebugDocument>() ?? new ScorebugDocument();
            Layouts = document.Layouts ?? [];
            ActiveIndex = document.ActiveIndex;
            PublishedIndex = document.PublishedIndex;
        }
        else
        {
            // An older file held one layout. It keeps its artwork and becomes layout 1.
            var single = node?.Deserialize<ScorebugLayout>() ?? ScorebugLayout.Default();
            Layouts = [single];
            ActiveIndex = PublishedIndex = 0;
        }
        if (Layouts.Count == 0) Layouts = [ScorebugLayout.Default()];
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var document = new ScorebugDocument
        {
            Layouts = Layouts,
            ActiveIndex = ActiveIndex,
            PublishedIndex = PublishedIndex
        };
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }

    /// <summary>Edits the layout being designed, then saves and notifies every view.</summary>
    public void Update(Action<ScorebugLayout> edit)
    {
        edit(Layout); Validate();
        Save(); Changed?.Invoke();
    }

    void Validate()
    {
        if (Layouts.Count == 0) Layouts = [ScorebugLayout.Default()];
        ActiveIndex = Math.Clamp(ActiveIndex, 0, Layouts.Count - 1);
        PublishedIndex = Math.Clamp(PublishedIndex, 0, Layouts.Count - 1);
        foreach (var layout in Layouts)
        {
            layout.Elements ??= [];
            if (layout.Elements.Count > 100) layout.Elements = layout.Elements.Take(100).ToList();
            layout.Width = (int)Math.Clamp(layout.Width, 320, 7680);
            layout.Height = (int)Math.Clamp(layout.Height, 240, 7680);
            if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = "Untitled layout";
            foreach (var item in layout.Elements)
            {
                item.X = Math.Clamp(double.IsFinite(item.X) ? item.X : 0, 0, layout.Width);
                item.Y = Math.Clamp(double.IsFinite(item.Y) ? item.Y : 0, 0, layout.Height);
                item.Width = Math.Clamp(double.IsFinite(item.Width) ? item.Width : 100, 24, layout.Width);
                item.Height = Math.Clamp(double.IsFinite(item.Height) ? item.Height : 50, 24, layout.Height);
                item.FontSize = Math.Clamp(double.IsFinite(item.FontSize) ? item.FontSize : 24, 8, 480);
                item.Opacity = Math.Clamp(double.IsFinite(item.Opacity) ? item.Opacity : 1, 0, 1);
                item.Rotation = Math.Clamp(double.IsFinite(item.Rotation) ? item.Rotation : 0, -360, 360);
                item.BorderThickness = Math.Clamp(double.IsFinite(item.BorderThickness) ? item.BorderThickness : 0, 0, 40);
                item.TransitionSeconds = Math.Clamp(double.IsFinite(item.TransitionSeconds) ? item.TransitionSeconds : 0.2, 0, 5);
                if (string.IsNullOrWhiteSpace(item.BorderColor)) item.BorderColor = "#FFFFFFFF";
                if (item.PeriodFormat is not ("Full" or "Short" or "Number")) item.PeriodFormat = null;
                if (string.IsNullOrWhiteSpace(item.FontFamily)) item.FontFamily = "Segoe UI";
            }
        }
    }

    /// <summary>Re-sizes the layout being designed and scales every element with it, so a 720p design survives a move to 4K.</summary>
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

    // ---------- layouts ----------

    /// <summary>Selects the layout being designed. Never touches what is on air until PublishLayout.</summary>
    public void SelectLayout(int index, bool publish = false)
    {
        var next = Math.Clamp(index, 0, Layouts.Count - 1);
        if (next == ActiveIndex && !publish) return;
        ActiveIndex = next;
        if (publish) PublishedIndex = next;
        Save(); Changed?.Invoke();
    }

    /// <summary>Sends the previewed layout to the audience output.</summary>
    public void PublishLayout()
    {
        if (PublishedIndex == ActiveIndex) return;
        PublishedIndex = ActiveIndex;
        Save(); Changed?.Invoke();
    }

    public int AddLayout(string? name = null, bool copyActive = false)
    {
        var layout = copyActive ? Layout.Copy(name) : new ScorebugLayout { Name = string.IsNullOrWhiteSpace(name) ? $"Layout {Layouts.Count + 1}" : name.Trim() };
        if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = $"Layout {Layouts.Count + 1}";
        Layouts.Add(layout);
        ActiveIndex = Layouts.Count - 1;
        Save(); Changed?.Invoke();
        return ActiveIndex;
    }

    public bool RemoveLayout(int index)
    {
        if (Layouts.Count <= 1 || index < 0 || index >= Layouts.Count) return false;
        Layouts.RemoveAt(index);
        if (ActiveIndex >= Layouts.Count) ActiveIndex = Layouts.Count - 1;
        if (PublishedIndex >= Layouts.Count) PublishedIndex = Layouts.Count - 1;
        if (ActiveIndex > index) ActiveIndex--;
        if (PublishedIndex > index) PublishedIndex--;
        Save(); Changed?.Invoke();
        return true;
    }

    public void RenameLayout(int index, string name)
    {
        if (index < 0 || index >= Layouts.Count || string.IsNullOrWhiteSpace(name)) return;
        Layouts[index].Name = name.Trim();
        Save(); Changed?.Invoke();
    }

    /// <summary>
    /// Starts over with the single factory layout. A new game is a blank slate: no layouts from
    /// the previous game, and the same default design every fresh install opens with.
    /// </summary>
    public void ResetToDefault()
    {
        Layouts = [ScorebugLayout.Default()];
        ActiveIndex = 0;
        PublishedIndex = 0;
        Save(); Changed?.Invoke();
    }
}
