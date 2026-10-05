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
    internal const string InvalidTimingReason = "AlarmConfirmation.InvalidTiming";
    private readonly DesktopLocalization _localization;
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly MeasurementConfirmationTiming _defaults;
    private readonly MonitorNumeric _numeric;
    internal NumericUpDown[] Fields { get; }
    internal TabControl Groups { get; } = new() { Padding = new Thickness(0) };

    internal AlarmConfirmationEditor(MeasurementLimitDescriptor descriptor, DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        _numeric = descriptor.Numeric;
        _defaults = MeasurementConfirmationTiming.DefaultFor(descriptor.Numeric);
        var trigger = CreateFieldsPanel();
        var recovery = CreateFieldsPanel();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Groups);
        Content = panel;
        _localization.Bind(Groups, AutomationProperties.NameProperty, text => text.Format("alarm.groupsName", text.GetString(descriptor.LabelKey)));
        var fields = new List<NumericUpDown>();
        string[] boundaryKeys = _numeric == MonitorNumeric.SpO2 ? AlarmText.BoundaryKeys[..2] : AlarmText.BoundaryKeys;
        foreach (string boundary in boundaryKeys)
        {
            foreach (string operation in new[] { "alarm.operationTrigger", "alarm.operationRecovery" })
            {
                string Label(Monitor.Application.Localization.ITextLocalizer text) =>
                    text.Format("alarm.timingField", text.GetString(boundary), text.GetString(operation));
                var field = CreateTimingField();
                _localization.Bind(field, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", text.GetString(descriptor.LabelKey), Label(text)));
                var page = operation == "alarm.operationTrigger" ? trigger : recovery;
                var row = new StackPanel { Spacing = 4, Width = 220, Margin = new Thickness(0, 0, 24, 8) };
                var caption = new TextBlock();
                _localization.Bind(caption, TextBlock.TextProperty, Label);
                row.Children.Add(caption);
                row.Children.Add(field);
                page.Children.Add(row);
                fields.Add(field);
            }
        }
        Fields = fields.ToArray();
        var triggerPage = AddPage("alarm.pageTrigger", trigger);
        var recoveryPage = AddPage("alarm.pageRecovery", recovery);
        Groups.SelectedIndex = 0;
        var reset = new Button();
        _localization.Bind(reset, ContentControl.ContentProperty, "alarm.resetParameterConfirmation");
        reset.Click += (_, _) => Restore(_defaults);
        var timingActions = new WrapPanel { Orientation = Orientation.Horizontal };
        timingActions.Children.Add(reset);
        var effect = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
            Margin = new Thickness(12, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        _localization.Bind(effect, TextBlock.TextProperty, "alarm.parameterChangeEffect");
        timingActions.Children.Add(effect);
        timingActions.Children.Add(DesktopInformationPages.Help("alarm-confirmation-timing"));
        panel.Children.Add(timingActions);
        panel.Children.Add(_error);
        Groups.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, Groups))
            { timingActions.IsVisible = ReferenceEquals(Groups.SelectedItem, triggerPage) || ReferenceEquals(Groups.SelectedItem, recoveryPage); }
        };
        Restore(_defaults);
        foreach (var field in Fields)
        {
            field.ValueChanged += (_, _) =>
            {
                try { _ = Read(); _error.Text = null; _error.IsVisible = false; }
                catch (ArgumentException)
                {
                    _localization.Bind(_error, TextBlock.TextProperty, "alarm.timingInvalid");
                    _error.IsVisible = true;
                }
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
        Groups.Items.Insert(0, Page("alarm.pageThreshold", content));
        Groups.SelectedIndex = 0;
    }

    internal TabItem AddPage(string title, Control content)
    {
        var page = Page(title, content);
        Groups.Items.Add(page);
        return page;
    }

    // title is a catalog key or verbatim text.
    private TabItem Page(string title, Control content)
    {
        var page = new TabItem
        {
            Content = content,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 20, 0),
            FontSize = 14,
            MinHeight = 44
        };
        _localization.BindLabel(page, TabItem.HeaderProperty, title);
        return page;
    }

    internal MeasurementConfirmationTiming Read()
    {
        int Milliseconds(int index) => ReadMilliseconds(Fields[index]);
        return new(new(Milliseconds(0), Milliseconds(1)), new(Milliseconds(2), Milliseconds(3)),
            Fields.Length == 4 ? new(0, 0) : new(Milliseconds(4), Milliseconds(5)),
            Fields.Length == 4 ? new(0, 0) : new(Milliseconds(6), Milliseconds(7)));
    }

    internal static NumericUpDown CreateTimingField() => new()
    {
        Minimum = 0,
        Maximum = BoundaryConfirmationTiming.MaximumMilliseconds / 1000m,
        Value = 0,
        Increment = .1m,
        Width = 220,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    internal static int ReadMilliseconds(NumericUpDown field)
    {
        if (field.Value is not { } seconds || seconds < 0 || seconds > 600 ||
            seconds * 1000 != decimal.Truncate(seconds * 1000))
        { throw new ArgumentException(InvalidTimingReason, nameof(field)); }
        return checked((int)(seconds * 1000));
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
