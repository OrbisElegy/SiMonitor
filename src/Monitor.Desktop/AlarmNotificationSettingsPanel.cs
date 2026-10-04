// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Event editors are hosted by their parameter pages; this page keeps the default and an overview.
internal sealed class AlarmNotificationSettingsPanel : StackPanel
{
    internal ComboBox Mode { get; } = new()
    {
        ItemsSource = new[] { "长警报（持续播放）", "短警报（每次一组）" },
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

    internal AlarmNotificationSettingsPanel(IReadOnlyList<AlarmLifecycleJournal> journals)
    {
        Spacing = 8;
        var labels = MeasuredLimitNotice.Descriptors.Prepend(MeasuredLimitNotice.SpO2Descriptor)
            .Prepend(MeasuredLimitNotice.HeartRateDescriptor)
            .SelectMany(d => (d.Numeric == MonitorNumeric.SpO2 ? LowOnly : BothDirections)
                .Select(low => (Id: d.Id + (low ? "-low" : "-high"), Label: d.Label + (low ? " · 低限" : " · 高限"))))
            .Append((Id: "co2-no-expiration", Label: "CO₂ · 未检出呼吸")).ToArray();
        _ids = labels.Select(l => l.Id).ToArray();
        Editors = labels.ToDictionary(l => l.Id, l => new ConditionEditor(l.Label), StringComparer.Ordinal);
        AutomationProperties.SetName(Mode, "默认报警声音方式");
        Children.Add(new TextBlock { Text = "默认声音方式", TextWrapping = TextWrapping.Wrap });
        Children.Add(Mode);
        Children.Add(new TextBlock { Text = "仅用于选择“跟随默认”的事件；修改立即生效，应用后保存。", TextWrapping = TextWrapping.Wrap });
        Children.Add(new TextBlock { Text = "各参数事件声音", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
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
        RefreshErrors();
    }

    // Moves the parameter's event editors into a page and adds its overview row.
    internal Control CreateEventPage(MonitorNumeric numeric, string title, params string[] ids)
    {
        var page = new StackPanel { Spacing = 12 };
        foreach (string id in ids)
        {
            var section = new StackPanel { Spacing = 4 };
            section.Children.Add(new TextBlock { Text = Direction(id), FontWeight = FontWeight.SemiBold });
            section.Children.Add(Editors[id]);
            page.Children.Add(section);
        }
        page.Children.Add(DesktopInformationPages.Help("alarm-notification-settings"));
        var name = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
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

    private static string Direction(string id) =>
        id.EndsWith("-low", StringComparison.Ordinal) ? "低限" :
        id.EndsWith("-high", StringComparison.Ordinal) ? "高限" : "未检出呼吸";

    private void RefreshErrors()
    {
        string[] invalid = Editors.Where(e => !e.Value.IsValid).Select(e => e.Value.Label).ToArray();
        _errors.Text = Mode.SelectedIndex is not (0 or 1) ? "请选择有效声音模式；保留上次有效模式。" :
            invalid.Length == 0 ? null : string.Join("、", invalid) + "：请选择有效声音方式；时间须在标注范围内且最多三位小数。保留上次有效策略。";
        _errors.IsVisible = _errors.Text is not null;
        foreach (string id in _ids)
        {
            var settings = _effective[id];
            string duration = settings.ToPolicy(EffectiveMode).SoundDuration == AlarmSoundDuration.Continuous ? "长警报" : "短警报";
            string source = settings.SoundMode == AlarmSoundMode.Inherit ? "默认" : "独立";
            string error = Editors[id].IsValid ? "" : " · 待修正";
            _summaries[id] = $"{duration}（{source}）{error}";
        }
        foreach (var (title, ids, summary, row) in _overviewRows)
        {
            summary.Text = string.Join("  ·  ", ids.Select(id => Direction(id) + " " + _summaries[id]));
            AutomationProperties.SetName(row, title + " 事件声音：" + summary.Text);
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

    internal sealed class ConditionEditor : StackPanel
    {
        internal string Label { get; }
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
            Text = "请选择声音方式；时间须在标注范围内且最多三位小数。保留上次有效策略。",
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        internal NumericUpDown RepeatSeconds { get; } = Number(0, 0);
        internal CheckBox LatchUntilAcknowledged { get; } = new() { Content = "恢复后保留未确认提示", IsChecked = false };
        internal CheckBox ReminderEnabled { get; } = new() { Content = "持续活动时提醒", IsChecked = false };
        internal NumericUpDown ReminderSeconds { get; } = Number(30, .001m);
        internal event Action? Changed;
        internal bool IsValid { get { try { _ = Read(); return true; } catch (ArgumentException) { return false; } } }
        private bool _restoring;
        private AlarmPlaybackMode _defaultMode;

        internal ConditionEditor(string label)
        {
            Label = label;
            Spacing = 4;
            var choices = new WrapPanel { Orientation = Orientation.Horizontal };
            string[] titles = ["跟随默认", "短警报", "长警报"];
            string[] descriptions = ["", "每次播放一组", "活动期间持续播放"];
            var buttons = new List<RadioButton>();
            for (int index = 0; index < titles.Length; index++)
            {
                var content = new StackPanel { Spacing = 2 };
                content.Children.Add(new TextBlock { Text = titles[index], FontWeight = FontWeight.SemiBold });
                content.Children.Add(index == 0 ? _defaultDescription : new TextBlock { Text = descriptions[index], FontSize = 12 });
                var choice = new RadioButton
                {
                    Content = content,
                    MinHeight = 44,
                    Width = 196,
                    IsChecked = index == 0
                };
                AutomationProperties.SetName(choice, label + " " + titles[index]);
                choices.Children.Add(choice);
                buttons.Add(choice);
            }
            SoundChoices = buttons;
            Children.Add(choices);
            var fields = AlarmConfirmationEditor.CreateFieldsPanel();
            Add("重复抑制（秒，0–3600）", RepeatSeconds);
            Add("提醒间隔（秒，0.001–3600）", ReminderSeconds);
            _shortSettings.Children.Add(ReminderEnabled);
            _shortSettings.Children.Add(fields);
            var advancedContent = new StackPanel { Spacing = 8 };
            advancedContent.Children.Add(LatchUntilAcknowledged);
            advancedContent.Children.Add(_shortSettings);
            var reset = new Button { Content = "恢复此事件默认", MinHeight = 44 };
            AutomationProperties.SetName(reset, label + " 恢复默认通知策略");
            reset.Click += (_, _) => Restore(AlarmNotificationSettings.Default);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(reset);
            actions.Children.Add(new TextBlock { Text = "修改立即生效，应用后保存。", Margin = new Thickness(12, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
            advancedContent.Children.Add(actions);
            Advanced.Content = advancedContent;
            Children.Add(Advanced);
            Children.Add(_error);
            void Add(string text, NumericUpDown field)
            {
                var row = new StackPanel { Spacing = 4, Width = 220, Margin = new Thickness(0, 0, 24, 8) };
                row.Children.Add(new TextBlock { Text = text });
                row.Children.Add(field);
                fields.Children.Add(row);
                AutomationProperties.SetName(field, label + " " + text);
            }
            AutomationProperties.SetName(LatchUntilAcknowledged, label + " 恢复后保留未确认提示");
            LatchUntilAcknowledged.IsCheckedChanged += (_, _) => Edited();
            AutomationProperties.SetName(ReminderEnabled, label + " 持续活动时提醒");
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
            _defaultDescription.Text = _defaultMode == AlarmPlaybackMode.Notifications ? "当前：短警报" : "当前：长警报";
            AutomationProperties.SetName(SoundChoices[0], Label + " 跟随默认，" + _defaultDescription.Text);
            _shortSettings.IsVisible = shortSound || invalid;
            RepeatSeconds.IsEnabled = ReminderEnabled.IsEnabled = shortSound || invalid;
            ReminderSeconds.IsEnabled = invalid || shortSound && ReminderEnabled.IsChecked == true;
            Advanced.Header = shortSound || invalid ? "短警报高级参数" : "保持与更多操作";
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
