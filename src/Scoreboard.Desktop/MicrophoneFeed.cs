using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Scoreboard.Desktop;

/// <summary>A capture device the operator can add to the media library, by the name PulseAudio knows.</summary>
public sealed record MicrophoneDevice(int Index, string Name);

/// <summary>
/// A live microphone in the media library. Like a camera it has no file: the sound is captured from
/// the device and sent straight to the output the item is set to, so a commentary mic can feed the
/// broadcast mix or the arena speakers. Cueing another item releases the device, so two entries can
/// never hold the same microphone open.
/// </summary>
public sealed class MicrophoneFeed : IDisposable
{
    static readonly object Gate = new();
    static readonly Dictionary<int, MicrophoneFeed> Feeds = [];

    // float32le, stereo, 48 kHz matches the meter and both PulseAudio utilities, and lets the
    // monitor level be applied by scaling samples in the pump without a second format conversion.
    const int SampleRate = 48000;
    const int Channels = 2;

    readonly int _index;
    Process? _capture;
    Process? _playback;
    Thread? _pump;
    volatile bool _running;
    double _volume = 1;
    string? _source;
    string? _sink;

    MicrophoneFeed(int index) => _index = index;

    /// <summary>One feed per capture device, shared by every window and every cue.</summary>
    public static MicrophoneFeed Get(int index)
    {
        lock (Gate)
        {
            index = Math.Max(0, index);
            if (!Feeds.TryGetValue(index, out var feed))
            {
                feed = new MicrophoneFeed(index);
                Feeds[index] = feed;
            }
            return feed;
        }
    }

    /// <summary>Every capture source PulseAudio can see, with its description, in pactl's order.</summary>
    public static IReadOnlyList<MicrophoneDevice> NamedDevices() => PulseSources();

    /// <summary>The name of one microphone, falling back to its position when it cannot be read.</summary>
    public static string MicrophoneName(int index)
    {
        var devices = NamedDevices();
        return index >= 0 && index < devices.Count ? devices[index].Name : $"Microphone {index + 1}";
    }

    public int Index => _index;

    /// <summary>True while this microphone is being captured and monitored.</summary>
    public bool IsRunning => _running;

    /// <summary>Why the last capture or monitor attempt failed, if it did.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Opens the microphone and starts sending it to <paramref name="outputDeviceId"/> (null is the
    /// default sink). The pump runs a small buffer, so the operator hears the room a fraction of a
    /// second behind the microphone, not a clip behind.
    /// </summary>
    public void Start(string? outputDeviceId)
    {
        var source = SourceName(_index);
        if (source is null)
        {
            Stop();
            Error = $"Microphone {_index + 1} is not available.";
            return;
        }
        if (_running && string.Equals(_source, source, StringComparison.Ordinal)
            && string.Equals(_sink, outputDeviceId, StringComparison.Ordinal)) return;

        Stop();
        Error = null;
        if (MediaProbe.Parec is null || MediaProbe.Pacat is null)
        {
            Error = "Microphone capture needs parec and pacat (install pulseaudio-utils).";
            return;
        }

        try
        {
            var captureInfo = MediaProbe.Start(MediaProbe.Parec,
                $"--device={source}", "--format=float32le", $"--rate={SampleRate}", $"--channels={Channels}", "--raw");
            var playbackInfo = MediaProbe.Start(MediaProbe.Pacat,
                "--format=float32le", $"--rate={SampleRate}", $"--channels={Channels}", "--raw");
            if (!string.IsNullOrWhiteSpace(outputDeviceId))
                playbackInfo.ArgumentList.Insert(0, $"--device={outputDeviceId}");
            // pacat writes nothing to stdout; leaving that pipe redirected would just hold a handle.
            playbackInfo.RedirectStandardOutput = false;
            playbackInfo.RedirectStandardInput = true;

            _capture = Process.Start(captureInfo);
            _playback = Process.Start(playbackInfo);
            if (_capture is null || _playback is null)
            {
                Error = "PulseAudio capture could not be started.";
                Stop();
                return;
            }

            _source = source;
            _sink = outputDeviceId;
            _running = true;
            _pump = new Thread(() => Pump(_capture.StandardOutput.BaseStream, _playback.StandardInput.BaseStream))
            {
                IsBackground = true,
                Name = $"Microphone {_index + 1}"
            };
            _pump.Start();
        }
        catch (Exception ex)
        {
            Error = $"Microphone {_index + 1}: {ex.Message}";
            Stop();
        }
    }

