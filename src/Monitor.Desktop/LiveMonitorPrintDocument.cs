// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;
using SkiaSharp;

namespace Monitor.Desktop;

internal sealed record LiveMonitorPrintOptions(bool TwelveLead, EcgLead Lead, long StartTimeNs,
    int DurationSeconds = 10, int SpeedMmPerSecond = 25, int GainMmPerMillivolt = 10, bool Color = true);

// Landscape A4, with fixed physical scales and the same vector pages for preview and PDF.
internal sealed class LiveMonitorPrintDocument : IDisposable
{
    internal const float WidthMm = 297, HeightMm = 210;
    internal const float TraceLeftMm = 32, TraceRightMm = 282;
    private readonly List<SKPicture> _pages = [];
    internal LiveMonitorPrintOptions Options { get; }
    internal long EndTimeNs { get; }
    internal IReadOnlyList<PreviewRecordEvent> Events { get; }
    internal int PageCount => _pages.Count;
    internal long SegmentDurationNs => 250_000_000_000L / Options.SpeedMmPerSecond;
    internal static float SampleX(long timeNs, long segmentStartNs, int speedMmPerSecond) =>
        TraceLeftMm + (float)((timeNs - segmentStartNs) / 1e9 * speedMmPerSecond);

    internal LiveMonitorPrintDocument(MonitorPrintSnapshot snapshot, LiveMonitorPrintOptions options, DesktopLocalization localization)
    {
        if (!Enum.IsDefined(options.Lead) || options.DurationSeconds is not (10 or 20 or 30) ||
            options.SpeedMmPerSecond is not (25 or 50) || options.GainMmPerMillivolt is not (5 or 10) ||
            options.StartTimeNs < snapshot.AvailableFromNs || options.StartTimeNs > snapshot.CapturedAtNs)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        Options = options;
        EndTimeNs = Math.Min(snapshot.CapturedAtNs, options.StartTimeNs + options.DurationSeconds * 1_000_000_000L);
        // Include an event exactly at capture time, even before its first sample arrives.
        Events = snapshot.Events.Where(item => item.TimeNs >= options.StartTimeNs &&
            (item.TimeNs < EndTimeNs || item.TimeNs == EndTimeNs && EndTimeNs == snapshot.CapturedAtNs)).ToArray();
        int[] leads = options.TwelveLead ? Enumerable.Range(0, 12).ToArray() : [(int)options.Lead];
        for (long start = options.StartTimeNs; start < EndTimeNs; start += SegmentDurationNs)
        {
            long end = Math.Min(EndTimeNs, start + SegmentDurationNs);
            foreach (int[] group in leads.Chunk(6))
            {
                using var recorder = new SKPictureRecorder();
                var canvas = recorder.BeginRecording(new SKRect(0, 0, WidthMm, HeightMm));
                DrawHeader(canvas, snapshot, localization);
                for (int row = 0; row < group.Length; row++)
                { DrawTrace(canvas, snapshot.Leads[group[row]], group[row], 48 + row * 24, start, end); }
                DrawText(canvas, FormattableString.Invariant($"{start / 1e9:0.000}–{end / 1e9:0.000} s"), 12, 195);
                DrawText(canvas, localization.Format("livePrint.eventCount", Events.Count), 105, 195);
                DrawText(canvas, (_pages.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 276, 201);
                _pages.Add(recorder.EndRecording());
            }
        }
        // Always append the event report; no events and no acquired samples are explicit states.
        var lines = new List<string>();
        using var typeface = SKFontManager.Default.MatchCharacter('中') ?? SKTypeface.FromFamilyName("sans-serif");
        using var font = new SKFont(typeface, 3.2f);
        for (int index = 0; index < Events.Count; index++)
        {
            var item = Events[index];
            string text = FormattableString.Invariant($"#{index + 1}   {item.TimeNs / 1e9:0.000} s   ") +
                localization.Get("livePrint.event." + item.Kind) + "   " + item.Detail;
            while (text.Length > 0)
            {
                int length = text.Length;
                while (length > 1 && font.MeasureText(text[..length]) > 270) { length--; }
                if (length < text.Length && char.IsHighSurrogate(text[length - 1])) { length--; }
                lines.Add(text[..length]);
                text = text[length..];
            }
        }
        if (lines.Count == 0) { lines.Add(localization.Get("livePrint.noEvents")); }
        foreach (string[] pageLines in lines.Chunk(25))
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, WidthMm, HeightMm));
            DrawHeader(canvas, snapshot, localization);
            DrawText(canvas, localization.Get("livePrint.events"), 12, 43, 4);
            for (int index = 0; index < pageLines.Length; index++)
            { DrawText(canvas, pageLines[index], 12, 52 + index * 5.5f); }
            DrawText(canvas, (_pages.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 276, 201);
            _pages.Add(recorder.EndRecording());
        }
    }

    private void DrawHeader(SKCanvas canvas, MonitorPrintSnapshot snapshot, DesktopLocalization localization)
    {
        canvas.Clear(SKColors.White);
        DrawText(canvas, localization.Get(Options.TwelveLead ? "livePrint.twelveLead" : "livePrint.rhythm"), 12, 15, 5);
        DrawText(canvas, localization.Get("print.simulation"), 225, 15);
        DrawText(canvas, localization.Format("livePrint.capture", snapshot.CapturedAtNs / 1e9), 12, 23);
        DrawText(canvas, FormattableString.Invariant($"{Options.StartTimeNs / 1e9:0.000}–{EndTimeNs / 1e9:0.000} s   ·   {Options.SpeedMmPerSecond} mm/s   ·   {Options.GainMmPerMillivolt} mm/mV"), 12, 30);
        if (EndTimeNs - Options.StartTimeNs < Options.DurationSeconds * 1_000_000_000L)
        { DrawText(canvas, localization.Format("livePrint.shortHistory", (EndTimeNs - Options.StartTimeNs) / 1e9, Options.DurationSeconds), 12, 37); }
    }

    private void DrawTrace(SKCanvas canvas, IReadOnlyList<MonitorPrintSample> samples, int lead, float top, long start, long end)
    {
        const float height = 24;
        float baseline = top + height / 2;
        float right = SampleX(end, start, Options.SpeedMmPerSecond);
        using var grid = new SKPaint { IsAntialias = true, StrokeWidth = .1f };
        for (int x = 12; x <= right; x++)
        {
            bool major = (x - 12) % 5 == 0;
            grid.Color = Options.Color ? SKColor.Parse(major ? "#DFA0AC" : "#F0D1D7") : SKColor.Parse(major ? "#999999" : "#DDDDDD");
            if (Options.Color || major) { canvas.DrawLine(x, top, x, top + height, grid); }
        }
        for (int y = 0; y <= height; y++)
        {
            grid.Color = Options.Color ? SKColor.Parse(y % 5 == 0 ? "#DFA0AC" : "#F0D1D7") : SKColor.Parse(y % 5 == 0 ? "#999999" : "#DDDDDD");
            if (Options.Color || y % 5 == 0) { canvas.DrawLine(12, top + y, right, top + y, grid); }
            if (!Options.Color)
            {
                for (int x = 12; x <= right; x++) { canvas.DrawCircle(x, top + y, .12f, grid); }
            }
        }
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = .2f };
        using var pulse = new SKPath();
        float calibrationWidth = .2f * Options.SpeedMmPerSecond;
        pulse.MoveTo(TraceLeftMm - calibrationWidth - 2, baseline);
        pulse.LineTo(TraceLeftMm - calibrationWidth - 2, baseline - Options.GainMmPerMillivolt);
        pulse.LineTo(TraceLeftMm - 2, baseline - Options.GainMmPerMillivolt);
        pulse.LineTo(TraceLeftMm - 2, baseline);
        canvas.DrawPath(pulse, ink);
        canvas.Save();
        canvas.ClipRect(new SKRect(TraceLeftMm, top, right, top + height));
        using var path = new SKPath();
        long? previous = null;
        foreach (var sample in samples)
        {
            if (sample.TimeNs < start || sample.TimeNs >= end) { continue; }
            if (!double.IsFinite(sample.Microvolts)) { previous = null; continue; }
            float x = SampleX(sample.TimeNs, start, Options.SpeedMmPerSecond);
            float y = baseline - (float)(sample.Microvolts / 1000 * Options.GainMmPerMillivolt);
            if (previous is { } time && sample.TimeNs - time == 4_000_000) { path.LineTo(x, y); }
            else { path.MoveTo(x, y); }
            previous = sample.TimeNs;
        }
        canvas.DrawPath(path, ink);
        canvas.Restore();
        DrawText(canvas, ProjectedEcgDemoSource.LeadNames[lead], 13, top + 4, 2.8f);
        foreach (var group in Events.Select((item, index) => (item.TimeNs, Number: index + 1)).GroupBy(item => item.TimeNs))
        {
            long time = group.Key;
            if (time < start || time > end || time == end && end != EndTimeNs) { continue; }
            float x = SampleX(time, start, Options.SpeedMmPerSecond);
            canvas.DrawLine(x, top + height - 3, x, top + height, ink);
            string label = group.Count() == 1 ? $"#{group.First().Number}" : $"#{group.First().Number}–{group.Last().Number}";
            DrawText(canvas, label, Math.Min(x, TraceRightMm - 16), top + height - 1, 2.4f);
        }
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, float size = 3.2f)
    {
        using var typeface = SKFontManager.Default.MatchCharacter('中') ?? SKTypeface.FromFamilyName("sans-serif");
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, x, y, font, paint);
    }

    internal byte[] PreviewPng(int page)
    {
        using var bitmap = new SKBitmap((int)WidthMm * 4, (int)HeightMm * 4);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(4);
        canvas.DrawPicture(_pages[page]);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    internal void WritePdf(Stream destination)
    {
        using var document = SKDocument.CreatePdf(destination);
        foreach (var picture in _pages)
        {
            var canvas = document.BeginPage(WidthMm * Ecg12PrintDocument.PointsPerMm, HeightMm * Ecg12PrintDocument.PointsPerMm);
            canvas.Scale(Ecg12PrintDocument.PointsPerMm);
            canvas.DrawPicture(picture);
            document.EndPage();
        }
        document.Close();
    }

    public void Dispose()
    {
        foreach (var page in _pages) { page.Dispose(); }
    }
}
