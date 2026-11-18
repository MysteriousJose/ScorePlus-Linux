// ScorePlus Linux port — WPF compatibility shims, layer 4 (layout read-back and text casing).
// Everything here is glue over Avalonia's real layout controls.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Scoreboard.Desktop;

/// <summary>Owner type for the WPF CharacterCasing knob Avalonia's TextBox does not have.</summary>
public sealed class WpfTextBoxCasing
{
    public static readonly AttachedProperty<CharacterCasing> CharacterCasingProperty =
        AvaloniaProperty.RegisterAttached<WpfTextBoxCasing, TextBox, CharacterCasing>("CharacterCasing", CharacterCasing.Normal);

    // A cased box is wired once; the flag keeps a repeated assignment from stacking handlers.
    internal static readonly HashSet<TextBox> Wired = [];
}

public static class WpfCompat4Extensions
{
    // WPF's ActualWidth/ActualHeight read the laid-out size. Avalonia keeps the same numbers on the
    // visual's Bounds, already in the parent's coordinate space.
    extension(Visual visual)
    {
        public double ActualWidth => visual.Bounds.Width;
        public double ActualHeight => visual.Bounds.Height;
    }

    // TECH DEBT: WPF filters keystrokes; this re-cases the text after each edit, so the caret can
    // jump inside a selection. TODO(port): a KeyDown filter, or a custom TextBox once the fields move.
    extension(TextBox box)
    {
        public CharacterCasing CharacterCasing
        {
            get => box.GetValue(WpfTextBoxCasing.CharacterCasingProperty);
            set
            {
                box.SetValue(WpfTextBoxCasing.CharacterCasingProperty, value);
                if (value == CharacterCasing.Normal) return;
                if (!WpfTextBoxCasing.Wired.Add(box)) return;
                box.TextChanged += (_, _) =>
                {
                    var wanted = value == CharacterCasing.Upper ? box.Text?.ToUpperInvariant() : box.Text?.ToLowerInvariant();
                    if (wanted is null || wanted == box.Text) return;
                    var caret = box.CaretIndex;
                    box.Text = wanted;
                    box.CaretIndex = Math.Min(caret, wanted.Length);
                };
            }
        }
    }
}

