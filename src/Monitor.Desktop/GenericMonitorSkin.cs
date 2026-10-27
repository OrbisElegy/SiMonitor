// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed partial class GenericMonitorSkin : IMonitorSkin, IMonitorDevicePanel
{
    private readonly DesktopLocalization _localization;
    private readonly Action<MonitorTherapyPreferences>? _save;
    private readonly Action<MonitorTherapyPreferences, bool>? _pacing;
    private (bool Running, bool Pending, bool Allowed, bool AedEnabled, bool TimelineAllowed)? _shownPacing;
    private string? _pacingError;
    private Grid? _body;
    private Control? _monitorArea;
    private readonly MonitorSkinPackage _package;
    internal Control? Screen { get; private set; }
    internal Grid? TherapyArea { get; private set; }
    internal Button LayoutButton { get; private set; } = null!;
    internal Button EcgButton { get; private set; } = null!;
    internal Button PrintButton { get; private set; } = null!;
    internal event Action? PrintRequested;
    internal Button MenuButton { get; private set; } = null!;
    internal event Action? MenuRequested;
    internal Button LeadButton { get; private set; } = null!;
    internal IReadOnlyList<RadioButton> LeadChoices => _leadChoices;
    private readonly List<RadioButton> _leadChoices = [];
    private EcgLead _displayLead = EcgLead.II;

    internal void ConfigureLeadSelection(EcgLead selected, Action<EcgLead> changed)
    {
        _displayLead = selected;
        string group = "monitor-lead-" + Guid.NewGuid().ToString("N");
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            string name = ProjectedEcgDemoSource.LeadNames[(int)lead];
            var choice = new RadioButton { Content = name, GroupName = group, MinHeight = 44, MinWidth = 64, IsChecked = lead == selected };
            _localization.Bind(choice, AutomationProperties.NameProperty, "skin.ecgLeadChoice", name);
            choice.IsCheckedChanged += (_, _) =>
            {
                if (choice.IsChecked != true) { return; }
                changed(lead);
                _displayLead = lead;
                RefreshLeadCaption();
            };
            // Keep the radio group attached until its checked-state update has finished.
            choice.Click += (_, _) => LeadButton.Flyout?.Hide();
            _leadChoices.Add(choice);
        }
    }

    private void RefreshLeadCaption()
    {
        string name = ProjectedEcgDemoSource.LeadNames[(int)_displayLead];
        _localization.Bind((TextBlock)LeadButton.Content!, TextBlock.TextProperty, "skin.leadsSelected", name);
        _localization.Bind(LeadButton, AutomationProperties.NameProperty, "skin.leadsSelected", name);
    }
    internal bool TwelveLeadVisible { get; private set; }
    internal event Action? EcgViewChanged;
    private Control? _waveforms, _twelveLeadWaveforms;

    internal void SetTwelveLeadVisible(bool visible)
    {
        if (_package.Defibrillator) { ClearWaveformFreeze(); UpdateSoftKeys(); }
        bool changed = TwelveLeadVisible != visible;
        TwelveLeadVisible = visible;
        if (_waveforms is null || _twelveLeadWaveforms is null) { return; }
        _waveforms.IsVisible = !visible;
        _twelveLeadWaveforms.IsVisible = visible;
        _twelveLeadWaveforms.InvalidateVisual();
        RefreshLayout();
        EcgButton.Background = _package.Bedside ? BedsideToolbar.KeyBackground(visible) : Brush.Parse(visible ? "#145AA3" : "#222C38");
        _localization.Bind(EcgButton, AutomationProperties.NameProperty, visible ? "skin.ecgSelected" : "skin.ecg");
        _localization.Bind(EcgButton, ToolTip.TipProperty, visible ? "skin.returnMonitor" : "skin.ecg");
        if (changed) { EcgViewChanged?.Invoke(); }
    }
    internal ComboBox LayoutRows { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ToggleSwitch ShowTherapy { get; } = new() { MinHeight = 44, IsChecked = false };
    private Action<MonitorSkin?, bool>? _layoutChanged;

    internal void ConfigureLayout(MonitorSkin layout, bool therapyVisible, Action<MonitorSkin?, bool> changed)
    {
        _localization.SetChoices(LayoutRows, "skin.threeRows", "skin.fourRows");
        _localization.Bind(LayoutRows, AutomationProperties.NameProperty, "skin.waveformRows");
        _localization.Bind(LayoutRows, ComboBox.PlaceholderTextProperty, "skin.waveformRows");
        _localization.Bind(ShowTherapy, ContentControl.ContentProperty, "skin.showTherapy");
        _localization.Bind(ShowTherapy, AutomationProperties.NameProperty, "skin.showTherapy");
        _localization.Bind(ShowTherapy, ToggleSwitch.OnContentProperty, "skin.visible");
        _localization.Bind(ShowTherapy, ToggleSwitch.OffContentProperty, "skin.hidden");
        LayoutRows.SelectedIndex = layout == MonitorSkin.ThreeRows ? 0 : layout == MonitorSkin.FourRows ? 1 : -1;
        if (_package.FixedWaveformRows is { } fixedRows)
        {
            LayoutRows.SelectedIndex = fixedRows == 3 ? 0 : 1;
            LayoutRows.IsEnabled = false;
        }
        ShowTherapy.IsEnabled = !_package.Defibrillator;
        ShowTherapy.IsChecked = therapyVisible;
        _layoutChanged = changed;
        LayoutRows.SelectionChanged += (_, _) => ChangeLayout();
        ShowTherapy.IsCheckedChanged += (_, _) => ChangeLayout();
    }

    private void ChangeLayout()
    {
        UpdateTherapyVisibility();
        _layoutChanged?.Invoke(LayoutRows.SelectedIndex switch
        {
            0 => MonitorSkin.ThreeRows,
            1 => MonitorSkin.FourRows,
            _ => null
        }, ShowTherapy.IsChecked == true);
    }

    private void UpdateTherapyVisibility()
    {
        if (_package.Defibrillator) { return; }
        if (_body is null || _monitorArea is null || TherapyArea is null) { return; }
        bool visible = ShowTherapy.IsChecked == true;
        TherapyArea.IsVisible = visible;
        _body.ColumnDefinitions[1].Width = new GridLength(visible ? 190 : 0);
        _monitorArea.Margin = new Thickness(0, 0, visible ? 8 : 0, 0);
    }
    internal TextBlock PacingStatus { get; } = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    internal DefibrillatorControls Defibrillator { get; }
    internal ComboBox Energy => Defibrillator.Energy;
    internal NumericUpDown Rate { get; } = Number(70, 30, 210, 5);
    internal NumericUpDown Current { get; } = Number(0, 0, 200, 5);
    internal ComboBox PacingType { get; } = new() { MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button ApplyPacing { get; } = new()
    {
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = Brush.Parse("#125345"),
        Foreground = Brushes.White
    };
    internal Button StopPacing { get; } = new()
    {
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = Brush.Parse("#222C38"),
        Foreground = Brushes.White
    };

    internal GenericMonitorSkin(DesktopLocalization localization, MonitorTherapyPreferences? preferences = null,
        Action<MonitorTherapyPreferences>? save = null, Action<MonitorTherapyPreferences, bool>? pacing = null,
        DefibrillatorConfiguration? defibrillator = null, Action<DefibrillatorCommand>? defibrillatorCommand = null,
        Action<int>? energyChanged = null, MonitorSkinPackage? package = null)
    {
        _package = package ?? MonitorSkinPackage.Generic;
        _localization = localization;
        _save = save;
        _pacing = pacing;
        preferences ??= MonitorTherapyPreferences.Default;
        preferences.Validate();
        Defibrillator = new(localization, defibrillator ?? DefibrillatorConfiguration.Default, preferences.EnergyJoules, defibrillatorCommand);
        Rate.Value = preferences.PacingRatePerMinute;
        Current.Value = preferences.PacingCurrentMilliamps;
        _localization.SetChoices(PacingType, Enum.GetValues<PacingIllustration>().Select(mode =>
            (Func<Monitor.Application.Localization.ITextLocalizer, string>)(text => text.GetString($"ecgTemplate.t{165 + (int)mode:D3}"))));
        PacingType.SelectedIndex = (int)preferences.PacingType;
        _localization.Bind(ApplyPacing, ContentControl.ContentProperty, "skin.pacingApply");
        _localization.Bind(StopPacing, ContentControl.ContentProperty, "skin.pacingStop");
        foreach (var field in new[] { Rate, Current }) { field.ValueChanged += (_, _) => SaveDraft(); }
        Energy.SelectionChanged += (_, _) =>
        {
            if (Energy.SelectedItem is int joules) { energyChanged?.Invoke(joules); }
            SaveDraft();
        };
        PacingType.SelectionChanged += (_, _) => SaveDraft();
        ApplyPacing.Click += (_, _) => RunPacing(false);
        StopPacing.Click += (_, _) => RunPacing(true);
        SetPacingState(false, false, false);
    }

    internal void SetPacingState(bool running, bool pending, bool allowed, bool aedEnabled = false, bool timelineAllowed = true)
    {
        if (_shownPacing == (running, pending, allowed, aedEnabled, timelineAllowed)) { return; }
        _shownPacing = (running, pending, allowed, aedEnabled, timelineAllowed);
        ApplyPacing.IsEnabled = _pacing is not null && !pending && allowed && !aedEnabled;
        StopPacing.IsEnabled = _pacing is not null && running && !pending;
        _localization.Bind(PacingStatus, TextBlock.TextProperty,
            _pacingError ?? (aedEnabled ? "aed.pacingUnavailable" : pending ? "skin.pacingWaiting" : running ? "skin.pacingRunning" :
                !timelineAllowed ? "timeline.pacingDenied" : allowed ? "skin.pacingStopped" : "skin.pacingDisallowedState"));
        _localization.Bind(PacingStatus, ToolTip.TipProperty, text => !timelineAllowed ? text.GetString("timeline.pacingDenied") :
            aedEnabled ? text.GetString("aed.pacingUnavailable") : allowed ? "" : text.GetString("skin.pacingDisabled"));
        _localization.Bind(ApplyPacing, ContentControl.ContentProperty, running ? "skin.pacingApply" : "skin.pacingStart");
    }

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        FormatString = "0",
        MinHeight = 36,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal MonitorTherapyPreferences Read()
    {
        int Integer(NumericUpDown field) => field.Value is { } value && value == decimal.Truncate(value)
            ? checked((int)value) : throw new ArgumentException("TherapyPreferences.InvalidConfiguration");
        var settings = new MonitorTherapyPreferences((int)(Energy.SelectedItem ?? throw new ArgumentException("TherapyPreferences.InvalidConfiguration")), Integer(Rate), Integer(Current), (PacingIllustration)PacingType.SelectedIndex);
        settings.Validate();
        return settings;
    }

    private void SaveDraft()
    {
        try
        {
            var settings = Read();
            _save?.Invoke(settings);
            _pacingError = null;
            _shownPacing = null;
        }
        catch (ArgumentException) { ShowPacingError("skin.therapyInvalid"); }
    }

    private void RunPacing(bool stop)
    {
        try
        {
            _pacing?.Invoke(stop ? MonitorTherapyPreferences.Default : Read(), stop);
            _pacingError = null;
            _shownPacing = null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { ShowPacingError(error is InvalidOperationException ? "skin.pacingPending" : "skin.therapyInvalid"); }
    }

    private void ShowPacingError(string key)
    {
        _pacingError = key;
        _shownPacing = null;
        _localization.Bind(PacingStatus, TextBlock.TextProperty, key);
    }

    public MonitorChannels Channels => MonitorChannels.All;
    public MonitorManualVitals SupportedManualVitals => _package.SupportedManualVitals;
    public MonitorChannels AdditionalMonitoredChannels => TwelveLeadVisible ? MonitorChannels.Ecg : MonitorChannels.None;
    public string Id => _package.Id;
    public IBrush Background => _package.Background;
    public IBrush ChannelBrush(int channel) => _package.ChannelBrush(channel);
    public bool CompactWaveformLabels => _package.Bedside || _package.Defibrillator;
    public double NumericFontSize => _package.Defibrillator ? 76 : _package.Bedside ? 112 : 64;
    public IBrush WarningBrush => _package.WarningBrush;
    public void RefreshLayout()
    {
        if (_monitorArea is GenericMonitorArea generic) { generic.RefreshLayout(); }
        else { _monitorArea?.InvalidateMeasure(); }
    }

    public Control Compose(MonitorSkinContent content)
    {
        if (_package.Defibrillator) { return ComposeEnclosure(content); }
        var root = new Grid { RowDefinitions = new(_package.Bedside ? "50,*,58" : "Auto,*,Auto"), Background = Background };
        if (_package.Bedside)
        {
            root.Width = 1024;
            root.Height = 640;
        }
        Screen = root;
        root.Children.Add(new Border
        {
            Background = _package.Bedside ? Brush.Parse("#272C35") : Background,
            Child = content.Header
        });
        var body = _body = new Grid
        {
            ColumnDefinitions = new("*,190"),
            Margin = _package.Bedside ? new Thickness(0) : new Thickness(8, 0, 8, 8)
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        _waveforms = content.Waveforms;
        _twelveLeadWaveforms = content.TwelveLeadWaveforms;
        var waves = new Panel();
        waves.Children.Add(_waveforms);
        waves.Children.Add(_twelveLeadWaveforms);
        var areaContent = content with { Waveforms = waves };
        var monitor = _monitorArea = _package.Bedside ? new BedsideMonitorArea(areaContent) : new GenericMonitorArea(areaContent);
        monitor.Margin = new Thickness(0, 0, 8, 0);
        if (content.Nibp is MonitorManualTile nibp) { nibp.SetAppearance(_package.NibpBrush, _package.Bedside ? 88 : 30, _package.Bedside); }
        body.Children.Add(monitor);
        var therapy = new StackPanel { Spacing = 8 };
        therapy.Children.Add(Caption("skin.therapyControls"));
        therapy.Children.Add(Panel("skin.defibrillator", [Defibrillator]));
        therapy.Children.Add(Panel("skin.pacer", [Field("skin.pacingType", PacingType), Field("skin.pacingRate", Rate),
            Field("skin.pacingCurrent", Current), PacingStatus, ApplyPacing, StopPacing]));
        var scroll = new ScrollViewer { Content = therapy, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var therapyArea = TherapyArea = new Grid();
        therapyArea.Children.Add(scroll);
        Grid.SetColumn(therapyArea, 1);
        body.Children.Add(therapyArea);
        var toolbar = new MonitorToolbar { Margin = new Thickness(8, 0, 8, 8), MinimumColumnWidth = 140 };
        string layoutKey = _package.Bedside ? "bedside.layout" : "skin.layout";
        LayoutButton = ToolbarButton(layoutKey);
        _localization.Bind(LayoutButton, AutomationProperties.NameProperty, layoutKey);
        var layoutPanel = new StackPanel { Spacing = 12, MinWidth = 240 };
        layoutPanel.Children.Add(ShowTherapy);
        layoutPanel.Children.Add(Caption("skin.waveformRows"));
        layoutPanel.Children.Add(LayoutRows);
        LayoutButton.Flyout = new Flyout { Content = layoutPanel, Placement = PlacementMode.Top };
        toolbar.Children.Add(LayoutButton);
        EcgButton = ToolbarButton("skin.ecg");
        EcgButton.Click += (_, _) => SetTwelveLeadVisible(!TwelveLeadVisible);
        toolbar.Children.Add(EcgButton);
        LeadButton = ToolbarButton("skin.leads");
        var leads = new UniformGrid { Columns = 4 };
        foreach (var choice in _leadChoices) { leads.Children.Add(choice); }
        LeadButton.Flyout = new Flyout { Content = leads, Placement = PlacementMode.Top };
        RefreshLeadCaption();
        toolbar.Children.Add(LeadButton);
        UpdateTherapyVisibility();
        PrintButton = ToolbarButton("skin.print");
        PrintButton.Click += (_, _) => PrintRequested?.Invoke();
        toolbar.Children.Add(PrintButton);
        AddNibpControls(toolbar);
        AddAlarmControls(toolbar);
        Control displayedToolbar = toolbar;
        if (_package.Bedside)
        {
            toolbar.Children.Clear();
            MenuButton = ToolbarButton("bedside.menu");
            _localization.Bind(MenuButton, AutomationProperties.NameProperty, "bedside.menu");
            MenuButton.Click += (_, _) => MenuRequested?.Invoke();
            displayedToolbar = new BedsideToolbar(
            [
                (LeadButton, BedsideToolbarIcon.Lead),
                (NibpStartButton, BedsideToolbarIcon.Nibp),
                (NibpStopButton, BedsideToolbarIcon.NibpStop),
                (NibpAutoButton, BedsideToolbarIcon.NibpAuto),
                (PrintButton, BedsideToolbarIcon.Print),
                (LayoutButton, BedsideToolbarIcon.Layout),
                (AlarmsButton, BedsideToolbarIcon.Alarm),
                (EcgButton, BedsideToolbarIcon.Ecg),
                (SilenceButton, BedsideToolbarIcon.Silence),
                (MenuButton, BedsideToolbarIcon.Menu)
            ]);
        }
        Grid.SetRow(displayedToolbar, 2);
        root.Children.Add(displayedToolbar);
        SetTwelveLeadVisible(TwelveLeadVisible);
        Control display = _package.Bedside
            ? new Border { Background = Background, Child = new Viewbox { Stretch = Stretch.Uniform, Child = root } }
            : root;
        return new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = display };
    }

    private Border Panel(string titleKey, Control[] controls)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Caption(titleKey));
        foreach (var control in controls) { panel.Children.Add(control); }
        return new Border
        {
            Background = Brush.Parse("#121820"),
            BorderBrush = Brush.Parse("#34414F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = panel
        };
    }

    private StackPanel Field(string key, Control control)
    {
        _localization.Bind(control, AutomationProperties.NameProperty, key);
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(Caption(key));
        panel.Children.Add(control);
        return panel;
    }

    private TextBlock Caption(string key)
    {
        var text = new TextBlock { Foreground = Brush.Parse("#BAC5D1"), FontSize = 12, Margin = new Thickness(4) };
        _localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }

    private Button ToolbarButton(string key)
    {
        var caption = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        _localization.Bind(caption, TextBlock.TextProperty, key);
        var button = new Button
        {
            Content = caption,
            MinHeight = 44,
            Padding = new Thickness(8, 6),
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush.Parse(key == "skin.shock" ? "#683039" : key == "skin.charge" ? "#58451C" : "#222C38"),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(5),
            FontSize = 14
        };
        return button;
    }

    private sealed class MonitorToolbar : UniformGrid
    {
        internal double MinimumColumnWidth { get; init; }
        protected override Size MeasureOverride(Size availableSize)
        {
            // Keep enough room for translated actions before moving to two rows.
            Columns = availableSize.Width < 8 * MinimumColumnWidth ? 4 : 8;
            return base.MeasureOverride(availableSize);
        }
    }
}
