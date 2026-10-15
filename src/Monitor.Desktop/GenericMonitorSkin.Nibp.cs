// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class GenericMonitorSkin
{
    internal Button NibpStartButton { get; private set; } = null!;
    internal Button NibpStopButton { get; private set; } = null!;
    internal Button NibpAutoButton { get; private set; } = null!;
    internal ToggleSwitch NibpAutoEnabled { get; } = new() { MinHeight = 44 };
    internal ComboBox NibpInterval { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal event Action? NibpStartRequested;
    internal event Action? NibpStopRequested;
    internal event Action<bool, int>? NibpAutoChanged;
    private bool _refreshingNibp;
    private (bool Enabled, bool Measuring, bool Automatic, int Interval)? _shownNibp;

    private void AddNibpControls(Panel toolbar)
    {
        NibpStartButton = ToolbarButton(_package.Bedside ? "bedside.nibpStart" : "skin.nibpStart");
        NibpStartButton.Click += (_, _) => NibpStartRequested?.Invoke();
        toolbar.Children.Add(NibpStartButton);
        NibpStopButton = ToolbarButton("bedside.nibpStop");
        _localization.Bind(NibpStopButton, AutomationProperties.NameProperty, "bedside.nibpStop");
        NibpStopButton.Click += (_, _) => NibpStopRequested?.Invoke();
        NibpAutoButton = ToolbarButton(_package.Bedside ? "bedside.nibpAuto" : "skin.nibpAuto");
        _localization.Bind(NibpAutoEnabled, ContentControl.ContentProperty, "nibp.autoEnabled");
        _localization.Bind(NibpAutoEnabled, AutomationProperties.NameProperty, "nibp.autoEnabled");
        _localization.SetChoices(NibpInterval, NibpMeasurement.AutoIntervalsMinutes.Select(minutes =>
            (Func<Monitor.Application.Localization.ITextLocalizer, string>)(text => text.Format("nibp.minutes", minutes))));
        _localization.Bind(NibpInterval, AutomationProperties.NameProperty, "nibp.interval");
        NibpInterval.SelectedIndex = 3;
        var label = new TextBlock();
        _localization.Bind(label, TextBlock.TextProperty, "nibp.interval");
        var form = new StackPanel { Spacing = 10, MinWidth = 200 };
        form.Children.Add(NibpAutoEnabled);
        form.Children.Add(label);
        form.Children.Add(NibpInterval);
        NibpAutoButton.Flyout = new Flyout { Content = form, Placement = PlacementMode.Top };
        void Change()
        {
            if (_refreshingNibp || NibpInterval.SelectedIndex < 0) { return; }
            NibpAutoChanged?.Invoke(NibpAutoEnabled.IsChecked == true, NibpMeasurement.AutoIntervalsMinutes[NibpInterval.SelectedIndex]);
        }
        NibpAutoEnabled.IsCheckedChanged += (_, _) => Change();
        NibpInterval.SelectionChanged += (_, _) => Change();
        toolbar.Children.Add(NibpAutoButton);
        NibpStartButton.IsEnabled = NibpStopButton.IsEnabled = NibpAutoButton.IsEnabled = false;
    }

    internal void RefreshNibp(NibpMeasurement measurement)
    {
        if (NibpStartButton is null) { return; }
        var state = (measurement.Enabled, measurement.Measuring, measurement.AutoEnabled, measurement.AutoIntervalMinutes);
        if (_shownNibp == state) { return; }
        _shownNibp = state;
        _refreshingNibp = true;
        try
        {
            NibpStartButton.IsEnabled = measurement.Enabled && (!_package.Bedside || !measurement.Measuring);
            NibpStopButton.IsEnabled = measurement.Enabled && (measurement.Measuring || measurement.AutoEnabled);
            NibpAutoButton.IsEnabled = measurement.Enabled;
            NibpAutoEnabled.IsEnabled = NibpInterval.IsEnabled = measurement.Enabled;
            if (!measurement.Enabled) { NibpAutoButton.Flyout?.Hide(); }
            NibpAutoEnabled.IsChecked = measurement.AutoEnabled;
            NibpInterval.SelectedIndex = NibpMeasurement.AutoIntervalsMinutes.ToList().IndexOf(measurement.AutoIntervalMinutes);
            string startKey = _package.Bedside ? "bedside.nibpStart" : measurement.Measuring ? "nibp.stop" : "skin.nibpStart";
            _localization.Bind((TextBlock)NibpStartButton.Content!, TextBlock.TextProperty, startKey);
            _localization.Bind(NibpStartButton, AutomationProperties.NameProperty, startKey);
            string autoKey = _package.Bedside ? "bedside.nibpAuto" : measurement.AutoEnabled ? "nibp.autoActive" : "skin.nibpAuto";
            object?[] arguments = !_package.Bedside && measurement.AutoEnabled ? [measurement.AutoIntervalMinutes] : [];
            _localization.Bind((TextBlock)NibpAutoButton.Content!, TextBlock.TextProperty, autoKey, arguments);
            _localization.Bind(NibpAutoButton, AutomationProperties.NameProperty, autoKey, arguments);
            NibpAutoButton.Background = _package.Bedside ? BedsideToolbar.KeyBackground(measurement.AutoEnabled)
                : Brush.Parse(measurement.AutoEnabled ? "#145AA3" : "#222C38");
        }
        finally { _refreshingNibp = false; }
    }
}
