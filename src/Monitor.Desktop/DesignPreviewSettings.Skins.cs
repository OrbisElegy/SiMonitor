// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    internal ComboBox MonitorAppearance { get; } = new() { MinHeight = 44, MinWidth = 220 };
    internal Button ImportSkin { get; } = new() { MinHeight = 44 };
    internal Button ExportSkin { get; } = new() { MinHeight = 44 };
    internal Button RecommendedSkinLayout { get; } = new() { MinHeight = 44 };
    internal TextBlock SkinStatus { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal event Action<MonitorSkinPackage>? MonitorAppearanceChanged;
    private MonitorSkinPackage[] _skinPackages = [MonitorSkinPackage.Generic, MonitorSkinPackage.BedsideMonitor];
    private bool _restoringSkin;

    private StackPanel BuildSkinSelection()
    {
        var panel = new StackPanel { Spacing = 10 };
        var label = new Label { Target = MonitorAppearance, Padding = new Thickness(0) };
        Localization.Bind(label, ContentControl.ContentProperty, "skin.appearance");
        Localization.Bind(MonitorAppearance, AutomationProperties.NameProperty, "skin.appearance");
        panel.Children.Add(label);
        panel.Children.Add(MonitorAppearance);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (button, key) in new[] { (ImportSkin, "skin.import"), (ExportSkin, "skin.export"), (RecommendedSkinLayout, "skin.recommendedLayout") })
        {
            Localization.Bind(button, ContentControl.ContentProperty, key);
            button.Margin = new Thickness(0, 0, 8, 4);
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        panel.Children.Add(SkinStatus);
        RestoreMonitorAppearance(MonitorSkinPackage.Generic);
        MonitorAppearance.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, MonitorAppearance) && !_restoringSkin && MonitorAppearance.SelectedIndex >= 0)
            { MonitorAppearanceChanged?.Invoke(_skinPackages[MonitorAppearance.SelectedIndex]); }
        };
        return panel;
    }

    internal void RestoreMonitorAppearance(MonitorSkinPackage package)
    {
        _restoringSkin = true;
        try
        {
            _skinPackages = [MonitorSkinPackage.Generic, MonitorSkinPackage.BedsideMonitor];
            int selected = Array.FindIndex(_skinPackages, item => item.Matches(package));
            if (selected < 0) { _skinPackages = [.. _skinPackages, package]; selected = 2; }
            MonitorAppearance.ItemsSource = _skinPackages.Select(item => item.Name).ToArray();
            MonitorAppearance.SelectedIndex = selected;
        }
        finally { _restoringSkin = false; }
    }
}
