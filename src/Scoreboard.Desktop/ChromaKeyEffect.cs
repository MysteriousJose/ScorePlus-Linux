using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// Describes the colour a video treats as transparent. Linux has no WPF pixel shader, so the
/// decoder feeds the same key and tolerance to ffmpeg's colorkey filter before the frames reach
/// the screen; this type is the shared description both paths use.
/// </summary>
public sealed class ChromaKeyEffect
{
    public Color KeyColor { get; set; } = Color.FromRgb(0, 177, 64);
    public double Tolerance { get; set; } = 0.16;

    /// <summary>The ffmpeg similarity for a tolerance in the designer's 0–1 range.</summary>
    public double Similarity => Math.Clamp(Tolerance, 0.01, 1);

    public string Filter => $"colorkey=0x{KeyColor.R:X2}{KeyColor.G:X2}{KeyColor.B:X2}:{Similarity:0.###}:0.1";
}
