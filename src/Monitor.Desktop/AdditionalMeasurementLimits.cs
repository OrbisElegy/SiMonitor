// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// One editor at a time; switching parameters retains each independent setting.
internal sealed class AdditionalMeasurementLimits : StackPanel
{
    internal ComboBox Parameter { get; } = new()
    { ItemsSource = MeasuredLimitNotice.Descriptors.Select(d => d.Label).ToArray(), SelectedIndex = 0, MinWidth = 240 };
    internal IReadOnlyDictionary<MonitorNumeric, LimitEditor> Editors { get; }
    private readonly ContentControl _editor = new();
    internal AdditionalMeasurementLimits()
    {
        Spacing = 8;
        Editors = MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new LimitEditor(d));
        Children.Add(new TextBlock { Text = "其他实测参数上下限", FontWeight = FontWeight.SemiBold });
        Children.Add(Parameter); Children.Add(_editor);
        AutomationProperties.SetName(Parameter, "选择报警参数");
        Parameter.SelectionChanged += (_, _) => Select(); Select();
        Children.Add(new TextBlock
        {
            Text = "各参数独立启用，切换参数保留设置。默认阈值仅作教学示例。只比较有效实测值；压力为平均压，RESP 与 CO₂ 呼吸率分别计算。无效读数不推断窒息或传感器脱落。",
            TextWrapping = TextWrapping.Wrap
        });
    }
    private void Select() => _editor.Content = Parameter.SelectedIndex >= 0 && Parameter.SelectedIndex < MeasuredLimitNotice.Descriptors.Count
        ? Editors[MeasuredLimitNotice.Descriptors[Parameter.SelectedIndex].Numeric] : null;
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
