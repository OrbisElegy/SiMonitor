// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class AlarmConfirmationEditor : UserControl
{
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly MeasurementConfirmationTiming _defaults;
    private readonly MonitorNumeric _numeric;
    internal NumericUpDown[] Fields { get; }
    internal TabControl Groups { get; } = new() { Padding = new Thickness(0) };

    internal AlarmConfirmationEditor(MeasurementLimitDescriptor descriptor)
    {
        _numeric = descriptor.Numeric;
        _defaults = MeasurementConfirmationTiming.DefaultFor(descriptor.Numeric);
        var trigger = CreateFieldsPanel();
        var recovery = CreateFieldsPanel();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Groups);
        Content = panel;
        AutomationProperties.SetName(Groups, descriptor.Label + "报警参数分组");
        var fields = new List<NumericUpDown>();
        string[] boundaryLabels = _numeric == MonitorNumeric.SpO2
            ? ["Critical 下限", "Warning 下限"]
            : ["Critical 下限", "Warning 下限", "Warning 上限", "Critical 上限"];
        foreach (string boundary in boundaryLabels)
        {
            foreach (string operation in new[] { "触发", "恢复" })
            {
                string label = boundary + " " + operation + "（秒）";
                var field = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = BoundaryConfirmationTiming.MaximumMilliseconds / 1000m,
                    Increment = .1m,
                    Width = 220,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                AutomationProperties.SetName(field, descriptor.Label + " " + label);
                var page = operation == "触发" ? trigger : recovery;
                var row = new StackPanel { Spacing = 4, Width = 220, Margin = new Thickness(0, 0, 24, 8) };
                row.Children.Add(new TextBlock { Text = label });
                row.Children.Add(field);
                page.Children.Add(row);
                fields.Add(field);
            }
        }
        Fields = fields.ToArray();
        AddPage("触发确认", trigger);
        AddPage("恢复确认", recovery);
        Groups.SelectedIndex = 0;
        var reset = new Button { Content = "恢复此参数默认确认时间" };
        reset.Click += (_, _) => Restore(_defaults);
        var timingActions = new WrapPanel { Orientation = Orientation.Horizontal };
        timingActions.Children.Add(reset);
        timingActions.Children.Add(new TextBlock
        {
            Text = "修改立即生效，并重新确认此参数。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
            Margin = new Thickness(12, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        });
        timingActions.Children.Add(DesktopInformationPages.Help("alarm-confirmation-timing"));
        panel.Children.Add(timingActions);
        panel.Children.Add(_error);
        Groups.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, Groups))
            { timingActions.IsVisible = Groups.SelectedItem is TabItem { Header: "触发确认" or "恢复确认" }; }
        };
        Restore(_defaults);
        foreach (var field in Fields)
        {
            field.ValueChanged += (_, _) =>
            {
                try { _ = Read(); _error.Text = null; _error.IsVisible = false; }
                catch (ArgumentException) { _error.Text = "触发／恢复时间须为 0–600 秒，最多三位小数；0 表示立即确认。"; _error.IsVisible = true; }
            };
        }
    }

    internal static WrapPanel CreateFieldsPanel() => new()
    {
        Orientation = Orientation.Horizontal,
        MaxWidth = 520,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    internal void SetThresholdContent(Control content)
    {
        Groups.Items.Insert(0, Page("阈值", content));
        Groups.SelectedIndex = 0;
    }

    internal void AddPage(string title, Control content) => Groups.Items.Add(Page(title, content));

    private static TabItem Page(string title, Control content) => new()
    {
        Header = title,
        Content = content,
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 20, 0),
        FontSize = 14,
        MinHeight = 44
    };

    internal MeasurementConfirmationTiming Read()
    {
        int Milliseconds(int index)
        {
            if (Fields[index].Value is not { } seconds || seconds < 0 || seconds > 600 ||
                seconds * 1000 != decimal.Truncate(seconds * 1000))
            { throw new ArgumentException("确认时间须为 0–600 秒，最多三位小数。"); }
            return checked((int)(seconds * 1000));
        }
        return new(new(Milliseconds(0), Milliseconds(1)), new(Milliseconds(2), Milliseconds(3)),
            Fields.Length == 4 ? new(0, 0) : new(Milliseconds(4), Milliseconds(5)),
            Fields.Length == 4 ? new(0, 0) : new(Milliseconds(6), Milliseconds(7)));
    }

    internal void Restore(MeasurementConfirmationTiming timing)
    {
        timing.ValidateFor(_numeric);
        BoundaryConfirmationTiming[] boundaries = [timing.CriticalLow, timing.WarningLow, timing.WarningHigh, timing.CriticalHigh];
        for (int index = 0; index < Fields.Length / 2; index++)
        {
            Fields[index * 2].Value = boundaries[index].TriggerMilliseconds / 1000m;
            Fields[index * 2 + 1].Value = boundaries[index].RecoveryMilliseconds / 1000m;
        }
    }
}
