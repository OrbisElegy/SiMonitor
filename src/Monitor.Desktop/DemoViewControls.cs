// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Explicit synthetic demo controls, not a production command bar.
internal sealed class DemoViewControls : StackPanel
{
    internal Button Zoom100 { get; } = new() { Content = "100%" };
    internal Button Zoom200 { get; } = new() { Content = "200%" };
    internal Button Zoom400 { get; } = new() { Content = "400%" };
    internal Button Paper { get; } = new() { Content = "纸张主题" };
    internal Button Dark { get; } = new() { Content = "深色主题" };

    public DemoViewControls()
    {
        IsVisible = false;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Spacing = 8;
        Children.Add(Zoom100);
        Children.Add(Zoom200);
        Children.Add(Zoom400);
        Children.Add(Paper);
        Children.Add(Dark);
    }

    internal void Bind(Action<uint> zoom, Action<Ecg12Theme> theme)
    {
        Zoom100.Click += (_, _) => { if (Zoom100.IsEnabled) { zoom(1); } };
        Zoom200.Click += (_, _) => { if (Zoom200.IsEnabled) { zoom(2); } };
        Zoom400.Click += (_, _) => { if (Zoom400.IsEnabled) { zoom(4); } };
        Paper.Click += (_, _) => { if (Paper.IsEnabled) { theme(Ecg12Theme.PaperGridBlack); } };
        Dark.Click += (_, _) => { if (Dark.IsEnabled) { theme(Ecg12Theme.MonitorDarkGreen); } };
        IsVisible = true;
    }

    internal void Update(uint scale, Ecg12Theme theme)
    {
        Zoom100.IsEnabled = scale != 1;
        Zoom200.IsEnabled = scale != 2;
        Zoom400.IsEnabled = scale != 4;
        Paper.IsEnabled = theme != Ecg12Theme.PaperGridBlack;
        Dark.IsEnabled = theme != Ecg12Theme.MonitorDarkGreen;
    }
}
