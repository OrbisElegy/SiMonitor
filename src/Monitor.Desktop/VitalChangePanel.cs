// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Scenarios;

namespace Monitor.Desktop;

// Queues vital sign changes for the running simulation. Actions take effect
// immediately on the scheduler; nothing here is applied with the settings footer.
internal sealed class VitalChangePanel : StackPanel
{
    internal sealed record Row(string Key, CheckBox Toggle, (VitalSign Sign, NumericUpDown Field, string FieldKey)[] Fields, int Scale);

    private readonly DesktopLocalization _localization;
    private Func<VitalChangeScheduler?> _scheduler = () => null;
    private Action _changed = () => { };
    private VitalChangeEntry[] _entries = [];
    private long _simulationTimeNs;
    internal Row[] Rows { get; }
    internal NumericUpDown Duration { get; } = Field(0, 3600, 60, 1);
    internal ComboBox Trigger { get; } = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown Delay { get; } = Field(0, 3600, 30, 1);
    internal Button Add { get; } = new() { MinHeight = 44, Classes = { "accent" } };
    internal ListBox Queue { get; } = new() { MinHeight = 120, MaxHeight = 320 };
    internal Button TriggerSelected { get; } = new() { MinHeight = 44 };
    internal Button RemoveSelected { get; } = new() { MinHeight = 44 };
    internal Button StopAll { get; } = new() { MinHeight = 44 };
    internal TextBlock Current { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock QueueLabel { get; } = new();

    internal VitalChangePanel(DesktopLocalization localization)
    {
        _localization = localization;
        Spacing = 14;
        Rows =
        [
            Single("vitalChanges.heartRate", VitalSign.HeartRateBpm, 1, 1),
            Single("vitalChanges.respiratoryRate", VitalSign.RespiratoryRatePerMinute, 1, 1),
            Single("vitalChanges.spo2", VitalSign.SpO2MilliPercent, 1000, .1m),
            Single("vitalChanges.etco2", VitalSign.EtCo2MmHg, 1, 1),
            Pair("vitalChanges.abp", VitalSign.AbpSystolicCentiMmHg, "vitalChanges.abpSystolic", VitalSign.AbpDiastolicCentiMmHg, "vitalChanges.abpDiastolic"),
            Pair("vitalChanges.pa", VitalSign.PaSystolicCentiMmHg, "vitalChanges.paSystolic", VitalSign.PaDiastolicCentiMmHg, "vitalChanges.paDiastolic"),
            Single("vitalChanges.cvp", VitalSign.CvpCentiMmHg, 100, .5m),
        ];
        Children.Add(Label("vitalChanges.targetsIntro"));
        foreach (var row in Rows)
        {
            _localization.Bind(row.Toggle, ContentControl.ContentProperty, row.Key);
            var line = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Toggle.MinWidth = 260;
            line.Children.Add(row.Toggle);
            foreach (var (_, field, fieldKey) in row.Fields)
            {
                field.Margin = new Avalonia.Thickness(0, 0, 12, 0);
                _localization.Bind(field, AutomationProperties.NameProperty, fieldKey);
                line.Children.Add(field);
            }
            void Refresh()
            {
                foreach (var (_, field, _) in row.Fields) { field.IsEnabled = row.Toggle.IsEnabled && row.Toggle.IsChecked == true; }
            }
            row.Toggle.IsCheckedChanged += (_, _) => Refresh();
            row.Toggle.PropertyChanged += (_, args) => { if (args.Property == IsEnabledProperty) { Refresh(); } };
            Refresh();
            Children.Add(line);
        }
        AddField("vitalChanges.duration", Duration);
        _localization.SetChoices(Trigger, "vitalChanges.triggerManual", "vitalChanges.triggerDelay", "vitalChanges.triggerPrevious");
        Trigger.SelectedIndex = 0;
        AddField("vitalChanges.trigger", Trigger);
        AddField("vitalChanges.delay", Delay);
        Trigger.SelectionChanged += (_, _) => Delay.IsEnabled = Trigger.SelectedIndex == (int)VitalChangeTrigger.AfterDelay;
        Delay.IsEnabled = false;
        _localization.Bind(Add, ContentControl.ContentProperty, "vitalChanges.add");
        Children.Add(Add);
        Children.Add(Status);
        Children.Add(DesktopInformationPages.Help("vital-changes"));
        _localization.Bind(QueueLabel, TextBlock.TextProperty, "vitalChanges.queue");
        Children.Add(QueueLabel);
        _localization.Bind(Queue, AutomationProperties.NameProperty, "vitalChanges.queue");
        Children.Add(Queue);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (button, key) in new[] { (TriggerSelected, "vitalChanges.triggerSelected"), (RemoveSelected, "vitalChanges.removeSelected"), (StopAll, "vitalChanges.stopAll") })
        {
            button.Margin = new Avalonia.Thickness(0, 0, 12, 0);
            _localization.Bind(button, ContentControl.ContentProperty, key);
            actions.Children.Add(button);
        }
        Children.Add(actions);
        Children.Add(Current);
        Add.Click += (_, _) => AddChange();
        TriggerSelected.Click += (_, _) => Act(scheduler => scheduler.Trigger(SelectedId()!.Value));
        RemoveSelected.Click += (_, _) => Act(scheduler => scheduler.Remove(SelectedId()!.Value));
        StopAll.Click += (_, _) => Act(scheduler => scheduler.Stop());
        Queue.SelectionChanged += (_, _) => RefreshActions();
        AttachedToVisualTree += (_, _) =>
        {
            _localization.LocaleChanged += Refresh;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => _localization.LocaleChanged -= Refresh;
        Refresh();
    }

    // The window owns the scheduler; settings pages are recreated on reset.
    internal void Connect(Func<VitalChangeScheduler?> scheduler, Action changed)
    {
        _scheduler = scheduler;
        _changed = changed;
        Reset();
    }

    // After settings are applied: offer the signs the applied source can vary, starting at their values.
    internal void Reset()
    {
        var scheduler = _scheduler();
        var values = scheduler?.ValuesAt(_simulationTimeNs);
        foreach (var row in Rows)
        {
            bool supported = values is not null && row.Fields.All(field => values.ContainsKey(field.Sign));
            row.Toggle.IsEnabled = supported;
            if (!supported) { row.Toggle.IsChecked = false; }
            if (!supported || row.Toggle.IsChecked == true) { continue; }
            foreach (var (sign, field, _) in row.Fields) { field.Value = values![sign] / (decimal)row.Scale; }
        }
        Refresh();
    }

    internal void Refresh()
    {
        var scheduler = _scheduler();
        int? selected = SelectedId();
        var entries = scheduler?.Events.Where(entry => entry.State is VitalChangeState.Waiting or VitalChangeState.Active).ToArray() ?? [];
        Queue.ItemsSource = entries.Select(Describe).ToArray();
        Queue.SelectedIndex = Array.FindIndex(entries, entry => entry.Id == selected);
        _entries = entries;
        Current.Text = scheduler is null ? "" : _localization.Format("vitalChanges.current", Values(scheduler.ValuesAt(_simulationTimeNs)));
        RefreshActions();
    }

    internal void ShowTime(long simulationTimeNs)
    {
        _simulationTimeNs = simulationTimeNs;
        Refresh();
    }

    internal void SetStatus(string key, params object?[] arguments) => _localization.Bind(Status, TextBlock.TextProperty, key, arguments);

    private int? SelectedId() => Queue.SelectedIndex >= 0 && Queue.SelectedIndex < _entries.Length ? _entries[Queue.SelectedIndex].Id : null;

    private void RefreshActions()
    {
        var entry = Queue.SelectedIndex >= 0 && Queue.SelectedIndex < _entries.Length ? _entries[Queue.SelectedIndex] : null;
        TriggerSelected.IsEnabled = entry?.State == VitalChangeState.Waiting;
        RemoveSelected.IsEnabled = entry is not null;
        StopAll.IsEnabled = _scheduler() is { } scheduler && (scheduler.IsChanging || _entries.Length > 0);
        Add.IsEnabled = _scheduler() is not null && Rows.Any(row => row.Toggle.IsEnabled);
    }

    private void AddChange()
    {
        if (_scheduler() is not { } scheduler) { return; }
        try
        {
            var targets = new Dictionary<VitalSign, int>();
            foreach (var row in Rows.Where(row => row.Toggle.IsEnabled && row.Toggle.IsChecked == true))
            {
                foreach (var (sign, field, fieldKey) in row.Fields)
                { targets[sign] = DesignPreviewSettings.ReadVitalValue(field, row.Scale, fieldKey); }
                if (row.Fields.Length == 2 && targets[row.Fields[0].Sign] - targets[row.Fields[1].Sign] < Monitor.Simulation.Authoring.VascularPressureTarget.MinimumPulseCentiMmHg)
                {
                    SetStatus("vitalChanges.pulseTooSmall");
                    return;
                }
            }
            if (targets.Count == 0)
            {
                SetStatus("vitalChanges.noSigns");
                return;
            }
            var trigger = (VitalChangeTrigger)Trigger.SelectedIndex;
            long duration = DesignPreviewSettings.ReadVitalValue(Duration, 1, "vitalChanges.durationField") * 1_000_000_000L;
            long delay = trigger == VitalChangeTrigger.AfterDelay
                ? DesignPreviewSettings.ReadVitalValue(Delay, 1, "vitalChanges.delayField") * 1_000_000_000L : 0;
            int id = scheduler.Add(new(targets, duration, trigger, delay));
            SetStatus("vitalChanges.added", id.ToString(CultureInfo.InvariantCulture));
        }
        catch (VitalValueException exception)
        {
            _localization.Bind(Status, TextBlock.TextProperty, text => text.Format("validation.vitalField",
                DesktopLocalization.Label(text, exception.FieldName), exception.Minimum, exception.Maximum, exception.Step));
        }
        catch (InvalidOperationException exception) when (exception.Message == "VitalChange.QueueFull")
        { SetStatus("vitalChanges.queueFull"); }
        _changed();
        Refresh();
    }

    private void Act(Action<VitalChangeScheduler> action)
    {
        if (_scheduler() is not { } scheduler) { return; }
        try { action(scheduler); }
        // The selection may have finished or been removed since the list was drawn.
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException) { }
        SetStatus("vitalChanges.updated");
        _changed();
        Refresh();
    }

