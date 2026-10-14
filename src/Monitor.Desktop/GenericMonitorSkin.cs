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

internal sealed partial class GenericMonitorSkin : IMonitorSkin
{
    private readonly DesktopLocalization _localization;
    private readonly Action<MonitorTherapyPreferences>? _save;
    private readonly Func<MonitorTherapyPreferences, bool, string>? _pacing;
    private (bool Running, bool Pending, bool Allowed, bool AedEnabled)? _shownPacing;
    private bool _pacingFeedback;
    private Grid? _body, _monitorArea;
    internal Grid? TherapyArea { get; private set; }
    internal Button LayoutButton { get; private set; } = null!;
    internal Button EcgButton { get; private set; } = null!;
    internal Button PrintButton { get; private set; } = null!;
    internal event Action? PrintRequested;
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
        bool changed = TwelveLeadVisible != visible;
        TwelveLeadVisible = visible;
        if (_waveforms is null || _twelveLeadWaveforms is null) { return; }
        _waveforms.IsVisible = !visible;
        _twelveLeadWaveforms.IsVisible = visible;
        _twelveLeadWaveforms.InvalidateVisual();
        EcgButton.Background = Brush.Parse(visible ? "#145AA3" : "#222C38");
        _localization.Bind(EcgButton, AutomationProperties.NameProperty, visible ? "skin.ecgSelected" : "skin.ecg");
        _localization.Bind(EcgButton, ToolTip.TipProperty, visible ? "skin.returnMonitor" : "skin.ecg");
        if (changed) { EcgViewChanged?.Invoke(); }
    }
    internal ComboBox LayoutRows { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ToggleSwitch ShowTherapy { get; } = new() { MinHeight = 44, IsChecked = true };
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
        if (_body is null || _monitorArea is null || TherapyArea is null) { return; }
        bool visible = ShowTherapy.IsChecked == true;
        TherapyArea.IsVisible = visible;
        _body.ColumnDefinitions[1].Width = new GridLength(visible ? 190 : 0);
        _monitorArea.Margin = new Thickness(0, 0, visible ? 8 : 0, 0);
    }
    internal TextBlock PacingStatus { get; } = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    internal DefibrillatorControls Defibrillator { get; }
    internal ComboBox Energy => Defibrillator.Energy;
    public DefibrillatorConfiguration? DefibrillatorOverride => null;
    internal NumericUpDown Rate { get; } = Number(70, 30, 180, 5);
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
        Action<MonitorTherapyPreferences>? save = null, Func<MonitorTherapyPreferences, bool, string>? pacing = null,
        DefibrillatorConfiguration? defibrillator = null, Action<DefibrillatorCommand>? defibrillatorCommand = null, Action<int>? energyChanged = null)
    {
        _localization = localization;
        _save = save;
        _pacing = pacing;
        preferences ??= MonitorTherapyPreferences.Default;
        preferences.Validate();
        Defibrillator = new(localization, defibrillator ?? DefibrillatorConfiguration.Default, preferences.EnergyJoules, defibrillatorCommand, ShowFeedback);
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

    internal void SetPacingState(bool running, bool pending, bool allowed, bool aedEnabled = false)
    {
        if (_shownPacing == (running, pending, allowed, aedEnabled)) { return; }
        _shownPacing = (running, pending, allowed, aedEnabled);
        ApplyPacing.IsEnabled = _pacing is not null && !pending && allowed && !aedEnabled;
        StopPacing.IsEnabled = _pacing is not null && running && !pending;
        _localization.Bind(PacingStatus, TextBlock.TextProperty,
            aedEnabled ? "aed.pacingUnavailable" : pending ? "skin.pacingWaiting" : running ? "skin.pacingRunning" : allowed ? "skin.pacingStopped" : "skin.pacingDisallowedState");
        _localization.Bind(PacingStatus, ToolTip.TipProperty, text => aedEnabled ? text.GetString("aed.pacingUnavailable") : allowed ? "" : text.GetString("skin.pacingDisabled"));
        _localization.Bind(ApplyPacing, ContentControl.ContentProperty, running ? "skin.pacingApply" : "skin.pacingStart");
        if (_pacingFeedback)
        {
            Feedback.IsVisible = false;
            _pacingFeedback = false;
        }
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
        try { var settings = Read(); _save?.Invoke(settings); }
        catch (ArgumentException) { ShowFeedback("skin.therapyInvalid"); }
    }

    private void RunPacing(bool stop)
    {
        try
        {
            ShowFeedback(_pacing?.Invoke(stop ? MonitorTherapyPreferences.Default : Read(), stop) ?? "skin.pacingUnavailable");
            _pacingFeedback = true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { ShowFeedback(error is InvalidOperationException ? "skin.pacingPending" : "skin.therapyInvalid"); }
    }

    internal void ShowFeedback(string key)
    {
        _pacingFeedback = false;
        Feedback.IsVisible = true;
        _localization.Bind(Feedback, TextBlock.TextProperty, key);
    }

    private static readonly string[] Colors = ["#53F28C", "#FFE16A", "#49BFFF", "#FF7777", "#F4F6FA", "#CAA7EA", "#F2B67D"];
    public MonitorChannels Channels => MonitorChannels.All;
    public MonitorChannels AdditionalMonitoredChannels => TwelveLeadVisible ? MonitorChannels.Ecg : MonitorChannels.None;
    public string Id => "generic";
    public IBrush Background => Brushes.Black;
    public IBrush ChannelBrush(int channel) => Brush.Parse(Colors[channel]);
    public void RefreshLayout() => _monitorArea?.InvalidateMeasure();
    internal TextBlock Feedback { get; } = new()
    {
        Foreground = Brush.Parse("#BAC5D1"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8),
        MinHeight = 36,
        IsVisible = false
    };

    public Control Compose(MonitorSkinContent content)
    {
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), Background = Background };
        root.Children.Add(content.Header);
        var body = _body = new Grid { ColumnDefinitions = new("*,190"), Margin = new Thickness(8, 0, 8, 8) };
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        _waveforms = content.Waveforms;
        _twelveLeadWaveforms = content.TwelveLeadWaveforms;
        var waves = new Panel();
        waves.Children.Add(_waveforms);
        waves.Children.Add(_twelveLeadWaveforms);
        var monitor = _monitorArea = new GenericMonitorArea(content with { Waveforms = waves }) { Margin = new Thickness(0, 0, 8, 0) };
        body.Children.Add(monitor);
        var therapy = new StackPanel { Spacing = 8 };
        therapy.Children.Add(Caption("skin.therapyControls"));
        therapy.Children.Add(Panel("skin.defibrillator", [Defibrillator], []));
        therapy.Children.Add(Panel("skin.pacer", [Field("skin.pacingType", PacingType), Field("skin.pacingRate", Rate),
            Field("skin.pacingCurrent", Current), PacingStatus, ApplyPacing, StopPacing], []));
        var scroll = new ScrollViewer { Content = therapy, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var therapyArea = TherapyArea = new Grid();
        therapyArea.Children.Add(scroll);
        Grid.SetRow(Feedback, 2);
        root.Children.Add(Feedback);
        Grid.SetColumn(therapyArea, 1);
        body.Children.Add(therapyArea);
        var toolbar = new UniformGrid { Columns = 8, Margin = new Thickness(8, 0, 8, 8) };
        LayoutButton = ToolbarButton("skin.layout");
        _localization.Bind(LayoutButton, AutomationProperties.NameProperty, "skin.layout");
        var layoutPanel = new StackPanel { Spacing = 12, MinWidth = 240 };
        layoutPanel.Children.Add(ShowTherapy);
        layoutPanel.Children.Add(Caption("skin.waveformRows"));
        layoutPanel.Children.Add(LayoutRows);
        LayoutButton.Flyout = new Flyout { Content = layoutPanel, Placement = PlacementMode.Top };
        toolbar.Children.Add(LayoutButton);
        EcgButton = ToolbarButton("skin.ecg");
        EcgButton.Click += (_, _) => SetTwelveLeadVisible(!TwelveLeadVisible);
        toolbar.Children.Add(EcgButton);
        SetTwelveLeadVisible(TwelveLeadVisible);
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
        Grid.SetRow(toolbar, 3);
        root.Children.Add(toolbar);
        return new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = root };
    }

    private Border Panel(string titleKey, Control[] controls, string[] buttons)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Caption(titleKey));
        foreach (var control in controls) { panel.Children.Add(control); }
        if (buttons.Length > 0)
        {
            var actions = new UniformGrid { Columns = 2 };
            foreach (string key in buttons) { actions.Children.Add(DummyButton(key)); }
            panel.Children.Add(actions);
        }
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
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush.Parse(key == "skin.shock" ? "#683039" : key == "skin.charge" ? "#58451C" : "#222C38"),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(5),
            FontSize = 14
        };
        return button;
    }

    private Button DummyButton(string key)
    {
        var button = ToolbarButton(key);
        button.Classes.Add("skin-dummy");
        _localization.Bind(button, AutomationProperties.NameProperty, text => text.GetString(key) + " · " + text.GetString("skin.preview"));
        button.Click += (_, _) =>
        {
            Feedback.IsVisible = true;
            _localization.Bind(Feedback, TextBlock.TextProperty,
                text => text.Format("skin.unavailable", text.GetString(key)));
        };
        return button;
    }
}
