using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The typed-path fallback for a Linux desktop with no native picker or portal. Every media path in
/// this app ends at a real file for ffmpeg, so a path box is a complete answer rather than a
/// degraded one. Returns the absolute path, or null when the operator cancels.
/// </summary>
public sealed class PathPromptWindow : Window
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new();
    readonly bool _save;
    readonly bool _folder;
    readonly bool _checkExists;
    readonly bool _overwrite;
    string? _result;
    bool _overwriteConfirmed;

    PathPromptWindow(string title, string initial, bool save, bool folder, bool checkExists, bool overwrite)
    {
        _save = save;
        _folder = folder;
        _checkExists = checkExists;
        _overwrite = overwrite;

        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Type or paste a full path",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = folder
                ? "A folder that already exists on this machine."
                : save
                    ? "The file to write. Its folder must already exist; the file may be replaced."
                    : "A file that already exists on this machine.",
            FontSize = 11,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _input.Text = initial;
        _input.FontSize = 13;
        _input.Padding = new Thickness(8, 6, 8, 6);
        panel.Children.Add(_input);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114));
        _error.FontSize = 11;
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button
        {
            Content = save ? "Save" : folder ? "Choose" : "Open",
            IsDefault = true,
            MinWidth = 88,
            Padding = new Thickness(12, 6, 12, 6)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 88,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(6, 0, 0, 0)
        };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    void Accept()
    {
        var typed = (_input.Text ?? "").Trim();
        if (typed.Length == 0) { Fail("Type a path first."); return; }
        string full;
        try { full = Path.GetFullPath(typed); }
        catch (Exception ex) { Fail(ex.Message); return; }

        if (_folder)
        {
            if (!Directory.Exists(full)) { Fail("That folder does not exist."); return; }
        }
        else
        {
            var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                Fail("The folder part of that path does not exist.");
                return;
            }
            if (_save)
            {
                if (File.Exists(full) && _overwrite && !_overwriteConfirmed)
                {
                    _overwriteConfirmed = true;
                    Fail("That file exists. Press Save again to overwrite it.");
                    return;
                }
            }
            else if (_checkExists && !File.Exists(full))
            {
                Fail("That file does not exist.");
                return;
            }
        }

        _result = full;
        DialogResult = true;
    }

    void Fail(string message) => _error.Text = message;

    /// <summary>Asks for one path. Returns null when there is no application or the operator cancels.</summary>
    public static string? Ask(Avalonia.Controls.Window? owner, string title, string initial,
        bool save, bool folder, bool checkExists = true, bool overwrite = true)
    {
        if (Avalonia.Application.Current is null) return null;
        var dialog = new PathPromptWindow(title, initial, save, folder, checkExists, overwrite);
        // Sets the Avalonia owner directly: callers can hand us either the port's compat Window or
        // a plain Avalonia window, and the shadowed Owner property only accepts the former.
        if (owner is Window compat) dialog.Owner = compat;
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
