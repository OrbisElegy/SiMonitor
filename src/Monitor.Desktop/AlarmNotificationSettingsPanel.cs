// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Localization;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Event editors are hosted by their parameter pages; this page keeps the default and an overview.
internal sealed class AlarmNotificationSettingsPanel : StackPanel
{
    private readonly DesktopLocalization _localization;
    internal ComboBox Mode { get; } = new()
    {
        SelectedIndex = 0,
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    internal IReadOnlyDictionary<string, ConditionEditor> Editors { get; }
    internal IReadOnlyDictionary<MonitorNumeric, Button> Overview => _overviewButtons;
    internal AlarmPlaybackMode EffectiveMode { get; private set; }
    internal event Action? ModeChanged;
    internal event Action<MonitorNumeric>? ParameterRequested;
    private readonly TextBlock _errors = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly string[] _ids;
    private readonly Dictionary<string, string> _summaries = new(StringComparer.Ordinal);
    private readonly StackPanel _overview = new() { Spacing = 4 };
    private readonly Dictionary<MonitorNumeric, Button> _overviewButtons = [];
    private readonly List<(string Title, string[] Ids, TextBlock Summary, Button Row)> _overviewRows = [];
    private static readonly bool[] LowOnly = [true];
    private static readonly bool[] BothDirections = [true, false];
    private bool _restoring;
    private readonly Dictionary<string, AlarmNotificationSettings> _effective = new(StringComparer.Ordinal);

    internal AlarmNotificationSettingsPanel(IReadOnlyList<AlarmLifecycleJournal> journals, DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Spacing = 8;
        var labels = MeasuredLimitNotice.Descriptors.Prepend(MeasuredLimitNotice.SpO2Descriptor)
            .Prepend(MeasuredLimitNotice.HeartRateDescriptor)
            .SelectMany(d => (d.Numeric == MonitorNumeric.SpO2 ? LowOnly : BothDirections)
                .Select(low => (Id: d.Id + (low ? "-low" : "-high"), Label: AlarmText.ConditionLabel(d, low))))
            .Append((Id: "co2-no-expiration", Label: new TextMessage("alarm.noExpirationLabel"))).ToArray();
        _ids = labels.Select(l => l.Id).ToArray();
        Editors = labels.ToDictionary(l => l.Id, l => new ConditionEditor(l.Label, _localization), StringComparer.Ordinal);
        _localization.SetChoices(Mode, "alarm.modeContinuous", "alarm.modeNotifications");
        _localization.Bind(Mode, AutomationProperties.NameProperty, "alarm.modeName");
        Children.Add(Text("alarm.modeLabel"));
        Children.Add(Mode);
        Children.Add(Text("alarm.modeNote"));
        var heading = Text("alarm.eventSounds");
        heading.FontWeight = FontWeight.SemiBold;
        heading.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(heading);
        Children.Add(_overview);
        Children.Add(_errors);
        Children.Add(DesktopInformationPages.Help("alarm-notification-settings"));
        foreach (var (id, editor) in Editors)
        {
            var journal = journals.Single(j => j.Conditions.Any(c => c.ConditionId == id));
            _effective[id] = AlarmNotificationSettings.Default;
            journal.ConfigureNotifications(id, _effective[id].ToPolicy(EffectiveMode));
            editor.SetDefaultMode(EffectiveMode);
            editor.Changed += () =>
            {
                if (_restoring) { return; }
                try
                {
                    var settings = editor.Read();
                    journal.ConfigureNotifications(id, settings.ToPolicy(EffectiveMode));
                    journal.Attention.Configure(id, settings.LatchingMode);
                    _effective[id] = settings;
                    ModeChanged?.Invoke();
                }
                catch (ArgumentException) { } // Invalid draft preserves the last valid runtime policy.
                RefreshErrors();
            };
        }
        Mode.SelectionChanged += (_, _) =>
        {
            if (_restoring) { return; }
            if (Mode.SelectedIndex is 0 or 1)
            {
                EffectiveMode = (AlarmPlaybackMode)Mode.SelectedIndex;
                foreach (var (id, editor) in Editors)
                {
                    journals.Single(j => j.Conditions.Any(c => c.ConditionId == id)).ConfigureNotifications(id, _effective[id].ToPolicy(EffectiveMode));
                    editor.SetDefaultMode(EffectiveMode);
                }
                ModeChanged?.Invoke();
            }
            RefreshErrors();
        };
        _localization.LocaleChanged += RefreshErrors;
        RefreshErrors();
    }

    // Moves the parameter's event editors into a page and adds its overview row.
    // title is a catalog key or verbatim text.
    internal Control CreateEventPage(MonitorNumeric numeric, string title, params string[] ids)
    {
        var page = new StackPanel { Spacing = 12 };
        foreach (string id in ids)
        {
            var section = new StackPanel { Spacing = 4 };
            var direction = Text(DirectionKey(id));
            direction.FontWeight = FontWeight.SemiBold;
            section.Children.Add(direction);
            section.Children.Add(Editors[id]);
            page.Children.Add(section);
        }
        page.Children.Add(DesktopInformationPages.Help("alarm-notification-settings"));
        var name = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _localization.BindLabel(name, TextBlock.TextProperty, title);
        var summary = new TextBlock { Foreground = DesktopFluentStyle.SecondaryText, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var content = new Grid { ColumnDefinitions = new("120,*,16") };
        content.Children.Add(name);
        Grid.SetColumn(summary, 1);
        content.Children.Add(summary);
        Grid.SetColumn(chevron, 2);
        content.Children.Add(chevron);
        var row = new Button
        {
            Content = content,
            MinHeight = 44,
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        row.Click += (_, _) => ParameterRequested?.Invoke(numeric);
        _overview.Children.Add(row);
        _overviewButtons[numeric] = row;
        _overviewRows.Add((title, ids, summary, row));
        RefreshErrors();
        return page;
    }

    internal string SummaryFor(string id) => _summaries[id];

    private static string DirectionKey(string id) =>
        id.EndsWith("-low", StringComparison.Ordinal) ? "alarm.directionLow" :
        id.EndsWith("-high", StringComparison.Ordinal) ? "alarm.directionHigh" : "alarm.directionNoExpiration";

    private void RefreshErrors()
    {
        string[] invalid = Editors.Where(e => !e.Value.IsValid).Select(e => e.Value.Label).ToArray();
        _errors.Text = Mode.SelectedIndex is not (0 or 1) ? _localization.Get("alarm.modeInvalid") :
            invalid.Length == 0 ? null : _localization.Format("alarm.eventsInvalid", string.Join(_localization.Get("alarm.listSeparator"), invalid));
        _errors.IsVisible = _errors.Text is not null;
        foreach (string id in _ids)
        {
            var settings = _effective[id];
            string duration = _localization.Get(settings.ToPolicy(EffectiveMode).SoundDuration == AlarmSoundDuration.Continuous ? "alarm.continuous" : "alarm.notifications");
            string source = _localization.Get(settings.SoundMode == AlarmSoundMode.Inherit ? "alarm.sourceDefault" : "alarm.sourceIndependent");
            string error = Editors[id].IsValid ? "" : _localization.Get("alarm.needsFixSuffix");
            _summaries[id] = _localization.Format("alarm.eventSummary", duration, source, error);
        }
        foreach (var (title, ids, summary, row) in _overviewRows)
        {
            summary.Text = string.Join("  ·  ", ids.Select(id => _localization.Get(DirectionKey(id)) + " " + _summaries[id]));
            AutomationProperties.SetName(row, _localization.Format("alarm.eventRowName", DesktopLocalization.Label(_localization.Current, title), summary.Text));
        }
    }

    internal (AlarmPlaybackMode Mode, IReadOnlyDictionary<string, AlarmNotificationSettings> Overrides) Read()
    {
        if (Mode.SelectedIndex is not (0 or 1)) { throw new ArgumentException("AlarmNotification.InvalidDraft"); }
        var settings = Editors.ToDictionary(e => e.Key, e => e.Value.Read(), StringComparer.Ordinal);
        return ((AlarmPlaybackMode)Mode.SelectedIndex,
            settings.Where(e => e.Value != AlarmNotificationSettings.Default).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));
    }

    internal void Restore(MonitorAlarmPreferences preferences, IReadOnlyList<AlarmLifecycleJournal> journals)
    {
        preferences.Validate();
        _restoring = true;
        try
        {
            foreach (var (id, editor) in Editors)
            {
                var settings = preferences.NotificationFor(id);
                editor.Restore(settings);
                journals.Single(j => j.Conditions.Any(c => c.ConditionId == id)).Attention.Configure(id, settings.LatchingMode);
                _effective[id] = settings;
                editor.SetDefaultMode(preferences.PlaybackMode);
                journals.Single(j => j.Conditions.Any(c => c.ConditionId == id)).ConfigureNotifications(id, settings.ToPolicy(preferences.PlaybackMode));
            }
            Mode.SelectedIndex = (int)preferences.PlaybackMode;
            EffectiveMode = preferences.PlaybackMode;
        }
        finally { _restoring = false; }
        RefreshErrors();
        ModeChanged?.Invoke();
    }

    private TextBlock Text(string key)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }

    internal sealed class ConditionEditor : StackPanel
    {
        private readonly DesktopLocalization _localization;
        private readonly Func<ITextLocalizer, string> _label;
        // The condition label in the selected language.
        internal string Label => _label(_localization.Current);
        internal IReadOnlyList<RadioButton> SoundChoices { get; }
        internal int SelectedSoundMode
        {
            get => SoundChoices.Select((choice, index) => choice.IsChecked == true ? index : -1).Max();
            set
            {
                bool wasRestoring = _restoring;
                _restoring = true;
                try
                {
                    for (int index = 0; index < SoundChoices.Count; index++) { SoundChoices[index].IsChecked = index == value; }
                }
                finally { _restoring = wasRestoring; }
                Edited();
            }
        }
        internal Expander Advanced { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8), MinHeight = 44 };
        private readonly StackPanel _shortSettings = new() { Spacing = 8 };
        private readonly TextBlock _defaultDescription = new();
        private readonly TextBlock _error = new()
        {
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        internal NumericUpDown RepeatSeconds { get; } = Number(0, 0);
        internal CheckBox LatchUntilAcknowledged { get; } = new() { IsChecked = false };
        internal CheckBox ReminderEnabled { get; } = new() { IsChecked = false };
        internal NumericUpDown ReminderSeconds { get; } = Number(30, .001m);
        internal event Action? Changed;
        internal bool IsValid { get { try { _ = Read(); return true; } catch (ArgumentException) { return false; } } }
        private bool _restoring;
        private AlarmPlaybackMode _defaultMode;

        // label is verbatim text.
        internal ConditionEditor(string label, DesktopLocalization? localization = null) : this(_ => label, localization) { }

        internal ConditionEditor(TextMessage label, DesktopLocalization? localization = null) : this(label.Render, localization) { }

        private ConditionEditor(Func<ITextLocalizer, string> label, DesktopLocalization? localization)
        {
            _localization = localization ?? new DesktopLocalization();
            _label = label;
            Spacing = 4;
            var choices = new WrapPanel { Orientation = Orientation.Horizontal };
            string[] titles = ["alarm.soundInherit", "alarm.notifications", "alarm.continuous"];
            string[] descriptions = ["", "alarm.notificationsDescription", "alarm.continuousDescription"];
            var buttons = new List<RadioButton>();
            for (int index = 0; index < titles.Length; index++)
            {
                var content = new StackPanel { Spacing = 2 };
                var title = new TextBlock { FontWeight = FontWeight.SemiBold };
                _localization.Bind(title, TextBlock.TextProperty, titles[index]);
                content.Children.Add(title);
                if (index == 0) { content.Children.Add(_defaultDescription); }
                else
                {
                    var description = new TextBlock { FontSize = 12 };
                    _localization.Bind(description, TextBlock.TextProperty, descriptions[index]);
                    content.Children.Add(description);
                }
                var choice = new RadioButton
                {
                    Content = content,
                    MinHeight = 44,
                    Width = 196,
                    IsChecked = index == 0
                };
                string key = titles[index];
                if (index > 0) { _localization.Bind(choice, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", _label(text), text.GetString(key))); }
                choices.Children.Add(choice);
                buttons.Add(choice);
            }
            SoundChoices = buttons;
            Children.Add(choices);
            var fields = AlarmConfirmationEditor.CreateFieldsPanel();
            Add("alarm.repeatSuppression", RepeatSeconds);
            Add("alarm.reminderInterval", ReminderSeconds);
            _localization.Bind(LatchUntilAcknowledged, ContentControl.ContentProperty, "alarm.latch");
            _localization.Bind(ReminderEnabled, ContentControl.ContentProperty, "alarm.reminder");
            _localization.Bind(_error, TextBlock.TextProperty, "alarm.eventInvalid");
            _shortSettings.Children.Add(ReminderEnabled);
            _shortSettings.Children.Add(fields);
            var advancedContent = new StackPanel { Spacing = 8 };
            advancedContent.Children.Add(LatchUntilAcknowledged);
            advancedContent.Children.Add(_shortSettings);
            var reset = new Button { MinHeight = 44 };
            _localization.Bind(reset, ContentControl.ContentProperty, "alarm.resetEvent");
            _localization.Bind(reset, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", _label(text), text.GetString("alarm.resetEventName")));
            reset.Click += (_, _) => Restore(AlarmNotificationSettings.Default);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(reset);
            var effect = new TextBlock { Margin = new Thickness(12, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            _localization.Bind(effect, TextBlock.TextProperty, "alarm.eventChangeEffect");
            actions.Children.Add(effect);
            advancedContent.Children.Add(actions);
            Advanced.Content = advancedContent;
            Children.Add(Advanced);
            Children.Add(_error);
            void Add(string key, NumericUpDown field)
            {
                var row = new StackPanel { Spacing = 4, Width = 220, Margin = new Thickness(0, 0, 24, 8) };
                var caption = new TextBlock();
                _localization.Bind(caption, TextBlock.TextProperty, key);
                row.Children.Add(caption);
                row.Children.Add(field);
                fields.Children.Add(row);
                _localization.Bind(field, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", _label(text), text.GetString(key)));
            }
            _localization.Bind(LatchUntilAcknowledged, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", _label(text), text.GetString("alarm.latch")));
            LatchUntilAcknowledged.IsCheckedChanged += (_, _) => Edited();
            _localization.Bind(ReminderEnabled, AutomationProperties.NameProperty, text => text.Format("alarm.qualified", _label(text), text.GetString("alarm.reminder")));
            foreach (var choice in SoundChoices)
            {
                choice.IsCheckedChanged += (_, _) =>
                {
                    if (!_restoring && SelectedSoundMode >= 0) { Edited(); }
                };
            }
            RepeatSeconds.ValueChanged += (_, _) => Edited();
            ReminderSeconds.ValueChanged += (_, _) => Edited();
            ReminderEnabled.IsCheckedChanged += (_, _) => Edited();
            ReminderSeconds.IsEnabled = false;
            _localization.LocaleChanged += UpdateAvailability;
        }

        private static NumericUpDown Number(decimal value, decimal minimum) => new()
        { Minimum = minimum, Maximum = 3600, Value = value, Increment = .1m, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };

        internal AlarmNotificationSettings Read()
        {
            if (SelectedSoundMode is < 0 or > 2) { throw new ArgumentException("AlarmNotification.InvalidDraft"); }
            static int Milliseconds(NumericUpDown field, decimal minimum)
            {
                if (field.Value is not { } seconds || seconds < minimum || seconds > 3600 || seconds * 1000 != decimal.Truncate(seconds * 1000))
                { throw new ArgumentException("AlarmNotification.InvalidDraft"); }
                return checked((int)(seconds * 1000));
            }
            return new(Milliseconds(RepeatSeconds, 0), ReminderEnabled.IsChecked == true, Milliseconds(ReminderSeconds, .001m))
            { SoundMode = (AlarmSoundMode)SelectedSoundMode, LatchingMode = LatchUntilAcknowledged.IsChecked == true ? AlarmLatchingMode.UntilAcknowledged : AlarmLatchingMode.NonLatching };
        }

        internal void SetDefaultMode(AlarmPlaybackMode mode)
        {
            _defaultMode = mode;
            UpdateAvailability();
        }

        private void UpdateAvailability()
        {
            bool shortSound = SelectedSoundMode == 1 || SelectedSoundMode == 0 && _defaultMode == AlarmPlaybackMode.Notifications;
            bool invalid = !IsValid;
            _defaultDescription.Text = _localization.Get(_defaultMode == AlarmPlaybackMode.Notifications ? "alarm.currentNotifications" : "alarm.currentContinuous");
            AutomationProperties.SetName(SoundChoices[0], _localization.Format("alarm.inheritName", Label, _defaultDescription.Text));
            _shortSettings.IsVisible = shortSound || invalid;
            RepeatSeconds.IsEnabled = ReminderEnabled.IsEnabled = shortSound || invalid;
            ReminderSeconds.IsEnabled = invalid || shortSound && ReminderEnabled.IsChecked == true;
            Advanced.Header = _localization.Get(shortSound || invalid ? "alarm.advancedShort" : "alarm.advancedMore");
            _error.IsVisible = invalid;
            if (invalid) { Advanced.IsExpanded = true; }
        }

        private void Edited()
        {
            UpdateAvailability();
            if (!_restoring) { Changed?.Invoke(); }
        }

        internal void Restore(AlarmNotificationSettings settings)
        {
            settings.Validate();
            _restoring = true;
            try
            {
                RepeatSeconds.Value = settings.RepeatSuppressionMilliseconds / 1000m;
                ReminderSeconds.Value = settings.ReminderMilliseconds / 1000m;
                ReminderEnabled.IsChecked = settings.ReminderEnabled;
                LatchUntilAcknowledged.IsChecked = settings.LatchingMode == AlarmLatchingMode.UntilAcknowledged;
                SelectedSoundMode = (int)settings.SoundMode;
            }
            finally { _restoring = false; }
            Edited();
        }
    }
}
