// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
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
    private readonly IReadOnlyDictionary<MonitorNumeric, ConfirmedLimitNotice> _notices =
        MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new ConfirmedLimitNotice(d.Numeric));
    internal IReadOnlyDictionary<MonitorNumeric, LimitEditor> Editors { get; } =
        MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new LimitEditor(d));
    internal IReadOnlyList<AlarmLifecycleJournal> Lifecycles => _notices.Values.Select(n => n.Lifecycle).ToArray();

    internal AdditionalMeasurementLimits()
    {
        foreach (var (numeric, notice) in _notices)
        {
            var editor = Editors[numeric];
            editor.Enabled.IsCheckedChanged += (_, _) => notice.Reset(editor.Enabled.IsChecked == true
                ? AlarmTransitionReason.ConfigurationChanged : AlarmTransitionReason.Disabled);
            foreach (var field in new[] { editor.CriticalLow, editor.WarningLow, editor.WarningHigh, editor.CriticalHigh }.Concat(editor.Confirmation.Fields))
            { field.ValueChanged += (_, _) => notice.Reset(AlarmTransitionReason.ConfigurationChanged); }
        }
    }
    internal IEnumerable<MonitorNotice> Notices(LiveMeasurementSnapshot snapshot)
    {
        foreach (var descriptor in MeasuredLimitNotice.Descriptors)
        {
            var limits = Editors[descriptor.Numeric].Limits;
            MonitorNotice? notice;
            MeasurementConfirmationTiming? timing = null;
            try { timing = Editors[descriptor.Numeric].Confirmation.Read(); }
            catch (ArgumentException) { _notices[descriptor.Numeric].Reset(AlarmTransitionReason.InvalidConfiguration); }
            notice = timing is null
                ? new(descriptor.Id + "-settings", MonitorNoticeLevel.Info, descriptor.Label + " 确认时间无效：请输入 0–600 秒，最多三位小数")
                : _notices[descriptor.Numeric].Evaluate(limits, snapshot, timing);
            if (notice is not null)
            { yield return notice; }
        }
    }
    internal void Reset(AlarmTransitionReason reason = AlarmTransitionReason.SessionReset)
    {
        foreach (var notice in _notices.Values) { notice.Reset(reason); }
    }

    internal sealed class LimitEditor : StackPanel
    {
        private readonly int _divisor;
        internal CheckBox Enabled { get; } = new() { Content = "启用此参数上下限提示", IsChecked = false };
        internal NumericUpDown CriticalLow { get; }
        internal NumericUpDown WarningLow { get; }
        internal NumericUpDown WarningHigh { get; }
        internal NumericUpDown CriticalHigh { get; }
        internal AlarmConfirmationEditor Confirmation { get; }
        internal LimitEditor(MeasurementLimitDescriptor descriptor)
        {
            _divisor = descriptor.Divisor; Spacing = 6;
            AutomationProperties.SetName(Enabled, "启用 " + descriptor.Label + " 上下限提示");
            Children.Add(Enabled);
            var thresholds = AlarmConfirmationEditor.CreateFieldsPanel();
            CriticalLow = Add("Critical 下限", descriptor.TeachingDefaults.CriticalLow!.Value);
            WarningLow = Add("Warning 下限", descriptor.TeachingDefaults.WarningLow!.Value);
            WarningHigh = Add("Warning 上限", descriptor.TeachingDefaults.WarningHigh!.Value);
            CriticalHigh = Add("Critical 上限", descriptor.TeachingDefaults.CriticalHigh!.Value);
            Confirmation = new(descriptor);
            Confirmation.SetThresholdContent(thresholds);
            Children.Add(Confirmation);
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
                var row = new StackPanel { Spacing = 6, Width = 220, Margin = new Thickness(0, 0, 24, 12) };
                row.Children.Add(new TextBlock { Text = text });
                row.Children.Add(number);
                thresholds.Children.Add(row);
                return number;
            }
        }
        internal MeasurementLimits Limits => new(Enabled.IsChecked == true, Units(CriticalLow), Units(WarningLow), Units(WarningHigh), Units(CriticalHigh));
        private int? Units(NumericUpDown control) => control.Value is { } value ? checked((int)(value * _divisor)) : null;
    }
}