    void Pump(Stream input, Stream output)
    {
        var buffer = new byte[16384];
        try
        {
            while (_running)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                Scale(buffer, read);
                output.Write(buffer, 0, read);
                output.Flush();
            }
        }
        catch { }
        finally
        {
            if (_running)
            {
                Error = $"Microphone {_index + 1} stopped.";
                _running = false;
            }
        }
    }

    /// <summary>The monitor level is applied to the captured float samples before they reach the sink.</summary>
    void Scale(byte[] buffer, int bytes)
    {
        var volume = (float)Math.Clamp(_volume, 0, 1);
        if (volume >= 0.999f) return;
        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes - bytes % 4));
        for (var index = 0; index < samples.Length; index++) samples[index] *= volume;
    }

    /// <summary>Changes where the microphone is heard, without releasing the capture device.</summary>
    public void SetOutput(string? outputDeviceId)
    {
        if (!_running)
        {
            _sink = outputDeviceId;
            return;
        }
        Start(outputDeviceId);
    }

    public void SetVolume(double volume) => _volume = Math.Clamp(volume, 0, 1);

    public void Stop()
    {
        _running = false;
        var capture = _capture;
        var playback = _playback;
        _capture = null;
        _playback = null;
        try { playback?.StandardInput.Close(); } catch { }
        try { if (capture is { HasExited: false }) capture.Kill(true); } catch { }
        try { if (playback is { HasExited: false }) playback.Kill(true); } catch { }
        var pump = _pump;
        _pump = null;
        if (pump is not null && pump != Thread.CurrentThread)
        {
            try { pump.Join(800); } catch { }
        }
        try { capture?.Dispose(); } catch { }
        try { playback?.Dispose(); } catch { }
        _source = null;
        _sink = null;
    }

    static string? SourceName(int index)
    {
        var devices = PulseSources();
        return index >= 0 && index < devices.Count ? devices[index].Name : null;
    }

    static IReadOnlyList<MicrophoneDevice> PulseSources()
    {
        if (MediaProbe.PulseAudio is null) return [];
        try
        {
            var info = MediaProbe.Start(MediaProbe.PulseAudio, "list", "sources");
            info.RedirectStandardError = false;
            using var process = Process.Start(info);
            if (process is null) return [];
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return ParseSources(text);
        }
        catch { return []; }
    }

    /// <summary>
    /// Reads `pactl list sources` output. Monitor sources echo a sink, so they are not microphones;
    /// the description is what an operator recognises in the picker.
    /// </summary>
    public static IReadOnlyList<MicrophoneDevice> ParseSources(string text)
    {
        var devices = new List<MicrophoneDevice>();
        foreach (var block in (text ?? "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            string? description = null;
            foreach (var raw in block.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Name:", StringComparison.Ordinal)) name = line["Name:".Length..].Trim();
                else if (line.StartsWith("Description:", StringComparison.Ordinal)) description = line["Description:".Length..].Trim();
            }
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase)) continue;
            devices.Add(new MicrophoneDevice(devices.Count,
                string.IsNullOrWhiteSpace(description) ? FriendlyName(name) : description));
        }
        return devices;
    }

    static string FriendlyName(string source) =>
        source.Replace("alsa_input.", "", StringComparison.Ordinal)
            .Replace(".analog-stereo", "", StringComparison.Ordinal)
            .Replace('_', ' ');

    public void Dispose() => Stop();
}
