// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Host-owned channel alarms occupy the header of each waveform row. A channel's
// rotation follows the channel across layout changes, including duplicate rows.
internal sealed class WaveformAlarmBanners : Panel
{
    private static readonly IBrush NeutralBackground = Brush.Parse("#34373B");
    private static readonly IBrush CriticalBackground = Brush.Parse("#B51F2C");
    private static readonly IBrush NoticeBackground = Brush.Parse("#145AA3");
    private readonly Func<IReadOnlyList<MonitorDisplaySlot>> _slots;
    private readonly IMonitorSkin _skin;
    private readonly DesktopLocalization _localization;
    private readonly Dictionary<int, MonitorNoticeRotation> _rotations = [];
    private readonly List<WaveformAlarmBannerRow> _rows = [];

    internal IReadOnlyList<WaveformAlarmBannerRow> Rows => _rows;

    internal WaveformAlarmBanners(Func<IReadOnlyList<MonitorDisplaySlot>> slots, IMonitorSkin skin,
        DesktopLocalization localization)
    {
        _slots = slots;
        _skin = skin;
        _localization = localization;
        ClipToBounds = true;
        RefreshRows();
    }

    internal void Refresh(IEnumerable<MonitorNotice> projected, long simTimeNs,
        Func<string, AlarmAttentionSnapshot?>? attentionFor, bool noticeColor)
    {
        ArgumentNullException.ThrowIfNull(projected);
        RefreshRows();
        var notices = projected.ToArray();
        foreach (var (channel, rotation) in _rotations)
        {
            var mapped = MonitorChannelMapping.ForChannel(channel);
            rotation.Update(notices.Where(notice => notice.Numeric is { } numeric &&
                MonitorChannelMapping.ForNumeric(numeric) == mapped), simTimeNs);
        }

        // The same simulation-clock phase as the main banner and numeric tiles.
        bool on = simTimeNs % 1_000_000_000 < 500_000_000;
        foreach (var row in _rows)
        {
            var rotation = _rotations[row.Channel];
            var notice = rotation.Current;
            row.Notice = notice;
            row.Region.IsVisible = notice is not null &&
                (_skin.Channels & MonitorChannelMapping.ForChannel(row.Channel)) != 0;
            string text = notice?.Message?.Render(_localization.Current) ?? notice?.Text ?? "";
            var attention = notice is null ? null : attentionFor?.Invoke(notice.Id);
            if (attention?.State != AlarmAttentionState.RecoveredUnacknowledged && notice is not null &&
                rotation.CriticalElapsedNs(notice.Id) is { } elapsed)
            {
                long seconds = elapsed / 1_000_000_000;
                text = _localization.Format("monitor.criticalElapsed", text,
                    string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}"));
            }
            row.Text.Text = text;
            ToolTip.SetTip(row.Region, text);
            AutomationProperties.SetName(row.Region, text);
            AutomationProperties.SetName(row.Text, text);

            bool highlight = on && notice is not null && notice.Level != MonitorNoticeLevel.Info &&
                attention?.State is not (AlarmAttentionState.ActiveAcknowledged or AlarmAttentionState.RecoveredUnacknowledged) &&
                (notice.Level != MonitorNoticeLevel.Notice || noticeColor);
            row.Region.Background = !highlight ? NeutralBackground : notice!.Level switch
            {
                MonitorNoticeLevel.Critical => CriticalBackground,
                MonitorNoticeLevel.Warning => _skin.WarningBrush,
                _ => NoticeBackground
            };
            row.Text.Foreground = highlight && notice!.Level == MonitorNoticeLevel.Warning ? Brushes.Black : Brushes.White;
        }
    }

    private void RefreshRows()
    {
        var slots = _slots();
        if (_rows.Count == slots.Count && _rows.Select(row => row.Channel).SequenceEqual(slots.Select(slot => slot.Channel)))
        { return; }
        var channels = slots.Select(slot => slot.Channel).ToHashSet();
        foreach (int channel in _rotations.Keys.Where(channel => !channels.Contains(channel)).ToArray())
        { _rotations.Remove(channel); }
        Children.Clear();
        _rows.Clear();
        foreach (var slot in slots)
        {
            if (!_rotations.ContainsKey(slot.Channel)) { _rotations.Add(slot.Channel, new MonitorNoticeRotation()); }
            var text = new TextBlock
            {
                FontSize = 13,
                FontFamily = DesignPreviewWindow.PreviewFont,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var region = new Border
            {
                Child = text,
                Background = NeutralBackground,
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                IsVisible = false
            };
            _rows.Add(new(slot.Channel, region, text));
            Children.Add(region);
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 670;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : _rows.Count * 138;
        var (scale, _, bannerWidth, bannerHeight) = Geometry(new Size(width, height));
        foreach (var row in _rows)
        {
            row.Text.FontSize = 13 * scale;
            row.Region.Padding = new Thickness(6 * scale, 0);
            row.Region.Measure(new Size(bannerWidth, bannerHeight));
        }
        // The waveform surface determines the size; alarm text cannot resize it.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (scale, rowHeight, bannerWidth, bannerHeight) = Geometry(finalSize);
        double left = Math.Max(0, finalSize.Width - 8 * scale - bannerWidth);
        for (int index = 0; index < _rows.Count; index++)
        {
            _rows[index].Region.Arrange(new Rect(left, index * rowHeight + 4 * scale, bannerWidth, bannerHeight));
        }
        return finalSize;
    }

    private (double Scale, double RowHeight, double BannerWidth, double BannerHeight) Geometry(Size size)
    {
        double rowHeight = size.Height / Math.Max(1, _rows.Count);
        // The header remains above y=30, where the waveform plotting area starts.
        double scale = Math.Clamp(Math.Min(size.Width / 670, rowHeight / 138), .5, 1);
        double width = Math.Min(245 * scale, Math.Max(0, size.Width - 188 * scale));
        double height = Math.Min(22 * scale, Math.Max(0, rowHeight - 6 * scale));
        return (scale, rowHeight, width, height);
    }
}

internal sealed class WaveformAlarmBannerRow(int channel, Border region, TextBlock text)
{
    internal int Channel { get; } = channel;
    internal Border Region { get; } = region;
    internal TextBlock Text { get; } = text;
    internal MonitorNotice? Notice { get; set; }
}
