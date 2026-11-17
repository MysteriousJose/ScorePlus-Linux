using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// An HTML element on the graphic. The markup is drawn by a real browser engine and captured to an
/// image, so the studio, both previews and the audience output all show the identical picture.
/// A live browser window cannot be used directly: it is a native window that Avalonia cannot scale
/// inside the viewbox every feed draws through, and it would float above the rest of the artwork.
/// </summary>
public sealed class HtmlSurface : Grid
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly Border _placeholder;
    readonly bool _showPlaceholder;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(160) };
    HtmlEngine? _engine;
    ScorebugElement _item;
    string _renderedHtml = "";
    int _renderedWidth, _renderedHeight;
    bool _busy;
    bool _disposed;
    int _attempts;

    public HtmlSurface(ScorebugElement item, bool showPlaceholder)
    {
        _item = item;
        _showPlaceholder = showPlaceholder;
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x38, 0xD7, 0xA3)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x38, 0xD7, 0xA3)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "HTML",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xF4, 0xF7, 0xFB)),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            }
        };
        if (!showPlaceholder) _placeholder.Background = Brushes.Transparent;
        Children.Add(_image);
        Children.Add(_placeholder);
        IsHitTestVisible = false;
        _refresh.Tick += (_, _) => Pump();
        Render();
    }

    /// <summary>Follows the element's markup and box size, without rebuilding the surface.</summary>
    public void Update(ScorebugElement item)
    {
        _item = item;
        Render();
    }

    /// <summary>Forces a fresh frame, used when the graphic is rebuilt.</summary>
    public void Refresh() => Render();

    void Render()
    {
        if (_disposed) return;
        var html = _item.Content ?? "";
        var width = Math.Max(16, (int)Math.Round(_item.Width));
        var height = Math.Max(16, (int)Math.Round(_item.Height));
        if (string.IsNullOrWhiteSpace(html))
        {
            _renderedHtml = html;
            _image.Source = null;
            _placeholder.Visibility = Visibility.Collapsed;
            _refresh.Stop();
            return;
        }
        var changed = html != _renderedHtml || width != _renderedWidth || height != _renderedHeight;
        if (!changed && _image.Source is not null) return;
        _renderedHtml = html;
        _renderedWidth = width;
        _renderedHeight = height;
        // Markup that animates has to keep being sampled; a still page only needs one capture.
        // A page that has not arrived yet is retried a few times — the first capture can race the
        // browser starting up, and a blank element on air would look broken.
        _attempts = 0;
        if (IsAnimated(html) || _image.Source is null) _refresh.Start(); else _refresh.Stop();
        Pump();
    }

    /// <summary>Captures a frame, unless the last one is still being taken.</summary>
    async void Pump()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            _engine ??= new HtmlEngine();
            var frame = await _engine.RenderAsync(_renderedHtml, _renderedWidth, _renderedHeight);
            if (frame is null)
            {
                // Nothing to show rather than a wrong picture: the studio draws a small HTML badge,
                // the previews and the audience stay clear. Keep trying for a moment, then settle.
                _attempts++;
                if (_attempts > 12) _refresh.Stop();
                _placeholder.Visibility = _showPlaceholder && _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            _image.Source = frame;
            _attempts = 0;
            if (!IsAnimated(_renderedHtml)) _refresh.Stop();
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            HtmlEngine.Note(ex);
            _attempts++;
            if (_attempts > 12) _refresh.Stop();
            _placeholder.Visibility = _showPlaceholder && _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _busy = false; }
    }

    static bool IsAnimated(string html)
    {
        foreach (var marker in new[] { "@keyframes", "animation", "<video", "<canvas", "<marquee", "requestAnimationFrame" })
            if (html.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _refresh.Stop();
        _engine?.Dispose();
        _engine = null;
    }
}

