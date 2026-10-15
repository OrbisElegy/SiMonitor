// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class MonitorTrendHistory
{
    internal sealed record Entry(long TimeNs, string HeartRate, string SpO2, string Respiration, ManualNibp Pressure);
    private readonly List<Entry> _entries = [];
    internal IReadOnlyList<Entry> Entries => _entries.AsReadOnly();
    internal long? LatestTimeNs => _entries.Count == 0 ? null : _entries[0].TimeNs;
    internal void Clear() => _entries.Clear();
    internal void Observe(LocalMonitorPreviewSession session, MonitorChannels channels)
    {
        if (session.Nibp.Reading is not { } pressure || session.Nibp.MeasuredAtNs is not { } time ||
            _entries.Any(entry => entry.TimeNs == time)) { return; }
        var values = session.Measurements;
        static string Rate(WaveformMeasurementStatus? status, int? value) => status == WaveformMeasurementStatus.Valid && value.HasValue
            ? (value.Value / 1000m).ToString("0", CultureInfo.InvariantCulture) : "--";
        _entries.Insert(0, new(time,
            channels.HasFlag(MonitorChannels.Ecg) ? Rate(values?.HeartRate.Status, values?.HeartRate.MilliBeatsPerMinute) : "--",
            channels.HasFlag(MonitorChannels.Pleth) ? Rate(values?.SpO2.Status, values?.SpO2.SaturationMilliPercent) + (values?.SpO2.IsQuestionable == true ? "?" : "") : "--",
            channels.HasFlag(MonitorChannels.Resp) ? Rate(values?.ImpedanceRespiration.Status, values?.ImpedanceRespiration.MilliBreathsPerMinute) : "--", pressure));
        if (_entries.Count > 4) { _entries.RemoveAt(4); }
    }
}

internal sealed class MonitorTrendTable(MonitorTrendHistory history, DesktopLocalization localization, IMonitorSkin skin) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        string[] headers = [localization.Get("skin.historyTime"), "HR", "SpO₂", "RR", "NIBP"];
        double[] columns = [0, .19, .34, .49, .64];
        IBrush[] colors = [Brush.Parse("#B9C6D4"), skin.ChannelBrush(0), skin.ChannelBrush(2), skin.ChannelBrush(1), Brush.Parse("#B9E7F5")];
        void Text(string text, int column, double y, IBrush color)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(DesignPreviewWindow.PreviewFont), 13, color);
            context.DrawText(formatted, new Point(8 + columns[column] * Math.Max(0, Bounds.Width - 16), y));
        }
        for (int column = 0; column < headers.Length; column++) { Text(headers[column], column, 2, colors[column]); }
        if (history.Entries.Count == 0) { Text(localization.Get("skin.historyEmpty"), 0, 24, colors[0]); }
        for (int row = 0; row < history.Entries.Count; row++)
        {
            var entry = history.Entries[row];
            long seconds = entry.TimeNs / 1_000_000_000;
            string[] values = [string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}"),
                entry.HeartRate, entry.SpO2, entry.Respiration,
                $"{entry.Pressure.SystolicMmHg}/{entry.Pressure.DiastolicMmHg} ({entry.Pressure.MeanMmHg})"];
            for (int column = 0; column < values.Length; column++) { Text(values[column], column, 24 + row * 17, colors[column]); }
        }
    }
}
