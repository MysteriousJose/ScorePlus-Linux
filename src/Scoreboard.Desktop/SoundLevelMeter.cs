using System.Diagnostics;
using System.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// A peak meter for what this machine is actually sending to its speakers. It listens to the default
/// PulseAudio monitor rather than to one clip, so it reads a live camera mix, a replay or a routed ad
/// exactly as the room hears them, and true silence settles at zero. One capture is shared by every
/// window, because the machine has one output.
/// </summary>
public static class SoundLevelMeter
{
    static readonly object Gate = new();
    static readonly Process? Capture;
    static readonly Thread? Reader;
    static float _held;
    static double _level;
    static DateTime _last = DateTime.UtcNow;

    static SoundLevelMeter()
    {
        if (MediaProbe.Parec is null) { Available = false; return; }
        try
        {
            var info = MediaProbe.Start(MediaProbe.Parec,
                "--device=@DEFAULT_MONITOR@", "--format=float32le", "--rate=48000",
                "--channels=2", "--raw");
            info.RedirectStandardError = true;
            Capture = Process.Start(info);
            if (Capture is null) { Available = false; return; }
            Available = true;
            Reader = new Thread(() => ReadLoop(Capture)) { IsBackground = true, Name = "PulseAudio meter" };
            Reader.Start();
        }
        catch
        {
            try { Capture?.Dispose(); } catch { }
            Capture = null;
            Available = false;
        }
    }

    /// <summary>False when there is no PulseAudio monitor to listen to; the meter then stays dark.</summary>
    public static bool Available { get; }

    /// <summary>
    /// A window may mute every speaker bus and still leave the monitor running. The gate lets the
    /// media session say nobody can hear the output, so the meter reads silence instead of a mix
    /// the room never receives.
    /// </summary>
    public static Func<bool>? AudibleGate { get; set; }

    public static bool Suppressed => AudibleGate?.Invoke() == false;

    static void ReadLoop(Process process)
    {
        try
        {
            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[4096];
            while (!process.HasExited)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                var peak = 0f;
                // parec is asked for float32 little-endian stereo; every four bytes is one sample.
                for (var offset = 0; offset + 4 <= read; offset += 4)
                {
                    var sample = Math.Abs(BitConverter.ToSingle(buffer, offset));
                    if (sample > peak) peak = sample;
                }
                if (!float.IsFinite(peak)) continue;
                lock (Gate) { if (peak > _held) _held = peak; }
            }
        }
        catch { }
    }

    /// <summary>
    /// The level to draw, 0–1. It rises instantly to the loudest sample since the last read and falls
    /// with a slow, time-based decay, so the bar is readable instead of strobing.
    /// </summary>
    public static double Read()
    {
        if (Suppressed) return 0;
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            var seconds = Math.Clamp((now - _last).TotalSeconds, 0, 1);
            _last = now;
            var decayed = _level * Math.Pow(0.10, seconds / 0.55);
            _level = Math.Max(Math.Min(1, _held), decayed);
            _held = 0;
            if (_level < 0.002) _level = 0;
            return _level;
        }
    }

    /// <summary>The level in dBFS, which is what an operator reads: 0 dB is full scale.</summary>
    public static double Decibels(double level) => level <= 0 ? double.NegativeInfinity : 20 * Math.Log10(level);
}