    private string Describe(VitalChangeEntry entry)
    {
        string trigger = entry.State == VitalChangeState.Active ? _localization.Get("vitalChanges.stateActive") : entry.Event.Trigger switch
        {
            VitalChangeTrigger.Manual => _localization.Get("vitalChanges.stateManual"),
            VitalChangeTrigger.AfterDelay => _localization.Format("vitalChanges.stateDelay", entry.Event.DelayNs / 1_000_000_000),
            _ => _localization.Get("vitalChanges.statePrevious"),
        };
        return _localization.Format("vitalChanges.entry", entry.Id, trigger, Values(entry.Event.Targets), entry.Event.DurationNs / 1_000_000_000);
    }

    private string Values(IReadOnlyDictionary<VitalSign, int> values)
    {
        var parts = new List<string>();
        foreach (var row in Rows)
        {
            if (!row.Fields.All(field => values.ContainsKey(field.Sign))) { continue; }
            string number = string.Join("/", row.Fields.Select(field => (values[field.Sign] / (decimal)row.Scale).ToString("0.#", CultureInfo.InvariantCulture)));
            parts.Add(_localization.Format(row.Key + "Value", number));
        }
        return string.Join(_localization.Get("alarm.listSeparator"), parts);
    }

    private static Row Single(string key, VitalSign sign, int scale, decimal increment)
    {
        var (minimum, maximum) = VitalChangeScheduler.Range(sign);
        return new(key, new CheckBox(), [(sign, Field(minimum / (decimal)scale, maximum / (decimal)scale, minimum / (decimal)scale, increment), key)], scale);
    }

    private static Row Pair(string key, VitalSign systolic, string systolicKey, VitalSign diastolic, string diastolicKey)
    {
        NumericUpDown Make(VitalSign sign)
        {
            var (minimum, maximum) = VitalChangeScheduler.Range(sign);
            return Field(minimum / 100m, maximum / 100m, minimum / 100m, 1);
        }
        return new(key, new CheckBox(), [(systolic, Make(systolic), systolicKey), (diastolic, Make(diastolic), diastolicKey)], 100);
    }

    private TextBlock Label(string key)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _localization.Bind(label, TextBlock.TextProperty, key);
        return label;
    }

    private void AddField(string key, Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(Label(key));
        Children.Add(control);
        _localization.Bind(control, AutomationProperties.NameProperty, key);
    }

    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        FormatString = "0.#",
        Width = 150,
        HorizontalAlignment = HorizontalAlignment.Left
    };
}
