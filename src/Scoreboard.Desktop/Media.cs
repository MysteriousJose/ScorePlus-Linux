using System.IO;

namespace Scoreboard.Desktop;

/// <summary>
/// One clip in the operator's media library. <see cref="Tag"/> is the folder-like bucket the clip
/// belongs to; scorebug video elements only display clips whose tag they are watching.
/// </summary>
public sealed record MediaEntry(string Path, string Tag = MediaLibrary.DefaultTag)
{
    public string Label => System.IO.Path.GetFileName(Path);
    public bool IsVideo => MediaLibrary.IsVideoFile(Path);
    public string KindLabel => IsVideo ? "VIDEO" : "AUDIO";
}

public static class MediaLibrary
{
    public const string DefaultTag = "General";

    public static readonly string[] SuggestedTags = ["General", "Ads", "Highlights", "Music", "Anthem", "Sponsors"];

    public static bool IsVideoFile(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v";

    public static string NormalizeTag(string? tag)
    {
        var trimmed = (tag ?? "").Trim();
        return trimmed.Length == 0 ? DefaultTag : trimmed;
    }

    /// <summary>Moves a clip into a tag, in place, and returns the updated entry.</summary>
    public static MediaEntry? Retag(IList<MediaEntry> library, MediaEntry selected, string? tag)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { Tag = NormalizeTag(tag) };
        library[index] = updated;
        return updated;
    }
}

/// <summary>
/// The clip currently cued to air. Scorebug video elements subscribe and only show it when their
/// own tag matches, so an ad element never displays the highlight reel and vice versa.
/// </summary>
public sealed class MediaPlayback
{
    public MediaEntry? NowPlaying { get; private set; }

    /// <summary>False while the operator has the clip paused, so scorebug video elements freeze with it.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Where the cued clip has been scrubbed to, in seconds. Video elements follow it.</summary>
    public double Position { get; private set; }

    /// <summary>Bumped on every explicit seek so an element can tell a seek from a play/pause.</summary>
    public int SeekVersion { get; private set; }

    public event Action? Changed;

    public void Start(MediaEntry? entry)
    {
        NowPlaying = entry;
        IsPlaying = entry is not null;
        Position = 0;
        SeekVersion++;
        Changed?.Invoke();
    }

    /// <summary>Moves every scorebug video element showing this clip to the same point.</summary>
    public void Seek(double seconds)
    {
        Position = Math.Max(0, seconds);
        SeekVersion++;
        Changed?.Invoke();
    }

    public void Stop() => Start(null);

    /// <summary>Pauses or resumes every scorebug video element showing the cued clip.</summary>
    public void SetPlaying(bool playing)
    {
        if (NowPlaying is null || IsPlaying == playing) return;
        IsPlaying = playing;
        Changed?.Invoke();
    }

    public bool Matches(MediaEntry entry, string? elementTag)
    {
        if (string.IsNullOrWhiteSpace(elementTag)) return true;
        return string.Equals(MediaLibrary.NormalizeTag(entry.Tag), MediaLibrary.NormalizeTag(elementTag), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The video this element should be showing right now, or null. Audio cues never fill a video box.</summary>
    public MediaEntry? ActiveFor(string? elementTag)
    {
        var entry = NowPlaying;
        if (entry is null || !entry.IsVideo) return null;
        return Matches(entry, elementTag) ? entry : null;
    }
}
