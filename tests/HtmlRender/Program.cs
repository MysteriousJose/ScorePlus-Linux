using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scoreboard.Core;
using Scoreboard.Desktop;
using Dispatcher = Avalonia.Threading.Dispatcher;
using Window = Avalonia.Controls.Window;

namespace HtmlRender;

/// <summary>
/// An HTML element has one contract on every operating system: it settles into a real captured
/// frame when a browser runtime exists, or into the small HTML placeholder with a recorded reason
/// when it does not. It never throws and never leaves a blank, unexplained box on the graphic.
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main()
    {
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        var root = Path.Combine(Path.GetTempPath(), $"html-render-{Guid.NewGuid():N}");
        AppPaths.UseRoot(root);
        try
        {
            var element = new ScorebugElement
            {
                Kind = ScorebugElementKind.Html,
                Width = 320,
                Height = 120,
                TransparentBackground = true,
                Content = "<div style=\"font:700 40px Arial;color:#38D7A3\">HTML OK</div>"
            };
            var surface = new HtmlSurface(element, showPlaceholder: true);
            var host = new Window { Width = 360, Height = 200, Content = surface };
            host.Show();
            host.UpdateLayout();

            var image = surface.GetVisualDescendants().OfType<Image>().Single();
            var placeholder = surface.GetVisualDescendants().OfType<Border>().Single();
            // HtmlEngine allows a browser capture 15 seconds to finish. The test must wait past
            // that timeout, otherwise a slow or failing browser is mistaken for a missing one
            // before the explained placeholder has had a chance to appear.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (image.Source is null && !placeholder.IsVisible && DateTime.UtcNow < deadline)
                Pump(TimeSpan.FromMilliseconds(200));

            var noBrowser = image.Source is null;
            if (!noBrowser)
            {
                Check(image.Source!.Size.Width >= 16 && image.Source.Size.Height >= 16,
                    "the browser engine produced a frame at the element's capture size");
            }
            else
            {
                Check(placeholder.IsVisible
                      && placeholder.Child is TextBlock { Text: "HTML" }
                      && !string.IsNullOrWhiteSpace(HtmlEngine.LastError),
                    "without a browser runtime the element shows the studio badge and records why");
                Console.WriteLine($"PASS: no browser runtime on this OS; the element explains itself ({HtmlEngine.LastError})");
            }

            // An empty element is deliberately clear: no placeholder, no frame.
            element.Content = "";
            surface.Update(element);
            Pump(TimeSpan.FromMilliseconds(100));
            Check(image.Source is null && !placeholder.IsVisible,
                "an empty HTML element stays clear on the graphic");

            // Previews and the audience pass showPlaceholder:false: the studio badge must never
            // leak onto air, whether the browser produced a frame or not.
            element.Content = "<div style=\"font:700 40px Arial;color:#38D7A3\">HTML OK</div>";
            var onAir = new HtmlSurface(element, showPlaceholder: false);
            var onAirHost = new Window { Width = 360, Height = 200, Content = onAir };
            onAirHost.Show();
            onAirHost.UpdateLayout();
            var onAirImage = onAir.GetVisualDescendants().OfType<Image>().Single();
            var onAirPlaceholder = onAir.GetVisualDescendants().OfType<Border>().Single();
            if (!noBrowser)
            {
                // A second surface starts its own browser, so give it the same room as the first.
                var onAirDeadline = DateTime.UtcNow.AddSeconds(20);
                while (onAirImage.Source is null && DateTime.UtcNow < onAirDeadline)
                    Pump(TimeSpan.FromMilliseconds(200));
            }
            else
            {
                Pump(TimeSpan.FromSeconds(1));
            }
            Check(!onAirPlaceholder.IsVisible, "the on-air HTML surface never shows the studio badge");
            Check(noBrowser ? onAirImage.Source is null : onAirImage.Source is not null,
                "the on-air HTML surface matches the browser state without a badge");
            onAir.Dispose();
            onAirHost.Close();

            surface.Dispose();
            host.Close();
            Dispatcher.UIThread.RunJobs();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
    }
}

/// <summary>The real App, minus the desktop lifetime: headless checks own the windows.</summary>
internal sealed class HeadlessApp : App
{
    public override void OnFrameworkInitializationCompleted() { }
}