/// <summary>
/// Captures HTML with the system Chromium/Chrome in headless mode. Each frame is a real browser
/// render written as a transparent PNG, then loaded into the Avalonia image the graphic draws.
/// Chromium's virtual clock advances between captures, so CSS/JS animations are sampled instead of
/// freezing on their first frame. A machine without a browser gets null frames and a placeholder.
/// </summary>
public sealed class HtmlEngine : IDisposable
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    readonly Stopwatch _clock = Stopwatch.StartNew();
    string _loaded = "";
    string? _work;
    string? _page;
    string? _shot;
    bool _disposed;

    /// <summary>The last failure, so a blank HTML element can be explained instead of guessed at.</summary>
    public static string? LastError { get; private set; }

    /// <summary>Records a browser failure in the diagnostics folder; never throws.</summary>
    public static void Note(Exception ex)
    {
        LastError = ex.Message;
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "html-last-error.txt"), $"{DateTimeOffset.Now:O} {ex}");
        }
        catch { }
    }

    string Wrap(string html) => html.Contains("<html", StringComparison.OrdinalIgnoreCase)
        ? html
        : "<!doctype html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0;padding:0;overflow:hidden;width:100%;height:100%;background:transparent;font-family:Segoe UI,Arial,sans-serif;color:#fff}</style></head><body>" + html + "</body></html>";

    public async Task<BitmapSource?> RenderAsync(string html, int width, int height)
    {
        if (_disposed) return null;
        await Gate.WaitAsync();
        try
        {
            var executable = FindBrowser();
            if (executable is null)
            {
                Note(new InvalidOperationException("no Chromium or Chrome found; install chromium or set SCOREPLUS_CHROME"));
                return null;
            }
            EnsureWorkDir();
            if (!string.Equals(_loaded, html, StringComparison.Ordinal) || !File.Exists(_page))
            {
                _loaded = html;
                _clock.Restart();
                await File.WriteAllTextAsync(_page!, Wrap(html));
            }
            try { File.Delete(_shot!); } catch { }

            // Let the page load, then sample the animation at the elapsed virtual time.
            var budget = Math.Clamp((int)_clock.ElapsedMilliseconds + 800, 800, 5000);
            var start = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "--headless=new",
                "--no-sandbox",
                "--disable-gpu",
                "--disable-dev-shm-usage",
                "--hide-scrollbars",
                "--mute-audio",
                "--no-first-run",
                "--disable-extensions",
                "--disable-background-networking",
                "--run-all-compositor-stages-before-draw",
                $"--virtual-time-budget={budget.ToString(CultureInfo.InvariantCulture)}",
                $"--window-size={width.ToString(CultureInfo.InvariantCulture)},{height.ToString(CultureInfo.InvariantCulture)}",
                "--force-device-scale-factor=1",
                "--default-background-color=00000000",
                $"--user-data-dir={Path.Combine(_work!, "profile")}",
                $"--screenshot={_shot}",
                new Uri(_page!).AbsoluteUri
            }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null)
            {
                Note(new InvalidOperationException($"{executable} could not be started"));
                return null;
            }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException($"{executable} did not finish the HTML capture");
            }
            var log = (await stderr).Trim();
            if (!File.Exists(_shot))
                throw new InvalidOperationException(log.Length > 0 ? log : $"{executable} wrote no screenshot");
            var bytes = await File.ReadAllBytesAsync(_shot);
            if (bytes.Length == 0)
                throw new InvalidOperationException("the browser wrote an empty screenshot");
            using var stream = new MemoryStream(bytes);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            var frame = BitmapSource.Wrap(bitmap);
            // Avalonia's headless platform decodes every bitmap as 1x1. The bytes are the real
            // browser capture, so take the dimensions from the PNG header and let the wrapper carry
            // them; the desktop backend decodes the same bytes at the same size.
            var (pngWidth, pngHeight) = PngSize(bytes);
            if (pngWidth > 0 && pngHeight > 0)
            {
                frame.PixelWidth = pngWidth;
                frame.PixelHeight = pngHeight;
            }
            return frame;
        }
        catch (Exception ex)
        {
            if (!_disposed) Note(ex);
            return null;
        }
        finally { Gate.Release(); }
    }

    static (int Width, int Height) PngSize(byte[] bytes)
    {
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            return (0, 0);
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return width > 0 && height > 0 ? (width, height) : (0, 0);
    }

    void EnsureWorkDir()
    {
        if (_work is not null) return;
        _work = Path.Combine(Path.GetTempPath(), $"scoreplus-html-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
        _page = Path.Combine(_work, "frame.html");
        _shot = Path.Combine(_work, "frame.png");
    }

    static string? FindBrowser()
    {
        foreach (var variable in new[] { "SCOREPLUS_CHROME", "SCOREPLUS_BROWSER", "CHROME_PATH" })
        {
            var forced = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(forced) && File.Exists(forced)) return forced;
        }

        var names = new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "microsoft-edge", "microsoft-edge-stable", "brave-browser" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var name in names)
        {
            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
                if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
        }

        foreach (var candidate in new[]
        {
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/snap/bin/chromium",
            "/opt/google/chrome/chrome",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        }) if (File.Exists(candidate)) return candidate;
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_work is { } work)
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
        _work = null;
        _page = null;
        _shot = null;
    }
}
