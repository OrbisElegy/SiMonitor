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
        Children.Add(Text("每次仅显示一条。Info / Notice / Warning / Critical 每轮分别占用 2 / 4 / 6 / 8 秒，同级轮流显示；更高新级别立即优先。声音始终跟随最高活动级别。"));
        Children.Add(HeartRateEnabled);
        Row("Critical HR 下限（bpm）", CriticalLowHeartRate); Row("Warning HR 下限（bpm）", WarningLowHeartRate);
        Row("Warning HR 上限（bpm）", WarningHeartRate); Row("Critical HR 上限（bpm）", CriticalHeartRate);
        Children.Add(Text("阈值即时生效：Critical 下限 < Warning 下限 < Warning 上限 < Critical 上限。低于下限或高于上限时提示；默认值仅作教学设置。只比较有效实测 HR；当前不包含窒息诊断、锁存、确认及声音限时暂停。"));
        Row("提示与声音联调（明确标为测试）", TestLevel);
        Row("联调闪烁数值", TestNumeric);
        Children.Add(NoticeColorEnabled);
        Children.Add(Text("报警数值背景每秒闪烁一次。关闭 Notice 颜色仅取消蓝色着色，提示文字与声音继续保留。"));
        Children.Add(Text("联调不代表患者异常，不伪造探头脱落或更改测量值。声音须在声音页单独启用。"));
        Children.Add(InfoTone);
        Row("Info 单声间隔（秒）", InfoInterval); Row("Notice 三联音组间隔（秒）", NoticeInterval);
        Row("Warning（3+2）×2 组间隔（秒）", WarningInterval); Row("Critical 单声间隔（秒）", CriticalInterval);
        Children.Add(Text("间隔从每组起点计算；音色和时序为可调教学实现。"));
    }
    internal MonitorSoundTiming Timing => new(InfoTone.IsChecked == true, Milliseconds(InfoInterval), Milliseconds(NoticeInterval), Milliseconds(WarningInterval), Milliseconds(CriticalInterval));
    internal IEnumerable<MonitorNotice> Notices(Monitor.Application.Measurements.LiveMeasurementSnapshot snapshot)
    {
        if (HeartRateEnabled.IsChecked == true)
        {
            if (WarningHeartRate.Value is not { } warning || CriticalHeartRate.Value is not { } critical || WarningLowHeartRate.Value is not { } warningLow || CriticalLowHeartRate.Value is not { } criticalLow ||
                criticalLow >= warningLow || warningLow >= warning || warning >= critical)
            { yield return new("hr-settings", MonitorNoticeLevel.Info, "HR 提示设置无效：须满足 Critical 下限 < Warning 下限 < Warning 上限 < Critical 上限"); }
            else if (snapshot.HeartRate.Status == Monitor.Application.Measurements.WaveformMeasurementStatus.Valid && snapshot.HeartRate.MilliBeatsPerMinute is { } rate)
            {
                if (rate < criticalLow * 1000) { yield return new("hr-low", MonitorNoticeLevel.Critical, "HR 极低") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate < warningLow * 1000) { yield return new("hr-low", MonitorNoticeLevel.Warning, "HR 低") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate > critical * 1000) { yield return new("hr-high", MonitorNoticeLevel.Critical, "HR 极高") { Numeric = MonitorNumeric.HeartRate }; }
                else if (rate > warning * 1000) { yield return new("hr-high", MonitorNoticeLevel.Warning, "HR 高") { Numeric = MonitorNumeric.HeartRate }; }
            }
        }
        if (TestLevel.SelectedIndex > 0)
        {
            yield return new("explicit-test", (MonitorNoticeLevel)(TestLevel.SelectedIndex - 1), "测试提示 · " + (MonitorNoticeLevel)(TestLevel.SelectedIndex - 1))
            { Numeric = TestNumeric.SelectedIndex > 0 ? (MonitorNumeric)(TestNumeric.SelectedIndex - 1) : null };
        }
    }
    private static int Milliseconds(NumericUpDown number) => checked((int)((number.Value ?? number.Minimum) * 1000));
    private void Row(string label, Control control)
    {
        var row = new StackPanel { Spacing = 4 }; row.Children.Add(Text(label)); row.Children.Add(control); Children.Add(row);
        AutomationProperties.SetName(control, label);
    }
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum) => new()
    { Value = value, Minimum = minimum, Maximum = maximum, Increment = .25m, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
