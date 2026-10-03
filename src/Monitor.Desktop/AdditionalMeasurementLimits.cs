// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Persistent independent editors; each is hosted in its own alarm category.
internal sealed class AdditionalMeasurementLimits
{
    private readonly IReadOnlyDictionary<MonitorNumeric, PressureLimitNotice> _pressureNotices =
        new[] { MonitorNumeric.AbpMean, MonitorNumeric.PaMean, MonitorNumeric.CvpMean }
            .ToDictionary(numeric => numeric, numeric => new PressureLimitNotice(numeric));
    internal IReadOnlyDictionary<MonitorNumeric, LimitEditor> Editors { get; } =
        MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new LimitEditor(d));
    internal AdditionalMeasurementLimits()
    {
        foreach (var (numeric, pressure) in _pressureNotices)
        {
            var editor = Editors[numeric];
            editor.Enabled.IsCheckedChanged += (_, _) => pressure.Reset();
            foreach (var field in new[] { editor.CriticalLow, editor.WarningLow, editor.WarningHigh, editor.CriticalHigh })
            { field.ValueChanged += (_, _) => pressure.Reset(); }
        }
    }
    internal IEnumerable<MonitorNotice> Notices(LiveMeasurementSnapshot snapshot)
    {
        foreach (var descriptor in MeasuredLimitNotice.Descriptors)
        {
            var limits = Editors[descriptor.Numeric].Limits;
            var notice = _pressureNotices.TryGetValue(descriptor.Numeric, out var pressure)
                ? pressure.Evaluate(limits, snapshot)
                : MeasuredLimitNotice.Evaluate(descriptor.Numeric, limits, snapshot);
            if (notice is not null)
            { yield return notice; }
        }
    }
    internal void Reset()
    {
        foreach (var pressure in _pressureNotices.Values) { pressure.Reset(); }
    }

    internal sealed class LimitEditor : StackPanel
    {
        private readonly int _divisor;
        internal CheckBox Enabled { get; } = new() { Content = "启用此参数上下限提示", IsChecked = false };
        internal NumericUpDown CriticalLow { get; }
        internal NumericUpDown WarningLow { get; }
        internal NumericUpDown WarningHigh { get; }
        internal NumericUpDown CriticalHigh { get; }
        internal LimitEditor(MeasurementLimitDescriptor descriptor)
        {
            _divisor = descriptor.Divisor; Spacing = 6;
            AutomationProperties.SetName(Enabled, "启用 " + descriptor.Label + " 上下限提示");
            Children.Add(Enabled);
            CriticalLow = Add("Critical 下限", descriptor.TeachingDefaults.CriticalLow!.Value);
            WarningLow = Add("Warning 下限", descriptor.TeachingDefaults.WarningLow!.Value);
            WarningHigh = Add("Warning 上限", descriptor.TeachingDefaults.WarningHigh!.Value);
            CriticalHigh = Add("Critical 上限", descriptor.TeachingDefaults.CriticalHigh!.Value);
            if (descriptor.Numeric is MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean)
            { Children.Add(DesktopInformationPages.Help("pressure-alarm-validation")); }
            NumericUpDown Add(string label, int value)
            {
                string text = descriptor.Label + " " + label + "（" + descriptor.Unit + "）";
                var number = new NumericUpDown
                {
                    Value = (decimal)value / _divisor,
                    Minimum = (decimal)descriptor.Minimum / _divisor,
                    Maximum = (decimal)descriptor.Maximum / _divisor,
                    Increment = .25m,
                    Width = 220,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                AutomationProperties.SetName(number, text);
                Children.Add(new TextBlock { Text = text }); Children.Add(number); return number;
            }
        }
        internal MeasurementLimits Limits => new(Enabled.IsChecked == true, Units(CriticalLow), Units(WarningLow), Units(WarningHigh), Units(CriticalHigh));
        private int? Units(NumericUpDown control) => control.Value is { } value ? checked((int)(value * _divisor)) : null;
    }
}
