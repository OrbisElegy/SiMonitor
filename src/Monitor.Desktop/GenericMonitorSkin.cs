// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class GenericMonitorSkin : IMonitorSkin
{
    private readonly DesktopLocalization _localization;
    private readonly Action<MonitorTherapyPreferences>? _save;
    private readonly Func<MonitorTherapyPreferences, bool, string>? _pacing;
    internal NumericUpDown Energy { get; } = Number(150, 1, 1000, 10);
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
        Action<MonitorTherapyPreferences>? save = null, Func<MonitorTherapyPreferences, bool, string>? pacing = null)
    {
        _localization = localization;
        _save = save;
        _pacing = pacing;
        preferences ??= MonitorTherapyPreferences.Default;
        preferences.Validate();
        Energy.Value = preferences.EnergyJoules;
        Rate.Value = preferences.PacingRatePerMinute;
        Current.Value = preferences.PacingCurrentMilliamps;
        _localization.SetChoices(PacingType, Enum.GetValues<PacingIllustration>().Select(mode =>
            (Func<Monitor.Application.Localization.ITextLocalizer, string>)(text => text.GetString($"ecgTemplate.t{165 + (int)mode:D3}"))));
        PacingType.SelectedIndex = (int)preferences.PacingType;
        _localization.Bind(ApplyPacing, ContentControl.ContentProperty, "skin.pacingApply");
        _localization.Bind(StopPacing, ContentControl.ContentProperty, "skin.pacingStop");
        foreach (var field in new[] { Energy, Rate, Current }) { field.ValueChanged += (_, _) => SaveDraft(); }
        PacingType.SelectionChanged += (_, _) => SaveDraft();
        ApplyPacing.Click += (_, _) => RunPacing(false);
        StopPacing.Click += (_, _) => RunPacing(true);
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
        var settings = new MonitorTherapyPreferences(Integer(Energy), Integer(Rate), Integer(Current), (PacingIllustration)PacingType.SelectedIndex);
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
        try { ShowFeedback(_pacing?.Invoke(stop ? MonitorTherapyPreferences.Default : Read(), stop) ?? "skin.pacingUnavailable"); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { ShowFeedback(error is InvalidOperationException ? "skin.pacingPending" : "skin.therapyInvalid"); }
    }

    internal void ShowFeedback(string key)
    {
        Feedback.IsVisible = true;
        _localization.Bind(Feedback, TextBlock.TextProperty, key);
    }

    private static readonly string[] Colors = ["#53F28C", "#FFE16A", "#49BFFF", "#FF7777", "#F4F6FA", "#CAA7EA", "#F2B67D"];
    public MonitorChannels Channels => MonitorChannels.All;
    public string Id => "generic";
    public IBrush Background => Brushes.Black;
    public IBrush ChannelBrush(int channel) => Brush.Parse(Colors[channel]);
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
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Background = Background };
        root.Children.Add(content.Header);
        var body = new Grid { ColumnDefinitions = new("*,190"), Margin = new Thickness(8, 0, 8, 8) };
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var monitor = new Grid { RowDefinitions = new("*,100"), ColumnDefinitions = new("*,200"), Margin = new Thickness(0, 0, 8, 0) };
        body.Children.Add(monitor);
        monitor.Children.Add(content.Waveforms);
        Grid.SetColumn(content.Numerics, 1);
        monitor.Children.Add(content.Numerics);
        var extras = new UniformGrid { Columns = 3 };
        extras.Children.Add(content.Temperature);
        extras.Children.Add(content.Custom1);
        extras.Children.Add(content.Custom2);
        Grid.SetRow(extras, 1);
        monitor.Children.Add(extras);
        Grid.SetRow(content.Nibp, 1);
        Grid.SetColumn(content.Nibp, 1);
        monitor.Children.Add(content.Nibp);
        var therapy = new StackPanel { Spacing = 8 };
        therapy.Children.Add(Caption("skin.therapyControls"));
        therapy.Children.Add(Panel("skin.defibrillator", [Field("skin.energy", Energy)],
            ["skin.aed", "skin.charge", "skin.shock", "skin.disarm", "skin.sync"]));
        therapy.Children.Add(Panel("skin.pacer", [Field("skin.pacingType", PacingType), Field("skin.pacingRate", Rate),
            Field("skin.pacingCurrent", Current), ApplyPacing, StopPacing], []));
        var scroll = new ScrollViewer { Content = therapy, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var therapyArea = new Grid { RowDefinitions = new("*,Auto") };
        therapyArea.Children.Add(scroll);
        Grid.SetRow(Feedback, 1);
        therapyArea.Children.Add(Feedback);
        Grid.SetColumn(therapyArea, 1);
        body.Children.Add(therapyArea);
        var toolbar = new UniformGrid { Columns = 8, Margin = new Thickness(8, 0, 8, 8) };
        foreach (string key in new[] { "skin.layout", "skin.ecg", "skin.leads", "skin.print", "skin.nibpStart", "skin.nibpAuto", "skin.alarms", "skin.silence" })
        { toolbar.Children.Add(DummyButton(key)); }
        Grid.SetRow(toolbar, 2);
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

    private Button DummyButton(string key)
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
