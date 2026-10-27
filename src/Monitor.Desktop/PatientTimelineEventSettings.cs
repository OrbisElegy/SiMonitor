// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Draft controls own no running clock. Restart validates and snapshots the complete list.
internal sealed class PatientTimelineEventSettings : UserControl
{
    private readonly DesktopLocalization _localization;
    private readonly bool _relativeTime;
    private readonly bool _showStatus;
    private readonly List<EventEditor> _editors = [];
    private IReadOnlyList<PatientTimelineEvent> _applied = [];
    private readonly ContentControl _detail = new();
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap };
    private bool _restoring;
    private bool _refreshing;
    private bool _graphInspector;
    internal event Action? DraftChanged;
    internal event Action? SelectionChanged;
    internal ListBox Events { get; } = new() { MaxHeight = 160, MinHeight = 48 };
    internal Button Add { get; } = Button();
    internal Button Remove { get; } = Button();
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock Progress { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal IReadOnlyList<EventEditor> Editors => _editors.AsReadOnly();
    internal EventEditor? SelectedEditor => Events.SelectedItem is ListBoxItem { Tag: EventEditor editor } ? editor : null;

    internal PatientTimelineEventSettings(DesktopLocalization localization, bool relativeTime = false, bool showStatus = true)
    {
        _localization = localization;
        _relativeTime = relativeTime;
        _showStatus = showStatus;
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        panel.Children.Add(Text(relativeTime ? "timeline.branchHint" : "timeline.restartHint"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        localization.Bind(Add, ContentControl.ContentProperty, "timeline.add");
        localization.Bind(Remove, ContentControl.ContentProperty, "timeline.remove");
        buttons.Children.Add(Add);
        buttons.Children.Add(Remove);
        panel.Children.Add(buttons);
        localization.Bind(Events, AutomationProperties.NameProperty, "timeline.events");
        localization.Bind(_empty, TextBlock.TextProperty, "timeline.empty");
        panel.Children.Add(_empty);
        panel.Children.Add(Events);
        panel.Children.Add(Status);
        panel.Children.Add(Progress);
        panel.Children.Add(_detail);
        Content = SettingsScroll.Create(panel);
        Add.Click += (_, _) => AddEvent();
        Remove.Click += (_, _) => RemoveSelected();
        Events.SelectionChanged += (_, _) =>
        {
            if (_refreshing) { return; }
            ShowSelected();
            Remove.IsEnabled = SelectedEditor is not null;
            SelectionChanged?.Invoke();
        };
        RefreshList();
    }

    // Each node opens at its top; the scroll position belonged to the previously shown node.
    private void ShowSelected()
    {
        if (ReferenceEquals(_detail.Content, SelectedEditor)) { return; }
        _detail.Content = SelectedEditor;
        if (Content is ScrollViewer scroll) { scroll.Offset = default; }
    }

    internal void AddEvent(bool atStart = false)
    {
        if (_editors.Count >= PatientTimelineEvent.MaximumEvents) { return; }
        NormalizeLegacyOrder();
        var editor = new EventEditor(_localization, _relativeTime)
        {
            EventName = { Text = _localization.Format("timeline.newEvent", _editors.Count + 1) },
            Trigger = { SelectedIndex = (int)PatientTimelineTrigger.AfterPrevious },
            StartSeconds = { Value = 30 },
            DurationSeconds = { Value = 0 }
        };
        int insertion = atStart ? 0 : SelectedEditor is { } selected ? _editors.IndexOf(selected) + 1 : _editors.Count;
        AddEditor(editor);
        _editors.Remove(editor);
        _editors.Insert(insertion, editor);
        RefreshList(editor);
        Changed();
    }

    internal void RemoveSelected()
    {
        if (SelectedEditor is not { } editor) { return; }
        int index = _editors.IndexOf(editor);
        _editors.Remove(editor);
        RefreshList(_editors.Count == 0 ? null : _editors[Math.Min(index, _editors.Count - 1)]);
        Changed();
    }

    internal void SelectEditor(EventEditor? editor)
    {
        RefreshList(editor);
        SelectionChanged?.Invoke();
    }

    internal void UseGraphInspector()
    {
        _graphInspector = true;
        var panel = (StackPanel)((ScrollViewer)Content!).Content!;
        foreach (var child in panel.Children) { child.IsVisible = ReferenceEquals(child, _detail); }
    }

    internal void MoveSelected(int direction)
    {
        if (SelectedEditor is not { } editor) { return; }
        NormalizeLegacyOrder();
        int index = _editors.IndexOf(editor);
        int destination = index + direction;
        if (destination < 0 || destination >= _editors.Count) { return; }
        _editors.RemoveAt(index);
        _editors.Insert(destination, editor);
        RefreshList(editor);
        Changed();
    }

    private void NormalizeLegacyOrder()
    {
        if (_editors.Count == 0 || _editors.Any(editor => editor.Trigger.SelectedIndex != (int)PatientTimelineTrigger.TimelineTime)) { return; }
        var ordered = _editors.OrderBy(editor => editor.StartSeconds.Value ?? decimal.MaxValue).ToArray();
        decimal previous = 0;
        _restoring = true;
        foreach (var editor in ordered)
        {
            decimal at = editor.StartSeconds.Value ?? previous;
            editor.StartSeconds.Value = Math.Max(0, at - previous);
            editor.Trigger.SelectedIndex = (int)PatientTimelineTrigger.AfterPrevious;
            previous = at;
        }
        _editors.Clear();
        _editors.AddRange(ordered);
        _restoring = false;
    }

    internal IReadOnlyList<PatientTimelineEvent> Read() => PatientTimelineEvent.Snapshot(_editors.Select(editor => editor.Read()).ToArray());

    internal void Restore(IReadOnlyList<PatientTimelineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var editors = PatientTimelineEvent.Snapshot(events).Select(item =>
        {
            var editor = new EventEditor(_localization, _relativeTime);
            editor.Restore(item);
            return editor;
        }).ToArray();
        _restoring = true;
        _editors.Clear();
        foreach (var editor in editors) { AddEditor(editor); }
        RefreshList();
        _restoring = false;
        MarkApplied();
    }

    internal void MarkApplied(IReadOnlyList<PatientTimelineEvent>? events = null)
    {
        _applied = PatientTimelineEvent.Snapshot(events ?? Read());
        RefreshDraftStatus();
    }

    private void AddEditor(EventEditor editor)
    {
        _editors.Add(editor);
        editor.DraftChanged += () =>
        {
            if (_restoring) { return; }
            RefreshList(SelectedEditor ?? editor);
            Changed();
        };
    }

    private void Changed()
    {
        if (_restoring) { return; }
        RefreshDraftStatus();
        DraftChanged?.Invoke();
    }

    private void RefreshDraftStatus()
    {
        bool changed;
        try { changed = !Read().SequenceEqual(_applied); }
        catch (ArgumentException) { changed = true; }
        _localization.Bind(Status, TextBlock.TextProperty, text => changed ? text.GetString("timeline.draft") : "");
        Status.IsVisible = _showStatus && changed;
    }

    private void RefreshList(EventEditor? selected = null)
    {
        selected ??= SelectedEditor ?? _editors.FirstOrDefault();
        var ordered = _editors.All(editor => editor.Trigger.SelectedIndex == (int)PatientTimelineTrigger.TimelineTime)
            ? _editors.OrderBy(editor => editor.StartSeconds.Value ?? decimal.MaxValue).ToArray() : _editors.ToArray();
        var rows = ordered.Select(editor =>
        {
            var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _localization.Bind(caption, TextBlock.TextProperty, text => editor.IsValid
                ? text.Format("timeline.row", editor.StartSeconds.Value ?? 0, editor.EventName.Text)
                : text.Format("timeline.rowInvalid", editor.EventName.Text));
            var row = new ListBoxItem { Content = caption, Tag = editor, MinHeight = 44 };
            _localization.Bind(row, AutomationProperties.NameProperty, text => editor.IsValid
                ? text.Format("timeline.row", editor.StartSeconds.Value ?? 0, editor.EventName.Text)
                : text.Format("timeline.rowInvalid", editor.EventName.Text));
            return row;
        }).ToArray();
        _refreshing = true;
        Events.ItemsSource = rows;
        Events.SelectedItem = rows.FirstOrDefault(row => ReferenceEquals(row.Tag, selected));
        _refreshing = false;
        ShowSelected();
        _empty.IsVisible = _editors.Count == 0;
        Events.IsVisible = !_graphInspector && _editors.Count != 0;
        if (_graphInspector) { _empty.IsVisible = false; }
        Add.IsEnabled = _editors.Count < PatientTimelineEvent.MaximumEvents;
        Remove.IsEnabled = SelectedEditor is not null;
    }

    private TextBlock Text(string key)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }

    private static Button Button() => new() { MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center };

    internal sealed class EventEditor : StackPanel
    {
        private readonly DesktopLocalization _localization;
        private readonly bool _relativeTime;
        private readonly bool _entryState;
        private readonly Expander _treatmentSection;
        private readonly Label _startLabel = new() { Padding = new Thickness(0) };
        private readonly StackPanel _startField = new() { Spacing = 5, Margin = new Thickness(0, 0, 16, 4) };
        private readonly StackPanel _ventilationFields = new() { Spacing = 10, Margin = new Thickness(28, 0, 0, 0) };
        private readonly TextBlock _targetsSummary = Summary();
        private readonly TextBlock _templatesSummary = Summary();
        private readonly TextBlock _ventilationSummary = Summary();
        private readonly TextBlock _treatmentSummary = Summary();
        // Catalog key of the visible start label, which validation messages also name.
        private string _startKey = "timeline.wait";
        private bool _restoring;
        internal event Action? DraftChanged;
        internal TextBox EventName { get; } = new() { MaxLength = 80, MinHeight = 40 };
        internal ComboBox Trigger { get; } = Choice();
        internal PatientTimelineTreatmentSettings Treatment { get; }
        internal NumericUpDown StartSeconds { get; } = Number(0, 3600, 30, .2m);
        internal NumericUpDown DurationSeconds { get; } = Number(0, 3600, 10, .2m);
        internal Dictionary<string, TargetEditor> Targets { get; } = new(StringComparer.Ordinal);
        internal ComboBox EcgTemplate { get; } = Choice();
        internal ComboBox RespirationTemplate { get; } = Choice();
        internal ComboBox EjectionTemplate { get; } = Choice();
        internal ComboBox PlethTemplate { get; } = Choice();
        internal ComboBox CapnogramTemplate { get; } = Choice();
        internal CheckBox ChangeVentilation { get; } = new();
        internal NumericUpDown TidalVolume { get; } = Number(0, 1500, 450, 10);
        internal NumericUpDown DeadSpace { get; } = Number(0, 500, 150, 10);
        internal NumericUpDown InspiredOxygen { get; } = Number(10, 100, 21, 1);
        internal CheckBox AirwayOpen { get; } = new() { IsChecked = true };
        internal TextBlock Error { get; } = new() { Foreground = PatientTimelineSettings.CriticalText, TextWrapping = TextWrapping.Wrap };
        internal bool IsValid
        {
            get
            {
                try { if (_entryState) { ReadInitialState(); } else { Read(); } return true; }
                catch (ArgumentException) { return false; }
            }
        }

        internal EventEditor(DesktopLocalization localization, bool relativeTime = false, bool entryState = false)
        {
            _localization = localization;
            _relativeTime = relativeTime;
            _entryState = entryState;
            Spacing = 16;
            Treatment = new(localization);
            // Validation leads the form so a long, partly collapsed editor cannot hide it.
            Children.Add(PatientTimelineSettings.Notice(Error));
            var general = new StackPanel { Spacing = 12 };
            AddField(general, "timeline.name", EventName);
            localization.SetChoices(Trigger, "timeline.triggerTimeline", "timeline.triggerAfterPrevious", "timeline.triggerManual");
            Trigger.SelectedIndex = (int)PatientTimelineTrigger.AfterPrevious;
            AddField(general, "timeline.trigger", Trigger);
            var times = new WrapPanel { Orientation = Orientation.Horizontal };
            _startLabel.Target = StartSeconds;
            _startField.Children.Add(_startLabel);
            _startField.Children.Add(StartSeconds);
            times.Children.Add(_startField);
            AddField(times, "timeline.duration", DurationSeconds);
            general.Children.Add(times);
            if (!entryState) { Children.Add(general); }
            var targets = new StackPanel { Spacing = 10 };
            targets.Children.Add(Hint(entryState ? "timeline.initialStateHint" : "timeline.targetHint"));
            AddTarget("HeartRate", "timeline.heartRate", Number(30, 180, 75, 1));
            AddTarget("RespiratoryRate", "timeline.respiratoryRate", Number(6, 60, 16, 1));
            AddTarget("Saturation", "timeline.saturation", Number(0, 100, 98, .1m));
            AddTarget("EtCo2", "timeline.etco2", Number(5, 80, 40, 1));
            AddTarget("Abp", "timeline.abp", Number(50, 250, 120, 1), Number(20, 150, 80, 1));
            AddTarget("Pa", "timeline.pa", Number(10, 100, 25, 1), Number(6, 60, 10, 1));
            AddTarget("Cvp", "timeline.cvp", Number(-5, 30, 6, .1m));
            AddTarget("Nibp", "timeline.nibp", Number(0, 300, 120, 1), Number(0, 300, 80, 1), Number(0, 300, 93, 1));
            AddTarget("Temperature", "timeline.temperature", Number(0, 50, 36.5m, .1m));
            Children.Add(Section(entryState ? "timeline.initialState" : "timeline.targets", targets, _targetsSummary, expanded: true));
            var templates = new StackPanel { Spacing = 10 };
            templates.Children.Add(Hint("timeline.templateHint"));
            AddTemplate("generation.ecg", EcgTemplate, DesignPreviewSettings.EcgChoiceCount, DesignPreviewSettings.EcgTemplateKey);
            AddTemplate("generation.respiration", RespirationTemplate, DesignPreviewSettings.RespirationChoiceCount, DesignPreviewSettings.RespirationTemplateKey);
            AddTemplate("generation.ejection", EjectionTemplate, DesignPreviewSettings.EjectionChoiceCount, DesignPreviewSettings.EjectionTemplateKey);
            AddTemplate("generation.pleth", PlethTemplate, DesignPreviewSettings.PlethChoiceCount, DesignPreviewSettings.PlethTemplateKey);
            AddTemplate("generation.capnogram", CapnogramTemplate, DesignPreviewSettings.CapnogramChoiceCount, DesignPreviewSettings.CapnogramTemplateKey);
            Children.Add(Section("timeline.templates", templates, _templatesSummary));
            var ventilation = new StackPanel { Spacing = 10 };
            localization.Bind(ChangeVentilation, ContentControl.ContentProperty, "timeline.changeVentilation");
            ventilation.Children.Add(ChangeVentilation);
            ventilation.Children.Add(Hint("timeline.ventilationHint"));
            AddField(_ventilationFields, "oxygenation.tidalVolume", TidalVolume);
            AddField(_ventilationFields, "oxygenation.deadSpace", DeadSpace);
            AddField(_ventilationFields, "oxygenation.inspiredOxygen", InspiredOxygen);
            localization.Bind(AirwayOpen, ContentControl.ContentProperty, "oxygenation.airwayOpen");
            _ventilationFields.Children.Add(AirwayOpen);
            ventilation.Children.Add(_ventilationFields);
            Children.Add(Section("timeline.ventilation", ventilation, _ventilationSummary));
            _treatmentSection = Section("timeline.treatmentTab", Treatment, _treatmentSummary);
            if (!entryState) { Children.Add(_treatmentSection); }
            Trigger.SelectionChanged += (_, _) => Changed();
            Treatment.Changed += Changed;
            EventName.TextChanged += (_, _) => Changed();
            foreach (var field in new[] { StartSeconds, DurationSeconds, TidalVolume, DeadSpace, InspiredOxygen })
            { field.ValueChanged += (_, _) => Changed(); }
            AirwayOpen.IsCheckedChanged += (_, _) => Changed();
            ChangeVentilation.IsCheckedChanged += (_, _) =>
            {
                if (!_restoring && ChangeVentilation.IsChecked == true) { Targets["Saturation"].Enabled.IsChecked = false; }
                Changed();
            };
            Targets["Saturation"].Enabled.IsCheckedChanged += (_, _) =>
            {
                if (!_restoring && Targets["Saturation"].Enabled.IsChecked == true) { ChangeVentilation.IsChecked = false; }
            };
            Changed();

            void AddTarget(string id, string title, params NumericUpDown[] fields)
            {
                var target = new TargetEditor(localization, title, fields);
                Targets[id] = target;
                target.DraftChanged += Changed;
                targets.Children.Add(target);
            }

            void AddTemplate(string title, ComboBox field, int count, Func<int, string> key)
            {
                localization.SetChoices(field, Enumerable.Range(0, count).Select(key).Prepend("timeline.unchanged").ToArray());
                field.SelectedIndex = 0;
                AddField(templates, title, field);
                field.SelectionChanged += (_, _) => Changed();
            }
        }

        internal PatientTimelineEvent? ReadInitialState()
        {
            var item = ReadState();
            return item.Targets.IsEmpty && !item.ChangesTemplate && item.Ventilation is null ? null : item;
        }

        internal PatientTimelineEvent Read() => ReadState();

        private PatientTimelineEvent ReadState()
        {
            string name = EventName.Text?.Trim() ?? (_entryState ? "initial" : "");
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            { throw new ArgumentException("PatientTimeline.InvalidName"); }
            long startTimeNs = _entryState || Trigger.SelectedIndex == (int)PatientTimelineTrigger.Manual ? 0 : ReadTime(StartSeconds, _startKey);
            long durationNs = _entryState ? 0 : ReadTime(DurationSeconds, "timeline.duration");
            if (startTimeNs > PatientTimelineEvent.MaximumTimeNs - durationNs)
            { throw new ArgumentException("PatientTimeline.InvalidTime"); }
            int? Value(string key, int scale = 1) => Targets[key].Enabled.IsChecked == true ? Targets[key].Read(0, scale) : null;
            VascularPressureTarget? Pressure(string key)
            {
                if (Targets[key].Enabled.IsChecked != true) { return null; }
                int systolic = Targets[key].Read(0, 100), diastolic = Targets[key].Read(1, 100);
                if (systolic - diastolic < VascularPressureTarget.MinimumPulseCentiMmHg)
                { throw new ArgumentException("Physiology.PressureTargetPulseTooSmall"); }
                return new(systolic, diastolic);
            }
            var targets = new PatientVitalTargets
            {
                HeartRateBpm = Value("HeartRate"),
                RespiratoryRatePerMinute = Value("RespiratoryRate"),
                SaturationMilliPercent = Value("Saturation", 1000),
                EtCo2MmHg = Value("EtCo2"),
                Abp = Pressure("Abp"),
                Pa = Pressure("Pa"),
                CvpCentiMmHg = Value("Cvp", 100),
                Nibp = Targets["Nibp"].Enabled.IsChecked == true
                    ? new(Targets["Nibp"].Read(0, 1), Targets["Nibp"].Read(1, 1), Targets["Nibp"].Read(2, 1)) : null,
                TemperatureDeciCelsius = Value("Temperature", 10)
            };
            var item = new PatientTimelineEvent(name, startTimeNs, durationNs, targets)
            {
                Trigger = _entryState ? PatientTimelineTrigger.TimelineTime : (PatientTimelineTrigger)Trigger.SelectedIndex,
                Treatment = _entryState ? null : Treatment.Read(),
                EcgTemplateId = EcgTemplate.SelectedIndex > 0 ? DesignPreviewSettings.EcgTemplateKey(EcgTemplate.SelectedIndex - 1) : null,
                RespirationTemplate = Selected(RespirationTemplate),
                EjectionTemplate = Selected(EjectionTemplate),
                PlethTemplate = Selected(PlethTemplate),
                CapnogramTemplate = Selected(CapnogramTemplate),
                Ventilation = ChangeVentilation.IsChecked == true ? new VentilationTransportPlan(
                    DesignPreviewSettings.ReadVitalValue(TidalVolume, 1000, "oxygenation.tidalVolumeField"),
                    DesignPreviewSettings.ReadVitalValue(DeadSpace, 1000, "oxygenation.deadSpaceField"),
                    DesignPreviewSettings.ReadVitalValue(InspiredOxygen, 10000, "oxygenation.inspiredOxygenField"),
                    AirwayOpen.IsChecked == true) : null
            };
            if (!_entryState || !item.Targets.IsEmpty || item.ChangesTemplate || item.Ventilation is not null) { item.Validate(); }
            return item;
        }

        internal void RevealTreatment()
        {
            _treatmentSection.IsExpanded = true;
            Dispatcher.UIThread.Post(() => Treatment.BringIntoView(), DispatcherPriority.Loaded);
        }

        internal void Restore(PatientTimelineEvent item)
        {
            item.Validate();
            _restoring = true;
            EventName.Text = item.Name;
            Trigger.SelectedIndex = (int)item.Trigger;
            Treatment.Restore(item.Treatment);
            StartSeconds.Value = item.StartTimeNs / 1_000_000_000m;
            DurationSeconds.Value = item.DurationNs / 1_000_000_000m;
            Set("HeartRate", item.Targets.HeartRateBpm);
            Set("RespiratoryRate", item.Targets.RespiratoryRatePerMinute);
            Set("Saturation", item.Targets.SaturationMilliPercent / 1000m);
            Set("EtCo2", item.Targets.EtCo2MmHg);
            Set("Abp", item.Targets.Abp?.SystolicCentiMmHg / 100m, item.Targets.Abp?.DiastolicCentiMmHg / 100m);
            Set("Pa", item.Targets.Pa?.SystolicCentiMmHg / 100m, item.Targets.Pa?.DiastolicCentiMmHg / 100m);
            Set("Cvp", item.Targets.CvpCentiMmHg / 100m);
            Set("Nibp", item.Targets.Nibp?.SystolicMmHg, item.Targets.Nibp?.DiastolicMmHg, item.Targets.Nibp?.MeanMmHg);
            Set("Temperature", item.Targets.TemperatureDeciCelsius / 10m);
            int ecgIndex = item.EcgTemplateId is null ? -1 : Enumerable.Range(0, DesignPreviewSettings.EcgChoiceCount)
                .FirstOrDefault(index => DesignPreviewSettings.EcgTemplateKey(index) == item.EcgTemplateId, -1);
            if (item.EcgTemplateId is not null && ecgIndex < 0) { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
            EcgTemplate.SelectedIndex = ecgIndex + 1;
            RespirationTemplate.SelectedIndex = (item.RespirationTemplate ?? -1) + 1;
            EjectionTemplate.SelectedIndex = (item.EjectionTemplate ?? -1) + 1;
            PlethTemplate.SelectedIndex = (item.PlethTemplate ?? -1) + 1;
            CapnogramTemplate.SelectedIndex = (item.CapnogramTemplate ?? -1) + 1;
            ChangeVentilation.IsChecked = item.Ventilation is not null;
            if (item.Ventilation is { } ventilation)
            {
                TidalVolume.Value = ventilation.TidalVolumeMicrolitersBtps / 1000m;
                DeadSpace.Value = ventilation.DeadSpaceMicrolitersBtps / 1000m;
                InspiredOxygen.Value = ventilation.InspiredOxygenMillionths / 10000m;
                AirwayOpen.IsChecked = ventilation.AirwayOpen;
            }
            _restoring = false;
            Changed();

            void Set(string key, params decimal?[] values)
            {
                var target = Targets[key];
                target.Enabled.IsChecked = values[0] is not null;
                for (int index = 0; index < values.Length; index++)
                { if (values[index] is { } value) { target.Values[index].Value = value; } }
            }
        }

        private void Changed()
        {
            if (_restoring) { return; }
            // Manual nodes have no wait; the label names the timing the trigger actually uses.
            _startField.IsVisible = Trigger.SelectedIndex != (int)PatientTimelineTrigger.Manual;
            string startKey = Trigger.SelectedIndex == (int)PatientTimelineTrigger.AfterPrevious ? "timeline.wait"
                : _relativeTime ? "timeline.relativeStart" : "timeline.start";
            if (startKey != _startKey || _startLabel.Content is null)
            {
                _startKey = startKey;
                _localization.Bind(_startLabel, ContentControl.ContentProperty, startKey);
                _localization.Bind(StartSeconds, AutomationProperties.NameProperty, startKey);
            }
            _ventilationFields.IsVisible = ChangeVentilation.IsChecked == true;
            SetSummary(_targetsSummary, Targets.Values.Count(target => target.Enabled.IsChecked == true), counted: true);
            SetSummary(_templatesSummary, new[] { EcgTemplate, RespirationTemplate, EjectionTemplate, PlethTemplate, CapnogramTemplate }
                .Count(field => field.SelectedIndex > 0), counted: true);
            SetSummary(_ventilationSummary, ChangeVentilation.IsChecked == true ? 1 : 0, counted: false);
            SetSummary(_treatmentSummary, Treatment.Override.IsChecked == true ? 1 : 0, counted: false);
            try
            {
                if (_entryState) { ReadInitialState(); }
                else { Read(); }
                Error.IsVisible = false;
            }
            catch (ArgumentException error)
            {
                Error.IsVisible = true;
                _localization.Bind(Error, TextBlock.TextProperty, text => error is VitalValueException field
                    ? text.Format("validation.vitalField", DesktopLocalization.Label(text, field.FieldName), field.Minimum, field.Maximum, field.Step)
                    : text.GetString(ErrorKey(error)));
            }
            DraftChanged?.Invoke();
        }

        // Collapsed sections still show whether this node changes anything inside them.
        private void SetSummary(TextBlock summary, int count, bool counted) =>
            _localization.Bind(summary, TextBlock.TextProperty, text => count == 0 ? text.GetString("timeline.summaryUnchanged")
                : counted ? text.Format("timeline.summaryCount", count) : text.GetString("timeline.summaryChanged"));

        private static string ErrorKey(ArgumentException error) => error.Message switch
        {
            "ManualVitals.InvalidNibp" => "timeline.invalidNibp",
            "Physiology.PressureTargetPulseTooSmall" => "timeline.invalidPressure",
            "PatientTimeline.InvalidName" => "timeline.invalidName",
            "PatientTimeline.InvalidTime" => "timeline.invalidTime",
            "PatientTimeline.EmptyEvent" => "timeline.emptyEvent",
            _ => "timeline.invalid"
        };

        private static int? Selected(ComboBox field) => field.SelectedIndex > 0 ? field.SelectedIndex - 1 : null;
        private static long ReadTime(NumericUpDown field, string key)
        {
            int fifths = DesignPreviewSettings.ReadVitalValue(field, 5, key);
            return fifths * 200_000_000L;
        }

        private Expander Section(string key, Control content, TextBlock summary, bool expanded = false)
        {
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _localization.Bind(title, TextBlock.TextProperty, key);
            var header = new Grid { ColumnDefinitions = new("*,Auto") };
            header.Children.Add(title);
            Grid.SetColumn(summary, 1);
            header.Children.Add(summary);
            var section = new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = expanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _localization.Bind(section, AutomationProperties.NameProperty, key);
            return section;
        }

        private static TextBlock Summary() => new()
        {
            FontSize = 12,
            Foreground = DesktopFluentStyle.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        private TextBlock Hint(string key)
        {
            var text = new TextBlock { FontSize = 12, Foreground = DesktopFluentStyle.SecondaryText, TextWrapping = TextWrapping.Wrap };
            _localization.Bind(text, TextBlock.TextProperty, key);
            return text;
        }

        private void AddField(Panel parent, string key, Control field)
        {
            var group = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 16, 4) };
            var label = new Label { Target = field, Padding = new Thickness(0) };
            _localization.Bind(label, ContentControl.ContentProperty, key);
            _localization.Bind(field, AutomationProperties.NameProperty, key);
            group.Children.Add(label);
            group.Children.Add(field);
            parent.Children.Add(group);
        }

        private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = increment,
            FormatString = "0.###",
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        private static ComboBox Choice() => new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    // Values appear once their target is selected; unselected targets leave the vital sign unchanged.
    internal sealed class TargetEditor : StackPanel
    {
        private readonly string _key;
        private readonly WrapPanel _fields = new() { Orientation = Orientation.Horizontal, IsVisible = false };
        internal event Action? DraftChanged;
        internal CheckBox Enabled { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
        internal IReadOnlyList<NumericUpDown> Values { get; }

        internal TargetEditor(DesktopLocalization localization, string key, NumericUpDown[] fields)
        {
            _key = key;
            Values = fields;
            Spacing = 6;
            localization.Bind(Enabled, ContentControl.ContentProperty, key);
            for (int index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                field.Width = 140;
                field.Margin = fields.Length == 1 ? new Thickness(12, 0, 0, 0) : new Thickness(0, 0, 12, 4);
                string labelKey = fields.Length == 1 ? key : index switch
                { 0 => "manual.systolic", 1 => "manual.diastolic", _ => "manual.mean" };
                localization.Bind(field, AutomationProperties.NameProperty, labelKey);
                var group = new StackPanel { Spacing = 4 };
                if (fields.Length > 1)
                {
                    var label = new Label { Target = field, Padding = new Thickness(0) };
                    localization.Bind(label, ContentControl.ContentProperty, labelKey);
                    group.Children.Add(label);
                }
                group.Children.Add(field);
                _fields.Children.Add(group);
                field.ValueChanged += (_, _) => DraftChanged?.Invoke();
            }
            if (fields.Length == 1)
            {
                var compact = new Grid { ColumnDefinitions = new("*,Auto") };
                compact.Children.Add(Enabled);
                Grid.SetColumn(_fields, 1);
                compact.Children.Add(_fields);
                Children.Add(compact);
            }
            else
            {
                _fields.Margin = new Thickness(28, 0, 0, 0);
                Children.Add(Enabled);
                Children.Add(_fields);
            }
            Enabled.IsCheckedChanged += (_, _) =>
            {
                _fields.IsVisible = Enabled.IsChecked == true;
                DraftChanged?.Invoke();
            };
        }

        internal int Read(int index, int scale) => DesignPreviewSettings.ReadVitalValue(Values[index], scale, _key);
    }
}
