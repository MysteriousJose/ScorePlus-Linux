using System.Diagnostics;

namespace Scoreboard.Desktop;

/// <summary>
/// Sends a clip's sound to a chosen PulseAudio sink. The transport and the scorebug elements stay
/// muted for that clip, and this router owns its audio, so one file is never heard twice.
/// </summary>
public sealed class AudioRouter : IDisposable
{
    public sealed record OutputDevice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    readonly FfmpegAudioPlayer _player = new();
    string? _path;
    string? _device;
    double _volume = 1;

    public bool IsPlaying => _player.IsPlaying;
    public string? LastError => _player.LastError;

    /// <summary>Every PulseAudio sink, by stable sink name, plus the default output.</summary>
    public static IReadOnlyList<OutputDevice> Devices()
    {
        if (MediaProbe.PulseAudio is null) return [];
        try
        {
            using var process = Process.Start(MediaProbe.Start(MediaProbe.PulseAudio, "list", "short", "sinks"));
            if (process is null) return [];
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t'))
                .Where(parts => parts.Length >= 2)
                .Select(parts => new OutputDevice(parts[1], UserName(parts[1])))
                .ToArray();
        }
        catch { return []; }
    }

    static string UserName(string sink) =>
        sink.Replace("alsa_output.", "", StringComparison.Ordinal)
            .Replace(".analog-stereo", "", StringComparison.Ordinal)
            .Replace('_', ' ');

    public static string? DefaultDeviceName() => Devices().FirstOrDefault()?.Name;

    /// <summary>Starts a clip on the chosen sink, or the default output when none was named.</summary>
    public void Play(string path, string? deviceId, double volume, double startSeconds = 0)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;
        _path = path;
        _device = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        _volume = Math.Clamp(volume, 0, 1);
        _player.Play(path, _device, _volume, startSeconds);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _player.SetVolume(_volume);
    }

    public void Pause() => _player.Pause();
    public void Resume() => _player.Resume();
    public void Seek(double seconds) => _player.Seek(Math.Max(0, seconds));
    public double Position => _player.Position;

    public void Stop()
    {
        _player.Stop();
        _path = null;
        _device = null;
    }

    public void Dispose() => _player.Dispose();
}
