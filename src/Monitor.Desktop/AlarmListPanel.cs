// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class AlarmListPanel : StackPanel
{
    private readonly DesktopLocalization _localization;
    private readonly Func<AlarmAttentionJournal, AlarmAttentionSnapshot, bool> _acknowledge;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly Dictionary<string, Row> _shown = new(StringComparer.Ordinal);
    internal TextBlock Empty { get; } = new();
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    internal IReadOnlyDictionary<string, Button> AcknowledgeButtons => _shown
        .Where(pair => pair.Value.Confirm is not null).ToDictionary(pair => pair.Key, pair => pair.Value.Confirm!, StringComparer.Ordinal);

    private sealed record Row(AlarmAttentionJournal? Owner, AlarmAttentionSnapshot? State, Grid Control, TextBlock Caption, Button? Confirm);

    internal AlarmListPanel(DesktopLocalization localization, Func<AlarmAttentionJournal, AlarmAttentionSnapshot, bool> acknowledge)
    {
        _localization = localization;
        _acknowledge = acknowledge;
        Width = 360;
        Spacing = 10;
        // The app's light button resources also flow into dark skin popups.
        Resources["ButtonBackground"] = Brush.Parse("#222C38");
        Resources["ButtonBackgroundPointerOver"] = Brush.Parse("#34414F");
        Resources["ButtonBackgroundPressed"] = Brush.Parse("#145AA3");
        Resources["ButtonForeground"] = Brushes.White;
        Resources["ButtonForegroundPointerOver"] = Brushes.White;
        Resources["ButtonForegroundPressed"] = Brushes.White;
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 18 };
        localization.Bind(title, TextBlock.TextProperty, "alarm.currentList");
        localization.Bind(Empty, TextBlock.TextProperty, "alarm.listEmpty");
        Children.Add(title);
        Children.Add(Empty);
        Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 320, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Children.Add(Status);
        Children.Add(DesktopInformationPages.Help("alarm-acknowledgement"));
        Children.Add(DesktopInformationPages.Help("topic-9"));
    }

    internal void Refresh(IReadOnlyList<MonitorNotice> notices, IReadOnlyList<AlarmLifecycleJournal> journals)
    {
        var attention = journals.SelectMany(j => j.Attention.Conditions.Select(state => (Owner: j.Attention, State: state)))
            .ToDictionary(item => item.State.ConditionId, StringComparer.Ordinal);
        var ordered = notices.DistinctBy(n => n.Id).OrderByDescending(n => n.Level).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        var ids = ordered.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _shown.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _rows.Children.Remove(_shown[id].Control);
            _shown.Remove(id);
        }
        for (int index = 0; index < ordered.Length; index++)
        {
            var notice = ordered[index];
            string conditionId = notice.Id.StartsWith(MonitorAlertSettings.RetainedNoticePrefix, StringComparison.Ordinal)
                ? notice.Id[MonitorAlertSettings.RetainedNoticePrefix.Length..] : notice.Id;
            var (owner, state) = attention.GetValueOrDefault(conditionId);
            if (!_shown.TryGetValue(notice.Id, out var row) || row.Owner != owner || row.State != state)
            {
                if (row is not null) { _rows.Children.Remove(row.Control); }
                row = CreateRow(owner, state);
                _shown[notice.Id] = row;
            }
            string text = notice.Message?.Render(_localization.Current) ?? notice.Text;
            row.Caption.Text = _localization.Format("alarm.listItem", _localization.Get("alarm.level" + notice.Level), text);
            if (row.Confirm is { } confirm)
            { AutomationProperties.SetName(confirm, _localization.Format("alarm.acknowledgeNamed", text)); }
            if (_rows.Children.IndexOf(row.Control) != index)
            {
                _rows.Children.Remove(row.Control);
                _rows.Children.Insert(index, row.Control);
            }
        }
        Empty.IsVisible = ordered.Length == 0;
    }

    private Row CreateRow(AlarmAttentionJournal? owner, AlarmAttentionSnapshot? state)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var caption = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        grid.Children.Add(caption);
        Button? confirm = null;
        if (owner is not null && state is { NeedsAcknowledgement: true, Episode: not null })
        {
            confirm = new Button { MinHeight = 44, VerticalAlignment = VerticalAlignment.Center };
            _localization.Bind(confirm, ContentControl.ContentProperty, "alarm.acknowledgeAction");
            confirm.Click += (_, _) =>
            {
                bool accepted = _acknowledge(owner, state);
                Status.IsVisible = !accepted;
                if (!accepted) { _localization.Bind(Status, TextBlock.TextProperty, "alarm.acknowledgeStale"); }
            };
            Grid.SetColumn(confirm, 1);
            grid.Children.Add(confirm);
        }
        return new(owner, state, grid, caption, confirm);
    }
}
