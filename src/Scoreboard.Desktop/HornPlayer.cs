using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Scoreboard.Core;
// Scoreboard.Core and Avalonia.Controls both have a Path type; this file wants the file system.
using Path = System.IO.Path;

namespace Scoreboard.Desktop;

public enum HornSound
{
    AirHorn,
    Buzzer,
    Siren,
    Custom
}

/// <summary>
/// A small audio channel for the console horn. Bundled recordings play through the same
/// PulseAudio-aware router as clips; the synthesizer stays as a fallback when an asset is missing.
/// </summary>
public static class HornPlayer
{
    const double TwoPi = Math.PI * 2;
    const int SampleRate = 44100;
    const double BuzzerHoldSeconds = 8.0;

    static readonly object Gate = new();
    static readonly AudioRouter Audio = new();

    /// <summary>Raised when a horn starts; useful to diagnostics and unattended checks.</summary>
    public static event Action<HornSound>? Started;

    public static bool Play(string? sound, string? customPath)
    {
        var kind = Parse(sound);
        lock (Gate)
        {
            Audio.Stop();
            Started?.Invoke(kind);
            try
            {
                if (kind == HornSound.Custom && !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    Audio.Play(customPath, null, 0.9);
                    return true;
                }
                if (kind is HornSound.AirHorn or HornSound.Buzzer or HornSound.Siren && TryPlayBundled(kind))
                    return true;

                var folder = AppPaths.Combine("Horn");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"horn-{kind}.wav");
                WriteWave(path, Generate(kind), SampleRate);
                Audio.Play(path, null, 0.72);
                return true;
            }
            catch
            {
                Audio.Stop();
                return false;
            }
        }
    }

    public static void Stop()
    {
        lock (Gate) Audio.Stop();
    }

    static bool TryPlayBundled(HornSound kind)
    {
        var file = kind switch
        {
            HornSound.AirHorn => "horn-air.mp3",
            HornSound.Buzzer => "horn-buzzer.mp3",
            HornSound.Siren => "horn-siren.mp3",
            _ => null
        };
        if (file is null) return false;
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "Assets"), AppPaths.Combine("Horn") })
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path)) continue;
            Audio.Play(path, null, kind == HornSound.Buzzer ? 0.70 : 0.82);
            return true;
        }
        return false;
    }

    public static HornSound Parse(string? value) => Enum.TryParse<HornSound>(value, true, out var result)
        ? result
        : HornSound.Buzzer;

    static void WriteWave(string path, byte[] pcm, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    static byte[] Generate(HornSound kind)
    {
        var seconds = kind switch
        {
            HornSound.Buzzer => BuzzerHoldSeconds,
            HornSound.Siren => 2.25,
            _ => 1.35
        };
        var attack = kind switch
        {
            HornSound.Buzzer => 0.012,
            HornSound.Siren => 0.12,
            _ => 0.045
        };
        var release = kind switch
        {
            HornSound.Buzzer => 0.14,
            HornSound.Siren => 0.32,
            _ => 0.20
        };
        var samples = (int)(SampleRate * seconds);
        var data = new byte[samples * 2];
        double phase1 = 0, phase2 = 0, phase3 = 0, phase4 = 0;

        for (var index = 0; index < samples; index++)
        {
            var time = (double)index / SampleRate;
            var remaining = seconds - time;
            var envelope = Smooth(Math.Min(1, time / attack)) * Smooth(Math.Min(1, remaining / release));
            double sample;

            switch (kind)
            {
                case HornSound.Buzzer:
                {
                    const double frequency = 196;
                    phase1 += TwoPi * frequency / SampleRate;
                    phase2 += TwoPi * frequency * 2 / SampleRate;
                    phase3 += TwoPi * frequency * 3 / SampleRate;
                    phase4 += TwoPi * frequency * 4 / SampleRate;
                    sample = Math.Sin(phase1) * 0.34
                        + Math.Sin(phase2) * 0.14
                        + Math.Sin(phase3) * 0.065
                        + Math.Sin(phase4) * 0.025;
                    sample *= 0.92 + 0.08 * Math.Sin(TwoPi * 12 * time);
                    break;
                }
                case HornSound.Siren:
                {
                    var sweep = 500 + 175 * Math.Sin(TwoPi * 0.48 * time - Math.PI / 2);
                    phase1 += TwoPi * sweep / SampleRate;
                    phase2 += TwoPi * sweep * 2 / SampleRate;
                    phase3 += TwoPi * sweep * 3 / SampleRate;
                    sample = Math.Sin(phase1) * 0.34
                        + Math.Sin(phase2) * 0.085
                        + Math.Sin(phase3) * 0.025;
                    break;
                }
                default:
                {
                    var fundamental = 220 * (1 + 0.003 * Math.Sin(TwoPi * 5.1 * time));
                    phase1 += TwoPi * fundamental / SampleRate;
                    phase2 += TwoPi * fundamental * 1.007 / SampleRate;
                    phase3 += TwoPi * fundamental * 2 / SampleRate;
                    phase4 += TwoPi * fundamental * 3 / SampleRate;
                    sample = Math.Sin(phase1) * 0.31
                        + Math.Sin(phase2) * 0.20
                        + Math.Sin(phase3) * 0.10
                        + Math.Sin(phase4) * 0.045;
                    break;
                }
            }

            sample = Math.Tanh(sample * 1.12);
            sample *= envelope;
            var value = (short)(Math.Clamp(sample, -1, 1) * short.MaxValue * 0.78);
            data[index * 2] = (byte)(value & 0xFF);
            data[index * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return data;
    }

    static double Smooth(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
