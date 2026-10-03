// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class MonitorAlertSettings : StackPanel
{
    internal CheckBox HeartRateEnabled { get; } = new() { Content = "启用实测 HR 上下限提示", IsChecked = false };
    internal NumericUpDown WarningLowHeartRate { get; } = Number(50, 1, 299);
    internal NumericUpDown CriticalLowHeartRate { get; } = Number(40, 1, 298);
    internal NumericUpDown WarningHeartRate { get; } = Number(120, 20, 300);
    internal NumericUpDown CriticalHeartRate { get; } = Number(180, 21, 350);
    internal CheckBox SpO2Enabled { get; } = new() { Content = "启用实测 SpO₂ 下限提示", IsChecked = false };
    internal NumericUpDown WarningSpO2 { get; } = Number(92, 1, 100);
    internal NumericUpDown CriticalSpO2 { get; } = Number(85, 1, 99);
    internal CheckBox NoExpirationEnabled { get; } = new() { Content = "启用 CO₂ 持续未检出呼吸提示", IsChecked = false };
    internal NumericUpDown NoExpirationSeconds { get; } = new() { Minimum = 5, Maximum = 120, Value = 20, Increment = 1, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    internal AdditionalMeasurementLimits AdditionalLimits { get; } = new();
    internal ComboBox TestLevel { get; } = new() { ItemsSource = new[] { "关闭联调提示", "Info · 测试", "Notice · 测试", "Warning · 测试", "Critical · 测试" }, SelectedIndex = 0, MinWidth = 220 };
    internal CheckBox InfoTone { get; } = new() { Content = "Info 使用稀疏单声（默认静音）" };
    internal CheckBox NoticeColorEnabled { get; } = new() { Content = "显示 Notice 蓝色提示与数值闪烁", IsChecked = true };
    internal ComboBox TestNumeric { get; } = new()
    {
        ItemsSource = new[] { "仅顶部提示", "HR", "RR · RESP", "SpO₂", "PR", "EtCO₂", "RR · CO₂", "ABP 平均压", "PA 平均压", "CVP 平均压" },
        SelectedIndex = 0,
        MinWidth = 220
    };
    internal NumericUpDown InfoInterval { get; } = Number(30, 5, 120);
    internal NumericUpDown NoticeInterval { get; } = Number(10, 1.5m, 60);
    internal NumericUpDown WarningInterval { get; } = Number(5, 3.5m, 60);
    internal NumericUpDown CriticalInterval { get; } = Number(1.5m, .25m, 2);
    internal MonitorAlertSettings()
    {
        Margin = new Thickness(20); Spacing = 12;
        Children.Add(Text("提示与声音 · 本地教学设置"));
        Children.Add(DesktopInformationPages.Help("topic-11"));
        Children.Add(HeartRateEnabled);
        Row("Critical HR 下限（bpm）", CriticalLowHeartRate); Row("Warning HR 下限（bpm）", WarningLowHeartRate);
        Row("Warning HR 上限（bpm）", WarningHeartRate); Row("Critical HR 上限（bpm）", CriticalHeartRate);
        Children.Add(DesktopInformationPages.Help("topic-12"));
        Children.Add(SpO2Enabled);
        Row("Critical SpO₂ 下限（%）", CriticalSpO2); Row("Warning SpO₂ 下限（%）", WarningSpO2);
        Children.Add(DesktopInformationPages.Help("topic-13"));
        foreach (var descriptor in MeasuredLimitNotice.Descriptors)
        {
            Children.Add(AdditionalLimits.Editors[descriptor.Numeric]);
            if (descriptor.Numeric == MonitorNumeric.EtCo2)
            { Children.Add(NoExpirationEnabled); Row("CO₂ 未检出呼吸时限（秒）", NoExpirationSeconds); }
        }
        Children.Add(DesktopInformationPages.Help("topic-14"));
        if (ProductIdentity.DevelopmentFeatures)
        {
            Row("提示与声音联调（明确标为测试）", TestLevel);
            Row("联调闪烁数值", TestNumeric);
        }
        Children.Add(NoticeColorEnabled);
        Children.Add(DesktopInformationPages.Help("settings-detail-10"));
        if (ProductIdentity.DevelopmentFeatures) { Children.Add(DesktopInformationPages.Help("settings-detail-11")); }
        Children.Add(InfoTone);
        Row("Info 单声间隔（秒）", InfoInterval); Row("Notice 三联音组间隔（秒）", NoticeInterval);
        Row("Warning（3+2）×2 组间隔（秒）", WarningInterval); Row("Critical 单声间隔（秒）", CriticalInterval);
        Children.Add(Text("间隔从每组起点计算；音色和时序为可调教学实现。"));
    }
    internal MonitorAlarmPreferences CapturePreferences()
    {
        static int? Read(NumericUpDown field, int scale)
        {
            if (field.Value is null) { return null; }
            return DesignPreviewSettings.ReadVitalValue(field, scale, "报警阈值");
        }
        var result = new MonitorAlarmPreferences(
            new(HeartRateEnabled.IsChecked == true, Read(CriticalLowHeartRate, 1000), Read(WarningLowHeartRate, 1000), Read(WarningHeartRate, 1000), Read(CriticalHeartRate, 1000)),
            SpO2Enabled.IsChecked == true, Read(WarningSpO2, 1000), Read(CriticalSpO2, 1000),
            NoExpirationEnabled.IsChecked == true, Read(NoExpirationSeconds, 1),
            MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d =>
            {
                var editor = AdditionalLimits.Editors[d.Numeric];
                return new MeasurementLimits(editor.Enabled.IsChecked == true, Read(editor.CriticalLow, d.Divisor),
                    Read(editor.WarningLow, d.Divisor), Read(editor.WarningHigh, d.Divisor), Read(editor.CriticalHigh, d.Divisor));
            }), NoticeColorEnabled.IsChecked == true);
        result.Validate(); return result;
    }
    internal void RestorePreferences(MonitorAlarmPreferences preferences)
    {
        preferences.Validate();
        AdditionalLimits.Reset();
        HeartRateEnabled.IsChecked = preferences.HeartRate.Enabled;
        CriticalLowHeartRate.Value = preferences.HeartRate.CriticalLow / 1000m;
        WarningLowHeartRate.Value = preferences.HeartRate.WarningLow / 1000m;
        WarningHeartRate.Value = preferences.HeartRate.WarningHigh / 1000m;
        CriticalHeartRate.Value = preferences.HeartRate.CriticalHigh / 1000m;
        SpO2Enabled.IsChecked = preferences.SpO2Enabled;
        WarningSpO2.Value = preferences.SpO2Warning / 1000m; CriticalSpO2.Value = preferences.SpO2Critical / 1000m;
        NoExpirationEnabled.IsChecked = preferences.NoExpirationEnabled; NoExpirationSeconds.Value = preferences.NoExpirationSeconds;
        NoticeColorEnabled.IsChecked = preferences.NoticeColorEnabled;
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            var saved = preferences.Additional[d.Numeric]; var editor = AdditionalLimits.Editors[d.Numeric];
            editor.Enabled.IsChecked = saved.Enabled;
            editor.CriticalLow.Value = saved.CriticalLow / (decimal)d.Divisor; editor.WarningLow.Value = saved.WarningLow / (decimal)d.Divisor;
            editor.WarningHigh.Value = saved.WarningHigh / (decimal)d.Divisor; editor.CriticalHigh.Value = saved.CriticalHigh / (decimal)d.Divisor;
        }
    }
    internal MonitorSoundTiming Timing => new(InfoTone.IsChecked == true, Milliseconds(InfoInterval), Milliseconds(NoticeInterval), Milliseconds(WarningInterval), Milliseconds(CriticalInterval));
    internal IEnumerable<MonitorNotice> Notices(Monitor.Application.Measurements.LiveMeasurementSnapshot snapshot)
    {
        if (HeartRateEnabled.IsChecked == true)
        {
            if (WarningHeartRate.Value is not { } warning || CriticalHeartRate.Value is not { } critical || WarningLowHeartRate.Value is not { } warningLow || CriticalLowHeartRate.Value is not { } criticalLow ||
                criticalLow >= warningLow || warningLow >= warning || warning >= critical)
            { yield return new("hr-settings", MonitorNoticeLevel.Info, "ECG HR 提示设置无效：须满足 Critical 下限 < Warning 下限 < Warning 上限 < Critical 上限"); }
            else if (snapshot.HeartRate.Status == Monitor.Application.Measurements.WaveformMeasurementStatus.Valid && snapshot.HeartRate.MilliBeatsPerMinute is { } rate)
            {
                if (rate < criticalLow * 1000) { yield return new("hr-low", MonitorNoticeLevel.Critical, "ECG HR 极低") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate < warningLow * 1000) { yield return new("hr-low", MonitorNoticeLevel.Warning, "ECG HR 低") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate > critical * 1000) { yield return new("hr-high", MonitorNoticeLevel.Critical, "ECG HR 极高") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate > warning * 1000) { yield return new("hr-high", MonitorNoticeLevel.Warning, "ECG HR 高") { Numeric = MonitorNumeric.HeartRate }; }
            }
        }
        var saturationNotice = SpO2LimitNotice.Evaluate(SpO2Enabled.IsChecked == true,
            MilliPercent(WarningSpO2), MilliPercent(CriticalSpO2), snapshot.SpO2);
        if (saturationNotice is not null) { yield return saturationNotice; }
        foreach (var notice in AdditionalLimits.Notices(snapshot)) { yield return notice; }
        int? delay = NoExpirationSeconds.Value is { } seconds && seconds == decimal.Truncate(seconds) ? checked((int)seconds) : null;
        if (NoExpirationNotice.Evaluate(NoExpirationEnabled.IsChecked == true, delay, snapshot.SampleTimeNs, snapshot.Capnography.Activity) is { } absence)
        { yield return absence; }
        if (ProductIdentity.DevelopmentFeatures && TestLevel.SelectedIndex > 0)
        {
            yield return new("explicit-test", (MonitorNoticeLevel)(TestLevel.SelectedIndex - 1), "测试提示 · " + (MonitorNoticeLevel)(TestLevel.SelectedIndex - 1))
            { Numeric = TestNumeric.SelectedIndex > 0 ? (MonitorNumeric)(TestNumeric.SelectedIndex - 1) : null };
        }
    }
    private static int Milliseconds(NumericUpDown number) => checked((int)((number.Value ?? number.Minimum) * 1000));
    private static int? MilliPercent(NumericUpDown number) => number.Value is { } value ? checked((int)(value * 1000)) : null;
    private void Row(string label, Control control)
    {
        var row = new StackPanel { Spacing = 4 }; row.Children.Add(Text(label)); row.Children.Add(control); Children.Add(row);
        AutomationProperties.SetName(control, label);
    }
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum) => new()
    { Value = value, Minimum = minimum, Maximum = maximum, Increment = .25m, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
