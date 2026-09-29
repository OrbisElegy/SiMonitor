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
    internal IReadOnlyDictionary<MonitorNumeric, LimitEditor> Editors { get; } =
        MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new LimitEditor(d));
    internal IEnumerable<MonitorNotice> Notices(LiveMeasurementSnapshot snapshot)
    {
        foreach (var descriptor in MeasuredLimitNotice.Descriptors)
            if (MeasuredLimitNotice.Evaluate(descriptor.Numeric, Editors[descriptor.Numeric].Limits, snapshot) is { } notice)
            { yield return notice; }
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
