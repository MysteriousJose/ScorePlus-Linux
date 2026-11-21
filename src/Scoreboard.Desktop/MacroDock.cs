using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The Macros tab: a board of one-press buttons on the left and the ordered step editor on the
/// right. Macros live in game state, so a saved game brings the show's shortcuts back with it.
/// </summary>
public sealed class MacroDock : UserControl
{
    const int EditorWidth = 360;
    static readonly string[] MacroColors =
    [
        "#38D7A3", "#4C9AFF", "#FF6B6B", "#FFB86B",
        "#B98CFF", "#F1C40F", "#2ECC71", "#95A5A6"
    ];

    readonly GameEngine _engine;
    readonly MediaSession _session;
    readonly ScorebugCatalog? _catalog;
    readonly MacroRunner _runner;
    List<SavedMacro> _macros = [];
    SavedMacro? _draft;
    bool _syncing;
    bool _dirty;
    bool _editorVisible = true;
    bool _showHints;
    int _page;
    int _pageCount = 1;

    // Below this width the board and the editor stack, so the fixed editor column never squeezes
    // the macro buttons off the panel.
    const double StackedBelowWidth = 900;
    readonly Grid _root = new();
    readonly ColumnDefinition _editorColumn = new() { Width = new GridLength(EditorWidth) };
    readonly ColumnDefinition _splitterColumn = new() { Width = new GridLength(8) };
    readonly DockPanel _board = new() { Margin = new Thickness(0, 0, 4, 0) };
    readonly Border _editorHost = new()
    {
        Background = Brush("#080808"),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(12, 10),
        BorderBrush = Brush("#383838"),
        BorderThickness = new Thickness(1)
    };
    readonly Border _splitter = new() { Width = 8, Background = Brush("#383838"), Margin = new Thickness(4, 0) };
    bool _stacked;
    readonly Grid _macroButtons = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _emptyText = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 8, 0) };
    readonly TextBlock _pageLabel = new() { FontSize = 10, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
    readonly Button _prevPage = new() { Content = "◀", MinWidth = 30, Padding = new Thickness(6, 3), Margin = new Thickness(0, 0, 4, 0) };
    readonly Button _nextPage = new() { Content = "▶", MinWidth = 30, Padding = new Thickness(6, 3), Margin = new Thickness(0, 0, 8, 0) };
    readonly Button _addPage = new() { Content = "+ Page", Padding = new Thickness(8, 3) };
    readonly Button _editorToggle = new() { Content = "Hide editor", Margin = new Thickness(0, 0, 6, 0) };

    readonly TextBlock _editStatus = new() { FontSize = 9, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _nameBox = new() { MinHeight = 32 };
    readonly TextBox _colorBox = new() { MinHeight = 32, Margin = new Thickness(7, 0, 0, 0) };
    readonly Border _colorPreview = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
    readonly WrapPanel _colorChoices = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly Button _saveButton = new() { Content = "Save macro" };
    readonly Button _deleteButton = new() { Content = "Delete" };
    readonly ListBox _stepList = new() { Height = 150, FontSize = 11 };
    readonly ComboBox _kind = new() { MinHeight = 32 };
    readonly ComboBox _side = new() { MinHeight = 32 };
    readonly TextBox _amount = new() { Text = "1", MinHeight = 32 };
    readonly TextBox _value = new() { Text = "0", MinHeight = 32, AcceptsReturn = false };
    readonly TextBox _wait = new() { Text = "1", MinHeight = 32 };
    readonly ComboBox _media = new() { MinHeight = 32 };
    readonly TextBox _path = new() { MinHeight = 32 };
    readonly ComboBox _flag = new() { MinHeight = 32 };
    readonly ComboBox _layout = new() { MinHeight = 32 };
    readonly ComboBox _command = new() { MinHeight = 32 };
    readonly ComboBox _sportValue = new() { MinHeight = 32 };
    readonly ComboBox _customValue = new() { MinHeight = 32 };
    readonly ComboBox _player = new() { MinHeight = 32 };
    readonly TextBox _stat = new() { MinHeight = 32 };
    readonly CheckBox _delta = new() { Content = "Add to the current value instead of replacing it", FontSize = 10 };
    readonly CheckBox _bypass = new() { Content = "Bypass Update Output for this step", FontSize = 10, Margin = new Thickness(0, 9, 0, 2) };
    readonly CheckBox _sportDelta = new() { Content = "Add to the current value", FontSize = 10, Margin = new Thickness(0, 5, 0, 0) };
    readonly CheckBox _customDelta = new() { Content = "Add to the current value", FontSize = 10, Margin = new Thickness(0, 5, 0, 0) };
    readonly CheckBox _clearFlag = new() { Content = "Clear the flag instead of putting it down", FontSize = 10, Margin = new Thickness(0, 5, 0, 0) };
    readonly TextBlock _valueLabel = new() { FontSize = 9, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 7, 0, 3) };
    readonly TextBlock _mediaHint = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    readonly StackPanel _sidePanel = new();
    readonly StackPanel _amountPanel = new();
    readonly StackPanel _valuePanel = new();
    readonly StackPanel _waitPanel = new();
    readonly StackPanel _rosterPanel = new();
    readonly StackPanel _mediaPanel = new();
    readonly StackPanel _flagPanel = new();
    readonly StackPanel _layoutPanel = new();
    readonly StackPanel _commandPanel = new();
    readonly StackPanel _sportPanel = new();
    readonly StackPanel _customPanel = new();
    readonly Button _addStep = new() { Content = "+ Add step" };
    readonly Button _updateStep = new() { Content = "Update selected" };

    IBrush _panel = Brush("#080808");
    IBrush _surface = Brush("#141414");
    IBrush _input = Brush("#050505");
    IBrush _text = Brush("#F4F7FB");
    IBrush _muted = Brush("#B0B0B0");
    IBrush _border = Brush("#383838");
    IBrush _accent = Brush("#38D7A3");
    IBrush _accentSoft = Brush("#123C33");
    IBrush _accentText = Brush("#F4F7FB");
    IBrush _hover = Brush("#2A2A2A");
    IBrush _selection = Brush("#123C33");
    IBrush _selectionText = Brush("#F4F7FB");

    sealed record KindChoice(MacroActionKind Kind, string Label) { public override string ToString() => Label; }
    sealed record PlayerChoice(string Id, string Label) { public override string ToString() => Label; }
    sealed record CommandChoice(string Id, string Label) { public override string ToString() => Label; }
    sealed record SportValueChoice(string Id, string Label) { public override string ToString() => Label; }
    sealed record CustomValueChoice(string Id, string Label) { public override string ToString() => Label; }
    sealed record MediaChoice(string Path, string Label) { public override string ToString() => Label; }
    sealed record LayoutChoice(string DisplayId, string LayoutId, string Label) { public override string ToString() => Label; }
    sealed record MacroPlacement(SavedMacro Macro, int Row, int Column);

    public event Action<string>? Status;

    public MacroDock(GameEngine engine, MediaSession session, string? theme = null, Action<string>? executeControl = null,
        ScorebugCatalog? catalog = null, Action? publishOutput = null)
    {
        _engine = engine;
        _session = session;
        _catalog = catalog;
        _runner = new MacroRunner(engine, session, executeControl, catalog, publishOutput);
        ThemeManager.SetSkipTheme(this, true);
        _showHints = TipsEnabled();
        Build();
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        FillKindChoices();
        _side.ItemsSource = new[] { "Home", "Visitors" };
        _side.SelectedIndex = 0;
        ApplyTheme(theme ?? PreferencesStore.Load().Theme);
        RefreshView(force: true);
    }

    public void CommitPendingEdits()
    {
        if (_dirty) SaveDraft(false);
    }

    public void SetHintVisibility(bool show)
    {
        if (_showHints == show) return;
        _showHints = show;
        RenderMacros();
    }

    public void RefreshView(bool force = false)
    {
        if (force) { _draft = null; _dirty = false; }
        var selectedId = _draft?.Id;
        _macros = _engine.State.Macros.Select(macro => macro.Copy()).ToList();
        _pageCount = Math.Clamp(_engine.State.MacroPages, 1, 99);
        if (_macros.Count > 0) _pageCount = Math.Max(_pageCount, _macros.Max(macro => macro.Page) + 1);
        _page = Math.Clamp(_page, 0, _pageCount - 1);
        if (_draft is null || !_dirty)
        {
            var selected = MacrosOnPage.FirstOrDefault(macro => macro.Id == selectedId) ?? MacrosOnPage.FirstOrDefault();
            _draft = selected?.Copy();
        }
        RenderMacros();
        LoadEditor();
    }

    public void ApplyTheme(string theme)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        _panel = Brush(light ? "#FFFFFF" : "#080808");
        _surface = Brush(light ? "#E5EAF0" : "#141414");
        _input = Brush(light ? "#FFFFFF" : "#050505");
        _text = Brush(light ? "#18202B" : "#F4F7FB");
        _muted = Brush(light ? "#586576" : "#B0B0B0");
        _border = Brush(light ? "#B9C3CE" : "#383838");
        _hover = Brush(light ? "#D9E2EC" : "#2A2A2A");
        var accent = UiPalette.Accent(PreferencesStore.Load());
        var soft = light ? UiPalette.Mix(accent, Colors.White, 0.78) : UiPalette.Soft(accent);
        _accent = new SolidColorBrush(accent);
        _accentSoft = new SolidColorBrush(soft);
        _accentText = new SolidColorBrush(Readable(soft));
        _selection = _accentSoft;
        _selectionText = _text;
        Background = _panel;
        RenderMacros();
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void Build()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(_splitterColumn);
        _root.ColumnDefinitions.Add(_editorColumn);
        _splitterColumn.Width = new GridLength(8);
        _splitterColumn.MinWidth = 0;

        var board = _board;
        var top = new StackPanel();
        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _editorToggle.Click += (_, _) => ToggleEditor();
        var newMacro = new Button { Content = "+ New macro" };
        newMacro.Click += (_, _) => NewMacro();
        actions.Children.Add(_editorToggle);
        actions.Children.Add(newMacro);
        DockPanel.SetDock(actions, Dock.Right);
        titleRow.Children.Add(actions);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new Border { Width = 3, Height = 13, CornerRadius = new CornerRadius(2), Background = _accent, Margin = new Thickness(0, 0, 7, 0) });
        title.Children.Add(new TextBlock { Text = "MACROS", FontSize = 13, FontWeight = FontWeight.Bold, Foreground = _muted, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(title);
        top.Children.Add(titleRow);
        top.Children.Add(new TextBlock
        {
            Text = "One button, several changes, in order. Each press runs the full step list against the current game; Update Output then puts the result on air. Media steps start playing immediately.",
            FontSize = 10,
            Foreground = _muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var pageBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        pageBar.Children.Add(new TextBlock { Text = "PAGE", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = _muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _prevPage.Click += (_, _) => SwitchPage(-1);
        _nextPage.Click += (_, _) => SwitchPage(1);
        _addPage.Click += (_, _) => AddPage();
        pageBar.Children.Add(_prevPage);
        pageBar.Children.Add(_pageLabel);
        pageBar.Children.Add(_nextPage);
        pageBar.Children.Add(_addPage);
        top.Children.Add(pageBar);
        DockPanel.SetDock(top, Dock.Top);
        board.Children.Add(top);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var boardBody = new Grid();
        boardBody.Children.Add(_macroButtons);
        _emptyText.IsVisible = false;
        boardBody.Children.Add(_emptyText);
        scroll.Content = boardBody;
        board.Children.Add(scroll);
        _root.Children.Add(board);

        var splitter = _splitter;
        splitter.Background = _border;
        Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        var editor = _editorHost;
        editor.Background = _panel;
        editor.BorderBrush = _border;
        var editorScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var editorPanel = new StackPanel();
        var editorTitle = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var statusBorder = new Border { Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(8), Background = _surface, BorderBrush = _border, BorderThickness = new Thickness(1), Margin = new Thickness(14, 0, 0, 0), Child = _editStatus };
        DockPanel.SetDock(statusBorder, Dock.Right);
        editorTitle.Children.Add(statusBorder);
        editorTitle.Children.Add(new TextBlock { Text = "EDIT MACRO", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = _muted, VerticalAlignment = VerticalAlignment.Center });
        editorPanel.Children.Add(editorTitle);
        editorPanel.Children.Add(Label("BUTTON LABEL", 0, 0, 0, 3));
        _nameBox.TextChanged += (_, _) =>
        {
            if (_syncing || _draft is null) return;
            _draft.Label = _nameBox.Text ?? "";
            MarkDirty();
        };
        editorPanel.Children.Add(_nameBox);
        editorPanel.Children.Add(Label("BUTTON COLOR", 0, 8, 0, 3));
        var colorRow = new DockPanel();
        DockPanel.SetDock(_colorPreview, Dock.Left);
        _colorPreview.BorderBrush = _border;
        colorRow.Children.Add(_colorPreview);
        _colorBox.TextChanged += (_, _) =>
        {
            if (_syncing || _draft is null) return;
            var text = (_colorBox.Text ?? "").Trim();
            var valid = text.Length == 0;
            var parsedColor = default(Color);
            if (!valid) valid = TryParseColor(text, out parsedColor);
            if (!valid) { _editStatus.Text = "COLOR?"; return; }
            _draft.Color = text.Length == 0 ? "" : NormalizeColor(parsedColor);
            MarkDirty();
            UpdateColorPreview();
            UpdateColorChoiceHighlight();
        };
        colorRow.Children.Add(_colorBox);
        editorPanel.Children.Add(colorRow);
        editorPanel.Children.Add(_colorChoices);
        editorPanel.Children.Add(Hint("Optional colour coding for the macro button. Leave it empty for the theme accent."));
        BuildColorChoices();
        var saveRow = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        _saveButton.Click += (_, _) => SaveDraft(true);
        _deleteButton.Click += (_, _) => DeleteDraft();
        saveRow.Children.Add(_saveButton);
        saveRow.Children.Add(_deleteButton);
        editorPanel.Children.Add(saveRow);

        editorPanel.Children.Add(Label("STEPS · RUN TOP TO BOTTOM"));
        _stepList.Background = _input;
        _stepList.BorderBrush = _border;
        _stepList.Foreground = _text;
        _stepList.ItemTemplate = new FuncDataTemplate<MacroAction>((action, _) => new TextBlock
        {
            Text = action.Summary,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _text
        });
        _stepList.SelectionChanged += (_, _) => LoadSelectedStep();
        editorPanel.Children.Add(_stepList);
        var stepButtons = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        var up = new Button { Content = "↑ Up" };
        var down = new Button { Content = "↓ Down" };
        var remove = new Button { Content = "Remove step" };
        up.Click += (_, _) => MoveStep(-1);
        down.Click += (_, _) => MoveStep(1);
        remove.Click += (_, _) => RemoveStep();
        stepButtons.Children.Add(up);
        stepButtons.Children.Add(down);
        stepButtons.Children.Add(remove);
        editorPanel.Children.Add(stepButtons);

        editorPanel.Children.Add(new Border { Height = 1, Background = _border, Margin = new Thickness(0, 10, 0, 2) });
        editorPanel.Children.Add(Label("ADD A STEP"));
        editorPanel.Children.Add(_kind);
        _kind.SelectionChanged += (_, _) => { if (!_syncing) UpdateFields(); };

        BuildPanels();
        editorPanel.Children.Add(_sidePanel);
        editorPanel.Children.Add(_amountPanel);
        editorPanel.Children.Add(_valuePanel);
        editorPanel.Children.Add(_waitPanel);
        editorPanel.Children.Add(_rosterPanel);
        editorPanel.Children.Add(_mediaPanel);
        editorPanel.Children.Add(_flagPanel);
        editorPanel.Children.Add(_layoutPanel);
        editorPanel.Children.Add(_commandPanel);
        editorPanel.Children.Add(_sportPanel);
        editorPanel.Children.Add(_customPanel);
        editorPanel.Children.Add(_bypass);
        editorPanel.Children.Add(Hint("Unchecked steps stay staged until Update Output; media always publishes at once."));
        var addRow = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        _addStep.Click += (_, _) => AddStep();
        _updateStep.Click += (_, _) => UpdateSelectedStep();
        addRow.Children.Add(_addStep);
        addRow.Children.Add(_updateStep);
        editorPanel.Children.Add(addRow);
        editorPanel.Children.Add(Hint("A macro starts from step 1 every time. Score, down, timeouts, clocks, period, media, roster stats, custom values, console buttons and sport values can be mixed freely; a missing player or source skips only that step."));
        editorScroll.Content = editorPanel;
        editor.Child = editorScroll;
        Grid.SetColumn(editor, 2);
        _root.Children.Add(editor);
        Content = _root;
    }

    void BuildPanels()
    {
        _sidePanel.Children.Add(Label("SIDE"));
        _sidePanel.Children.Add(_side);
        _amountPanel.Children.Add(Label("POINTS TO ADD (CAN BE NEGATIVE)"));
        _amountPanel.Children.Add(_amount);
        _valuePanel.Children.Add(_valueLabel);
        _valuePanel.Children.Add(_value);
        _waitPanel.Children.Add(Label("WAIT BEFORE THE NEXT STEP (SECONDS)"));
        _waitPanel.Children.Add(_wait);
        _waitPanel.Children.Add(Hint("Use a wait to let a replay, graphic or camera settle before the next step."));

        _rosterPanel.Children.Add(Label("PLAYER"));
        _rosterPanel.Children.Add(_player);
        _rosterPanel.Children.Add(Label("STAT"));
        _rosterPanel.Children.Add(_stat);
        _rosterPanel.Children.Add(_delta);

        _mediaPanel.Children.Add(Label("MEDIA ITEM"));
        _mediaPanel.Children.Add(_media);
        _media.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _media.SelectedItem is MediaChoice choice) _path.Text = choice.Path;
        };
        _mediaPanel.Children.Add(Label("OR A FILE / LIVE SOURCE ADDRESS"));
        _mediaPanel.Children.Add(_path);
        _mediaHint.Text = "A clip or camera plays; a microphone is put on air. Already-live sources are left alone.";
        _mediaPanel.Children.Add(_mediaHint);

        _flagPanel.Children.Add(Label("FLAG TYPE"));
        _flagPanel.Children.Add(_flag);
        _flagPanel.Children.Add(_clearFlag);

        _layoutPanel.Children.Add(Label("LAYOUT"));
        _layoutPanel.Children.Add(_layout);
        _layoutPanel.Children.Add(Hint("The chosen layout is put on air as soon as this step runs."));

        _commandPanel.Children.Add(Label("CONSOLE BUTTON"));
        _commandPanel.Children.Add(_command);
        _commandPanel.Children.Add(Hint("Runs the same action as that button on the console. Dialog-opening actions are left out."));

        _sportPanel.Children.Add(Label("SPORT VALUE"));
        _sportPanel.Children.Add(_sportValue);
        _sportPanel.Children.Add(_sportDelta);

        _customPanel.Children.Add(Label("CUSTOM VALUE"));
        _customPanel.Children.Add(_customValue);
        _customPanel.Children.Add(_customDelta);
    }

    void FillKindChoices()
    {
        _kind.ItemsSource = new[]
        {
            new KindChoice(MacroActionKind.Score, "Score — add points to a side"),
            new KindChoice(MacroActionKind.SetDown, "Down — set the current down"),
            new KindChoice(MacroActionKind.SetTimeouts, "Timeouts — set a side's remaining count"),
            new KindChoice(MacroActionKind.SetGameClock, "Game clock — set the clock"),
            new KindChoice(MacroActionKind.SetPlayClock, "Play clock — set the clock"),
            new KindChoice(MacroActionKind.SetTimeoutClock, "Timeout clock — set the clock"),
            new KindChoice(MacroActionKind.SetPeriod, "Period — set the current period"),
            new KindChoice(MacroActionKind.PutOnAir, "Media — take a clip, mic or camera on air"),
            new KindChoice(MacroActionKind.TakeOffAir, "Media — take a camera, mic or clip off air"),
            new KindChoice(MacroActionKind.SwitchLayout, "Layout — switch a display to a layout"),
            new KindChoice(MacroActionKind.PenaltyFlag, "Flag — put a flag down or clear it"),
            new KindChoice(MacroActionKind.RosterStat, "Roster stat — change any player's stat"),
            new KindChoice(MacroActionKind.Control, "Console button — run any control action"),
            new KindChoice(MacroActionKind.SportValue, "Sport value — set any counter or flag"),
            new KindChoice(MacroActionKind.SetFootballDistance, "Football — set distance to go"),
            new KindChoice(MacroActionKind.SetFootballBallSpot, "Football — set ball spot"),
            new KindChoice(MacroActionKind.SetPossession, "Possession — set the team with the ball"),
            new KindChoice(MacroActionKind.CustomValue, "Custom value — set a value on the console"),
            new KindChoice(MacroActionKind.Wait, "Wait — pause before the next step")
        };
        _kind.SelectedIndex = 0;
    }

    void BuildColorChoices()
    {
        AddColorChoice("", "Theme accent", 54);
        foreach (var color in MacroColors) AddColorChoice(color, color, 26);
    }

    void AddColorChoice(string value, string tip, double width)
    {
        var swatch = new Button
        {
            Width = width,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 4),
            Tag = value,
            ToolTip = tip,
            Background = value.Length == 0 ? _surface : Brush(value),
            BorderBrush = _border,
            BorderThickness = new Thickness(1)
        };
        if (value.Length == 0)
        {
            swatch.Content = "THEME";
            swatch.FontSize = 9;
        }
        swatch.Click += (_, _) => SetMacroColor(value);
        _colorChoices.Children.Add(swatch);
    }

    void ToggleEditor()
    {
        _editorVisible = !_editorVisible;
        _editorToggle.Content = _editorVisible ? "Hide editor" : "Show editor";
        ApplyEditorLayout();
        RenderMacros();
    }

    /// <summary>
    /// Fits the desk to its width: wide keeps the board and the editor side by side with the
    /// splitter between them, narrow stacks the editor under the board so neither is squeezed.
    /// </summary>
    void ApplyWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var stacked = width < StackedBelowWidth;
        if (stacked == _stacked) return;
        _stacked = stacked;
        if (stacked)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(_board, 0);
            Grid.SetColumnSpan(_board, 3);
            Grid.SetRow(_board, 0);
            Grid.SetColumn(_editorHost, 0);
            Grid.SetColumnSpan(_editorHost, 3);
            Grid.SetRow(_editorHost, 1);
            _editorHost.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            _root.RowDefinitions.Clear();
            Grid.SetColumn(_board, 0);
            Grid.SetColumnSpan(_board, 1);
            Grid.SetRow(_board, 0);
            Grid.SetColumn(_editorHost, 2);
            Grid.SetColumnSpan(_editorHost, 1);
            Grid.SetRow(_editorHost, 0);
            _editorHost.Margin = default;
        }
        ApplyEditorLayout();
        RenderMacros();
    }

    /// <summary>Puts the editor where the current mode wants it, and honours the editor toggle.</summary>
    void ApplyEditorLayout()
    {
        if (_stacked)
        {
            _splitterColumn.Width = new GridLength(0);
            _editorColumn.Width = new GridLength(0);
            _splitter.IsVisible = false;
            _root.RowDefinitions[1].Height = _editorVisible
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            _editorHost.IsVisible = _editorVisible;
            return;
        }

        _splitterColumn.Width = new GridLength(_editorVisible ? 8 : 0);
        _editorColumn.Width = new GridLength(_editorVisible ? EditorWidth : 0);
        _splitter.IsVisible = _editorVisible;
        _editorHost.IsVisible = _editorVisible;
    }

    IEnumerable<SavedMacro> MacrosOnPage => _macros.Where(macro => macro.Page == _page);

    void StoreBoard() => _engine.StoreMacros(_macros, _pageCount);

    void RenderMacros()
    {
        _macroButtons.Children.Clear();
        _macroButtons.RowDefinitions.Clear();
        _macroButtons.ColumnDefinitions.Clear();
        var page = MacrosOnPage.ToList();
        _emptyText.IsVisible = page.Count == 0;
        _emptyText.Text = _pageCount > 1
            ? $"Page {_page + 1} is empty. Press + New macro to put a button here, or use ◀ / ▶ to change pages."
            : "No macros yet. Choose + New macro, add a few steps, then press the macro card to run them together.";
        var columns = _editorVisible ? 5 : 9;
        for (var column = 0; column < columns; column++)
            _macroButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var placements = LayoutMacros(columns);
        var rows = placements.Count == 0 ? 0 : placements.Max(placement => placement.Row) + 1;
        for (var row = 0; row < rows; row++)
            _macroButtons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var placement in placements)
        {
            var card = BuildMacroCard(placement.Macro);
            Grid.SetRow(card, placement.Row);
            Grid.SetColumn(card, placement.Column);
            _macroButtons.Children.Add(card);
        }
        UpdatePageBar();
    }

    List<MacroPlacement> LayoutMacros(int columns)
    {
        columns = Math.Max(1, columns);
        var placements = new List<MacroPlacement>();
        var occupied = new HashSet<(int Row, int Column)>();
        var explicitCells = _macros.Any(macro => macro.Row >= 0 && macro.Column >= 0);
        var pending = new List<SavedMacro>();
        foreach (var macro in MacrosOnPage)
        {
            if (macro.Row >= 0 && macro.Column >= 0)
            {
                var row = macro.Row;
                var column = Math.Clamp(macro.Column, 0, columns - 1);
                if (occupied.Add((row, column)))
                {
                    placements.Add(new MacroPlacement(macro, row, column));
                    continue;
                }
            }
            pending.Add(macro);
        }
        var nextRow = 0;
        var nextColumn = 0;
        void Advance()
        {
            nextColumn++;
            if (nextColumn < columns) return;
            nextColumn = 0;
            nextRow++;
        }
        foreach (var macro in pending)
        {
            if (!explicitCells && macro.StartNewRow && occupied.Any(cell => cell.Row == nextRow))
            {
                nextRow++;
                nextColumn = 0;
            }
            while (occupied.Contains((nextRow, nextColumn))) Advance();
            occupied.Add((nextRow, nextColumn));
            placements.Add(new MacroPlacement(macro, nextRow, nextColumn));
            Advance();
        }
        return placements.OrderBy(placement => placement.Row).ThenBy(placement => placement.Column).ToList();
    }

    Border BuildMacroCard(SavedMacro macro)
    {
        var custom = ParseColor(macro.Color);
        var card = new Border
        {
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8),
            Background = _surface,
            BorderBrush = custom is Color cardColor ? new SolidColorBrush(cardColor) : _border,
            BorderThickness = new Thickness(custom is null ? 1 : 2),
            CornerRadius = new CornerRadius(9),
            Tag = macro,
            MinHeight = 74
        };
        var stack = new StackPanel();
        var header = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(4, 3, 4, 5),
            MinHeight = _showHints ? 20 : 8,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Tag = macro,
            ToolTip = "Click to edit · right-click to edit or delete"
        };
        if (_showHints)
            header.Child = new TextBlock { Text = "⠿  CLICK TO EDIT", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = _muted };
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                EditMacroCard(macro);
                e.Handled = true;
            }
        };
        stack.Children.Add(header);
        var run = new Button
        {
            Content = new TextBlock { Text = macro.Label, TextWrapping = TextWrapping.Wrap, Foreground = custom is Color fill ? new SolidColorBrush(Readable(fill)) : _text },
            Tag = macro,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 34,
            Background = custom is Color color ? new SolidColorBrush(color) : _accentSoft,
            BorderBrush = custom is Color edge ? new SolidColorBrush(UiPalette.Mix(edge, Colors.Black, 0.25)) : _accent,
            BorderThickness = new Thickness(1),
            ToolTip = string.Join(Environment.NewLine, macro.Actions.Select(action => action.Summary))
        };
        run.Click += async (_, _) => await RunMacro(macro);
        stack.Children.Add(run);
        card.Child = stack;
        AttachMacroMenu(card, macro);
        return card;
    }

    void AttachMacroMenu(Control element, SavedMacro macro)
    {
        var menu = new ContextMenu { Background = _surface, BorderBrush = _border };
        var edit = new MenuItem { Header = "Edit macro", Foreground = _text };
        edit.Click += (_, _) => EditMacroCard(macro);
        var delete = new MenuItem { Header = "Delete macro", Foreground = _text };
        delete.Click += (_, _) => DeleteMacroCard(macro);
        menu.Items.Add(edit);
        menu.Items.Add(delete);
        element.ContextMenu = menu;
    }

    async Task RunMacro(SavedMacro macro)
    {
        if (_draft is not null && _draft.Id == macro.Id && _dirty) SaveDraft(false);
        var current = _macros.FirstOrDefault(item => item.Id == macro.Id);
        if (current is null) return;
        _editStatus.Text = "RUNNING";
        var message = await _runner.RunAsync(current);
        _editStatus.Text = "RAN";
        Status?.Invoke(message);
    }

    void EditMacroCard(SavedMacro macro)
    {
        if (_draft?.Id == macro.Id) return;
        SaveDraft(false);
        _draft = macro.Copy();
        _dirty = false;
        LoadEditor();
    }

    void DeleteMacroCard(SavedMacro macro)
    {
        if (_draft?.Id == macro.Id) { _draft = null; _dirty = false; }
        _macros.RemoveAll(item => item.Id == macro.Id);
        StoreBoard();
        if (_draft is null) _draft = MacrosOnPage.FirstOrDefault()?.Copy();
        RenderMacros();
        LoadEditor();
        Status?.Invoke($"MACRO · {macro.Label} deleted");
    }

    void NewMacro()
    {
        SaveDraft(false);
        var macro = new SavedMacro { Label = $"Macro {MacrosOnPage.Count() + 1}", Page = _page };
        _macros.Add(macro);
        StoreBoard();
        _draft = macro.Copy();
        _dirty = false;
        RenderMacros();
        LoadEditor();
    }

    void DeleteDraft()
    {
        if (_draft is null) return;
        var label = _draft.Label;
        _macros.RemoveAll(item => item.Id == _draft.Id);
        StoreBoard();
        _draft = MacrosOnPage.FirstOrDefault()?.Copy();
        _dirty = false;
        RenderMacros();
        LoadEditor();
        Status?.Invoke($"MACRO · {label} deleted");
    }

    void SaveDraft(bool announce)
    {
        if (_draft is null) return;
        _draft.Label = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Macro" : _nameBox.Text.Trim();
        var existing = _macros.FirstOrDefault(item => item.Id == _draft.Id);
        if (existing is null)
        {
            _draft.Page = _page;
            _macros.Add(_draft.Copy());
        }
        else
        {
            _draft.StartNewRow = existing.StartNewRow;
            _draft.Row = existing.Row;
            _draft.Column = existing.Column;
            _draft.Page = existing.Page;
            var index = _macros.IndexOf(existing);
            _macros[index] = _draft.Copy();
        }
        StoreBoard();
        _dirty = false;
        _syncing = true;
        try
        {
            if (_nameBox.Text != _draft.Label) _nameBox.Text = _draft.Label;
            if (_colorBox.Text != _draft.Color) _colorBox.Text = _draft.Color;
        }
        finally { _syncing = false; }
        RenderMacros();
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
        _editStatus.Text = "SAVED";
        if (announce) Status?.Invoke($"MACRO · {_draft.Label} saved");
    }

    void SwitchPage(int delta)
    {
        var target = Math.Clamp(_page + delta, 0, _pageCount - 1);
        if (target == _page) return;
        SaveDraft(false);
        _page = target;
        _draft = MacrosOnPage.FirstOrDefault()?.Copy();
        _dirty = false;
        RenderMacros();
        LoadEditor();
        Status?.Invoke($"MACRO · Page {_page + 1} of {_pageCount}");
    }

    void AddPage()
    {
        if (_pageCount >= 99) return;
        SaveDraft(false);
        _pageCount++;
        _page = _pageCount - 1;
        _draft = null;
        _dirty = false;
        StoreBoard();
        RenderMacros();
        LoadEditor();
        Status?.Invoke($"MACRO · Added blank page {_page + 1}");
    }

    void UpdatePageBar()
    {
        _pageLabel.Text = $"PAGE {_page + 1} / {_pageCount}";
        _prevPage.IsEnabled = _page > 0;
        _nextPage.IsEnabled = _page < _pageCount - 1;
        _addPage.IsEnabled = _pageCount < 99;
    }

    void LoadEditor()
    {
        _syncing = true;
        try
        {
            _nameBox.Text = _draft?.Label ?? "";
            _colorBox.Text = _draft?.Color ?? "";
            _stepList.ItemsSource = _draft?.Actions;
            _saveButton.IsEnabled = _deleteButton.IsEnabled = _draft is not null;
            _editStatus.Text = _draft is null ? "NEW" : _dirty ? "UNSAVED" : "SAVED";
            if (_draft is not null && _draft.Actions.Count > 0) _stepList.SelectedIndex = 0;
            UpdateFields();
            UpdateColorPreview();
            UpdateColorChoiceHighlight();
            UpdatePageBar();
        }
        finally { _syncing = false; }
    }

    MacroActionKind SelectedKind => _kind.SelectedItem is KindChoice choice
        ? choice.Kind
        : (_stepList.SelectedItem as MacroAction)?.Kind ?? MacroActionKind.Score;

    void UpdateFields()
    {
        var kind = SelectedKind;
        _sidePanel.IsVisible = kind is MacroActionKind.Score or MacroActionKind.SetTimeouts
            or MacroActionKind.RosterStat or MacroActionKind.SetPossession or MacroActionKind.PenaltyFlag;
        _amountPanel.IsVisible = kind == MacroActionKind.Score;
        _valuePanel.IsVisible = kind is MacroActionKind.SetDown or MacroActionKind.SetTimeouts
            or MacroActionKind.SetGameClock or MacroActionKind.SetPlayClock
            or MacroActionKind.SetTimeoutClock or MacroActionKind.SetPeriod
            or MacroActionKind.RosterStat or MacroActionKind.SportValue or MacroActionKind.CustomValue
            or MacroActionKind.SetFootballDistance or MacroActionKind.SetFootballBallSpot;
        _waitPanel.IsVisible = kind == MacroActionKind.Wait;
        _rosterPanel.IsVisible = kind == MacroActionKind.RosterStat;
        var mediaStep = kind is MacroActionKind.PlayVideo or MacroActionKind.PutOnAir or MacroActionKind.TakeOffAir;
        _mediaPanel.IsVisible = mediaStep;
        _flagPanel.IsVisible = kind == MacroActionKind.PenaltyFlag;
        _layoutPanel.IsVisible = kind == MacroActionKind.SwitchLayout;
        _commandPanel.IsVisible = kind == MacroActionKind.Control;
        _sportPanel.IsVisible = kind == MacroActionKind.SportValue;
        _customPanel.IsVisible = kind == MacroActionKind.CustomValue;
        _valueLabel.Text = kind switch
        {
            MacroActionKind.SetDown => "DOWN (1–4)",
            MacroActionKind.SetTimeouts => "TIMEOUTS REMAINING",
            MacroActionKind.SetGameClock => "GAME CLOCK (MM:SS)",
            MacroActionKind.SetPlayClock => "SECONDS",
            MacroActionKind.SetTimeoutClock => "SECONDS",
            MacroActionKind.SetPeriod => "PERIOD",
            MacroActionKind.RosterStat => "VALUE (OR AMOUNT TO ADD)",
            MacroActionKind.SportValue => "VALUE (OR AMOUNT TO ADD)",
            MacroActionKind.SetFootballDistance => "YARDS TO GO (1–99)",
            MacroActionKind.SetFootballBallSpot => "BALL SPOT (0–50)",
            MacroActionKind.CustomValue => "VALUE (OR AMOUNT TO ADD)",
            _ => "VALUE"
        };
        if (kind == MacroActionKind.RosterStat) RefreshRoster();
        if (kind == MacroActionKind.Control) RefreshCommands();
        if (kind == MacroActionKind.SportValue) RefreshSportValues();
        if (kind == MacroActionKind.CustomValue) RefreshCustomValues();
        if (kind == MacroActionKind.PenaltyFlag) RefreshFlags();
        if (kind == MacroActionKind.SwitchLayout) RefreshLayouts();
        if (mediaStep) RefreshMedia();
        var direct = mediaStep || kind == MacroActionKind.SwitchLayout;
        if (direct) _bypass.IsChecked = true;
        if (kind == MacroActionKind.Wait) _bypass.IsChecked = false;
        _bypass.IsEnabled = !direct && kind != MacroActionKind.Wait;
    }

    void RefreshMedia()
    {
        var selected = (_media.SelectedItem as MediaChoice)?.Path ?? _path.Text?.Trim();
        var choices = _session.Library
            .Select(entry => new MediaChoice(entry.Path, $"{entry.KindLabel} · {entry.Label} · {entry.Tag}"))
            .ToList();
        _media.ItemsSource = choices;
        _media.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Path, selected, StringComparison.OrdinalIgnoreCase));
    }

    void RefreshFlags()
    {
        var selected = _flag.SelectedItem as string;
        var types = GameEngine.PenaltyTypesFor(_engine.State.SportId);
        _flag.ItemsSource = types;
        _flag.SelectedItem = types.Contains(selected) ? selected : types.FirstOrDefault();
    }

    void RefreshLayouts()
    {
        var selectedDisplay = (_layout.SelectedItem as LayoutChoice)?.DisplayId;
        var selectedLayout = (_layout.SelectedItem as LayoutChoice)?.LayoutId;
        var choices = _catalog?.Displays
            .SelectMany(display => display.Layouts.Select(layout =>
                new LayoutChoice(display.Id, layout.Id, $"{display.Name} · {layout.Name}")))
            .ToList() ?? [];
        _layout.ItemsSource = choices;
        _layout.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.DisplayId, selectedDisplay, StringComparison.OrdinalIgnoreCase)
            && string.Equals(choice.LayoutId, selectedLayout, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
    }

    void RefreshRoster()
    {
        var home = _side.SelectedIndex != 1;
        var roster = RosterStore.Book.Find(home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId);
        var previous = (_player.SelectedItem as PlayerChoice)?.Id;
        var choices = (roster?.Players ?? []).Select(player => new PlayerChoice(player.Id, player.Record)).ToList();
        _player.ItemsSource = choices;
        if (previous is not null) _player.SelectedItem = choices.FirstOrDefault(choice => choice.Id == previous);
        if (_player.SelectedItem is null && choices.Count > 0) _player.SelectedIndex = 0;
        _player.IsEnabled = choices.Count > 0;
        _stat.IsEnabled = roster is not null;
    }

    void RefreshCommands()
    {
        var selected = (_command.SelectedItem as CommandChoice)?.Id;
        var choices = ControlActions.ForSport(_engine.State.SportId)
            .Where(action => !action.Id.EndsWith(".set", StringComparison.OrdinalIgnoreCase)
                && !action.Id.StartsWith("camera.", StringComparison.OrdinalIgnoreCase)
                && !action.Id.StartsWith("replay.", StringComparison.OrdinalIgnoreCase))
            .Select(action => new CommandChoice(action.Id, $"{action.Group} · {action.Label}"))
            .ToList();
        _command.ItemsSource = choices;
        _command.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
    }

    void RefreshSportValues()
    {
        var selected = (_sportValue.SelectedItem as SportValueChoice)?.Id;
        var choices = _engine.State.Controls.Select(control => new SportValueChoice(control.Id, control.Label)).ToList();
        _sportValue.ItemsSource = choices;
        _sportValue.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
    }

    void RefreshCustomValues()
    {
        var selected = (_customValue.SelectedItem as CustomValueChoice)?.Id;
        var choices = _engine.State.Stats.Select(stat => new CustomValueChoice(stat.Id, stat.Name)).ToList();
        _customValue.ItemsSource = choices;
        _customValue.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
    }

    void LoadSelectedStep()
    {
        if (_syncing || _stepList.SelectedItem is not MacroAction action) return;
        _syncing = true;
        try
        {
            _kind.SelectedItem = _kind.Items.OfType<KindChoice>().FirstOrDefault(choice => choice.Kind == action.Kind);
            _side.SelectedIndex = action.Home ? 0 : 1;
            _amount.Text = action.Amount.ToString(CultureInfo.InvariantCulture);
            _value.Text = action.Kind == MacroActionKind.SetGameClock ? ClockText(action.Value) : action.Value.ToString(CultureInfo.InvariantCulture);
            _wait.Text = Math.Clamp(action.WaitSeconds, 0, 60).ToString("0.##", CultureInfo.InvariantCulture);
            _bypass.IsChecked = action.BypassOutput || action.Kind is MacroActionKind.PlayVideo or MacroActionKind.PutOnAir
                or MacroActionKind.TakeOffAir or MacroActionKind.SwitchLayout;
            _path.Text = action.Path;
            _delta.IsChecked = action.Delta;
            _sportDelta.IsChecked = action.Delta;
            _customDelta.IsChecked = action.Delta;
            _clearFlag.IsChecked = action.Value == 0;
            UpdateFields();
            switch (action.Kind)
            {
                case MacroActionKind.Control:
                    _command.SelectedItem = _command.Items.OfType<CommandChoice>().FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
                    break;
                case MacroActionKind.SportValue:
                    _sportValue.SelectedItem = _sportValue.Items.OfType<SportValueChoice>().FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
                    break;
                case MacroActionKind.CustomValue:
                    _customValue.SelectedItem = _customValue.Items.OfType<CustomValueChoice>().FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
                    break;
                case MacroActionKind.RosterStat:
                    RefreshRoster();
                    _player.SelectedItem = _player.Items.OfType<PlayerChoice>().FirstOrDefault(choice => choice.Id == action.PlayerId);
                    _stat.Text = action.StatName;
                    break;
                case MacroActionKind.PenaltyFlag:
                    RefreshFlags();
                    _flag.SelectedItem = action.Path;
                    break;
                case MacroActionKind.SwitchLayout:
                    RefreshLayouts();
                    _layout.SelectedItem = _layout.Items.OfType<LayoutChoice>().FirstOrDefault(choice =>
                        string.Equals(choice.DisplayId, action.DisplayId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(choice.LayoutId, action.Path, StringComparison.OrdinalIgnoreCase));
                    break;
                case MacroActionKind.PlayVideo:
                case MacroActionKind.PutOnAir:
                case MacroActionKind.TakeOffAir:
                    RefreshMedia();
                    _media.SelectedItem = _media.Items.OfType<MediaChoice>().FirstOrDefault(choice => string.Equals(choice.Path, action.Path, StringComparison.OrdinalIgnoreCase));
                    break;
            }
        }
        finally { _syncing = false; }
    }

    MacroAction ReadActionFromFields()
    {
        var kind = SelectedKind;
        var value = int.TryParse(_value.Text, out var parsed) ? parsed : 0;
        if (kind == MacroActionKind.SetGameClock && ClockFormat.TryParse(_value.Text, out var seconds)) value = seconds;
        if (kind == MacroActionKind.PenaltyFlag) value = _clearFlag.IsChecked == true ? 0 : 1;
        var media = _media.SelectedItem as MediaChoice;
        var command = _command.SelectedItem as CommandChoice;
        var sport = _sportValue.SelectedItem as SportValueChoice;
        var custom = _customValue.SelectedItem as CustomValueChoice;
        var layout = _layout.SelectedItem as LayoutChoice;
        var flag = _flag.SelectedItem as string ?? "";
        var path = kind switch
        {
            MacroActionKind.PlayVideo or MacroActionKind.PutOnAir or MacroActionKind.TakeOffAir => _path.Text?.Trim() ?? "",
            MacroActionKind.SwitchLayout => layout?.LayoutId ?? "",
            MacroActionKind.PenaltyFlag => flag,
            MacroActionKind.Control => command?.Id ?? "",
            MacroActionKind.SportValue => sport?.Id ?? "",
            MacroActionKind.CustomValue => custom?.Id ?? "",
            _ => ""
        };
        var label = kind switch
        {
            MacroActionKind.Control => command?.Label ?? "",
            MacroActionKind.SportValue => sport?.Label ?? "",
            MacroActionKind.CustomValue => custom?.Label ?? "",
            MacroActionKind.SwitchLayout => layout?.Label ?? "",
            MacroActionKind.PenaltyFlag => flag,
            MacroActionKind.PlayVideo or MacroActionKind.PutOnAir or MacroActionKind.TakeOffAir when media is not null
                && string.Equals(media.Path, _path.Text?.Trim(), StringComparison.OrdinalIgnoreCase) => media.Label,
            _ => ""
        };
        return new MacroAction
        {
            Kind = kind,
            Home = _side.SelectedIndex != 1,
            Amount = int.TryParse(_amount.Text, out var amount) ? amount : 1,
            Value = value,
            Path = path,
            Label = label,
            DisplayId = kind == MacroActionKind.SwitchLayout ? layout?.DisplayId ?? "" : "",
            PlayerId = (_player.SelectedItem as PlayerChoice)?.Id ?? "",
            PlayerName = (_player.SelectedItem as PlayerChoice)?.Label ?? "",
            StatName = _stat.Text?.Trim() ?? "",
            BypassOutput = kind is MacroActionKind.PlayVideo or MacroActionKind.PutOnAir or MacroActionKind.TakeOffAir
                or MacroActionKind.SwitchLayout || _bypass.IsChecked == true,
            WaitSeconds = double.TryParse(_wait.Text, out var wait) ? wait : 1,
            Delta = kind == MacroActionKind.SportValue ? _sportDelta.IsChecked == true
                : kind == MacroActionKind.CustomValue ? _customDelta.IsChecked == true
                : _delta.IsChecked == true
        };
    }

    void AddStep()
    {
        if (_draft is null) return;
        _draft.Actions.Add(ReadActionFromFields());
        _dirty = true;
        ReloadSteps();
        _stepList.SelectedIndex = _draft.Actions.Count - 1;
        MarkDirty();
    }

    void UpdateSelectedStep()
    {
        if (_draft is null || _stepList.SelectedItem is not MacroAction action) return;
        var replacement = ReadActionFromFields();
        action.Kind = replacement.Kind;
        action.Home = replacement.Home;
        action.Amount = replacement.Amount;
        action.Value = replacement.Value;
        action.Path = replacement.Path;
        action.DisplayId = replacement.DisplayId;
        action.Label = replacement.Label;
        action.PlayerId = replacement.PlayerId;
        action.PlayerName = replacement.PlayerName;
        action.StatName = replacement.StatName;
        action.Delta = replacement.Delta;
        action.BypassOutput = replacement.BypassOutput;
        action.WaitSeconds = replacement.WaitSeconds;
        _dirty = true;
        ReloadSteps();
        MarkDirty();
    }

    void MoveStep(int delta)
    {
        if (_draft is null || _stepList.SelectedItem is not MacroAction action) return;
        var index = _draft.Actions.IndexOf(action);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _draft.Actions.Count) return;
        _draft.Actions.RemoveAt(index);
        _draft.Actions.Insert(target, action);
        _dirty = true;
        ReloadSteps();
        _stepList.SelectedIndex = target;
        MarkDirty();
    }

    void RemoveStep()
    {
        if (_draft is null || _stepList.SelectedItem is not MacroAction action) return;
        var index = _draft.Actions.IndexOf(action);
        if (index < 0) return;
        _draft.Actions.RemoveAt(index);
        _dirty = true;
        ReloadSteps();
        if (_draft.Actions.Count > 0) _stepList.SelectedIndex = Math.Min(index, _draft.Actions.Count - 1);
        MarkDirty();
    }

    void ReloadSteps()
    {
        _stepList.ItemsSource = null;
        _stepList.ItemsSource = _draft?.Actions;
    }

    void MarkDirty()
    {
        _dirty = true;
        _editStatus.Text = "UNSAVED";
    }

    void SetMacroColor(string value)
    {
        if (_draft is null) return;
        _draft.Color = value.Length == 0 ? "" : ParseColor(value) is Color color ? NormalizeColor(color) : _draft.Color;
        _dirty = true;
        _syncing = true;
        try { _colorBox.Text = _draft.Color; }
        finally { _syncing = false; }
        MarkDirty();
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void UpdateColorPreview()
    {
        var custom = _draft is not null ? ParseColor(_draft.Color) : null;
        _colorPreview.Background = custom is Color color ? new SolidColorBrush(color) : _accentSoft;
        _colorPreview.BorderBrush = _border;
        _colorPreview.ToolTip = custom is not null ? _draft!.Color : "Theme accent";
    }

    void UpdateColorChoiceHighlight()
    {
        var selected = _draft?.Color ?? "";
        foreach (var swatch in _colorChoices.Children.OfType<Button>())
        {
            var isSelected = string.Equals(swatch.Tag as string ?? "", selected, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(isSelected ? 3 : 1);
            swatch.BorderBrush = isSelected ? _text : _border;
        }
    }

    static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text) || text[0] != '#') return false;
        try
        {
            var parsed = Color.Parse(text);
            color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
            return true;
        }
        catch { return false; }
    }

    static Color? ParseColor(string? text) => TryParseColor(text, out var color) ? color : null;

    static string NormalizeColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    static Color Readable(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.6 ? Color.FromRgb(24, 32, 43) : Colors.White;
    }

    static string ClockText(int seconds) => GameEngine.FormatClock(Math.Clamp(seconds, 0, 9999));

    static bool TipsEnabled()
    {
        try { return PreferencesStore.Load().ShowHeadingHints; } catch { return false; }
    }

    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    static TextBlock Label(string text, double left = 0, double top = 7, double right = 0, double bottom = 3) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(left, top, right, bottom)
    };

    static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
    };
}
