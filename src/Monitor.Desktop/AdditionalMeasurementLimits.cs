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
    private MonitorChannels _monitoredChannels = MonitorChannels.All;
    internal void SetMonitoredChannels(MonitorChannels channels)
    {
        _monitoredChannels = channels;
        foreach (var (numeric, notice) in _notices)
        {
            bool monitored = (channels & MonitorChannelMapping.ForNumeric(numeric)) != 0;
            Editors[numeric].Enabled.IsEnabled = monitored;
            if (!monitored) { notice.Reset(AlarmTransitionReason.Disabled); }
        }
    }
    internal IReadOnlyDictionary<MonitorNumeric, LimitEditor> Editors { get; }
    internal IReadOnlyList<AlarmLifecycleJournal> Lifecycles => _notices.Values.Select(n => n.Lifecycle).ToArray();

    internal AdditionalMeasurementLimits(DesktopLocalization? localization = null)
    {
        var shared = localization ?? new DesktopLocalization();
        Editors = MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => new LimitEditor(d, shared));
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
            if ((_monitoredChannels & MonitorChannelMapping.ForNumeric(descriptor.Numeric)) == 0)
            { _notices[descriptor.Numeric].Reset(AlarmTransitionReason.Disabled); continue; }
            var limits = Editors[descriptor.Numeric].Limits;
            MonitorNotice? notice;
            MeasurementConfirmationTiming? timing = null;
            try { timing = Editors[descriptor.Numeric].Confirmation.Read(); }
            catch (ArgumentException) { _notices[descriptor.Numeric].Reset(AlarmTransitionReason.InvalidConfiguration); }
            notice = timing is null
                ? new(descriptor.Id + "-settings", MonitorNoticeLevel.Info, descriptor.Label + " 确认时间无效：请输入 0–600 秒，最多三位小数")
                { Message = new("alarm.confirmationInvalid", descriptor.LabelMessage) }
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
        internal CheckBox Enabled { get; } = new() { IsChecked = false };
        internal NumericUpDown CriticalLow { get; }
        internal NumericUpDown WarningLow { get; }
        internal NumericUpDown WarningHigh { get; }
        internal NumericUpDown CriticalHigh { get; }
        internal AlarmConfirmationEditor Confirmation { get; }
        internal LimitEditor(MeasurementLimitDescriptor descriptor, DesktopLocalization localization)
        {
            _divisor = descriptor.Divisor; Spacing = 6;
            localization.Bind(Enabled, ContentControl.ContentProperty, "alarm.limitEnabled");
            localization.Bind(Enabled, AutomationProperties.NameProperty, text => text.Format("alarm.limitEnabledName", text.GetString(descriptor.LabelKey)));
            Children.Add(Enabled);
            var thresholds = AlarmConfirmationEditor.CreateFieldsPanel();
            CriticalLow = Add(AlarmText.BoundaryKeys[0], descriptor.TeachingDefaults.CriticalLow!.Value);
            WarningLow = Add(AlarmText.BoundaryKeys[1], descriptor.TeachingDefaults.WarningLow!.Value);
            WarningHigh = Add(AlarmText.BoundaryKeys[2], descriptor.TeachingDefaults.WarningHigh!.Value);
            CriticalHigh = Add(AlarmText.BoundaryKeys[3], descriptor.TeachingDefaults.CriticalHigh!.Value);
            Confirmation = new(descriptor, localization);
            Confirmation.SetThresholdContent(thresholds);
            Children.Add(Confirmation);
            if (descriptor.Numeric is MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean)
            { Children.Add(DesktopInformationPages.Help("pressure-alarm-validation")); }
            NumericUpDown Add(string boundary, int value)
            {
                string Label(Monitor.Application.Localization.ITextLocalizer text) => text.Format("alarm.thresholdRow",
                    text.GetString(descriptor.LabelKey), text.GetString(boundary), AlarmText.Unit(text, descriptor));
                var number = new NumericUpDown
                {
                    Value = (decimal)value / _divisor,
                    Minimum = (decimal)descriptor.Minimum / _divisor,
                    Maximum = (decimal)descriptor.Maximum / _divisor,
                    Increment = .25m,
                    Width = 220,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                localization.Bind(number, AutomationProperties.NameProperty, Label);
                var row = new StackPanel { Spacing = 6, Width = 220, Margin = new Thickness(0, 0, 24, 12) };
                var caption = new TextBlock();
                localization.Bind(caption, TextBlock.TextProperty, Label);
                row.Children.Add(caption);
                row.Children.Add(number);
                thresholds.Children.Add(row);
                return number;
            }
        }
        internal MeasurementLimits Limits => new(Enabled.IsChecked == true, Units(CriticalLow), Units(WarningLow), Units(WarningHigh), Units(CriticalHigh));
        private int? Units(NumericUpDown control) => control.Value is { } value ? checked((int)(value * _divisor)) : null;
    }
}
