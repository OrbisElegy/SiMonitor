// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Synthetic verification controls; never an exam policy editor.
internal sealed class DemoMeasurementControls : StackPanel
{
    internal Button Enabled { get; } = new() { Content = "启用测量" };
    internal Button Disabled { get; } = new() { Content = "禁用测量" };
    internal Button Locked { get; } = new() { Content = "课程锁定" };
    internal CheckBox Auxiliary { get; } = new() { Content = "允许辅助频率", VerticalAlignment = VerticalAlignment.Center };
    private bool _updating;

    public DemoMeasurementControls()
    {
        IsVisible = false;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Spacing = 8;
        Children.Add(Enabled);
        Children.Add(Disabled);
        Children.Add(Locked);
        Children.Add(Auxiliary);
    }

    internal void Bind(Action<SystemViewCommandAssessmentPolicy> select, Action<bool> auxiliary)
    {
        Enabled.Click += (_, _) => { if (Enabled.IsEnabled) { select(SystemViewCommandAssessmentPolicy.Enabled); } };
        Disabled.Click += (_, _) => { if (Disabled.IsEnabled) { select(SystemViewCommandAssessmentPolicy.Disabled); } };
        Locked.Click += (_, _) => { if (Locked.IsEnabled) { select(SystemViewCommandAssessmentPolicy.CourseLocked); } };
        Auxiliary.IsCheckedChanged += (_, _) => { if (!_updating) { auxiliary(Auxiliary.IsChecked == true); } };
        IsVisible = true;
    }

    internal void Update(SystemViewCommandAssessmentPolicy policy, bool auxiliary)
    {
        Enabled.IsEnabled = policy != SystemViewCommandAssessmentPolicy.Enabled;
        Disabled.IsEnabled = policy != SystemViewCommandAssessmentPolicy.Disabled;
        Locked.IsEnabled = policy != SystemViewCommandAssessmentPolicy.CourseLocked;
        _updating = true;
        try { Auxiliary.IsChecked = auxiliary; }
        finally { _updating = false; }
    }
}
