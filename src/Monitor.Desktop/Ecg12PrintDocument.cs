// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;
using SkiaSharp;

namespace Monitor.Desktop;

internal enum Ecg12PrintAnswer { HeartRate, Pr, Qrs, Qt, Qtc, QrsAxis }

internal sealed record Ecg12PrintOptions(string Title, string Subtitle, bool ShowCandidate, string Candidate,
    bool Color, bool ShowMeasurements, string Diagnosis, int SpeedMmPerSecond = 25, int GainMmPerMillivolt = 10,
    long StartTimeNs = 0)
{
    internal IReadOnlyList<Ecg12PrintAnswer> AnswerFields { get; init; } = Enum.GetValues<Ecg12PrintAnswer>();
}

// One physical A4 page. PDF and preview replay the same vector picture in millimeters.
internal sealed class Ecg12PrintDocument : IDisposable
{
    internal const float WidthMm = 210, HeightMm = 297, PointsPerMm = 72f / 25.4f;
    internal const float GridLeftMm = 12, GridRightMm = 198, GridBottomMm = 246;
    internal float GridTopMm { get; }
    internal float RowHeightMm => (GridBottomMm - 8 - GridTopMm - 14) / 11;
    private readonly SKPicture _picture;
    internal Ecg12PrintOptions Options { get; }
    internal long EndTimeNs { get; }
    internal static float SamplesLeftMm(int speedMmPerSecond) => GridLeftMm + 2 + .2f * speedMmPerSecond;
    internal static long DurationNs(int speedMmPerSecond) => (long)((GridRightMm - SamplesLeftMm(speedMmPerSecond)) * 1_000_000_000d / speedMmPerSecond);
    internal static float SampleX(long timeNs, Ecg12PrintOptions options) =>
        SamplesLeftMm(options.SpeedMmPerSecond) + (float)((timeNs - options.StartTimeNs) / 1e9 * options.SpeedMmPerSecond);
    internal float BaselineMm(int lead) => GridTopMm + 14 + lead * RowHeightMm;

    internal Ecg12PrintDocument(IReadOnlyList<WaveformEnvelope> blocks, Ecg12PrintOptions options, DesktopLocalization localization)
    {
        if (options.SpeedMmPerSecond is not (25 or 50) || options.GainMmPerMillivolt is not (5 or 10) || options.StartTimeNs < 0)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (options.AnswerFields.Any(field => !Enum.IsDefined(field)) || options.AnswerFields.Distinct().Count() != options.AnswerFields.Count)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        Options = options with { AnswerFields = options.AnswerFields.ToArray() };
        GridTopMm = 14 + (string.IsNullOrWhiteSpace(options.Title) ? 0 : 8) +
            (string.IsNullOrWhiteSpace(options.Subtitle) ? 0 : 6) + (options.ShowCandidate ? 10 : 0);
        EndTimeNs = checked(options.StartTimeNs + DurationNs(options.SpeedMmPerSecond));
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, WidthMm, HeightMm));
        Draw(canvas, blocks, localization);
        _picture = recorder.EndRecording();
    }

    internal void WritePdf(Stream destination)
    {
        using var document = SKDocument.CreatePdf(destination);
        var canvas = document.BeginPage(WidthMm * PointsPerMm, HeightMm * PointsPerMm);
        canvas.Scale(PointsPerMm);
        canvas.DrawPicture(_picture);
        document.EndPage();
        document.Close();
    }

    internal byte[] PreviewPng(int pixelsPerMm = 4)
    {
        using var bitmap = new SKBitmap((int)WidthMm * pixelsPerMm, (int)HeightMm * pixelsPerMm);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(pixelsPerMm);
        canvas.DrawPicture(_picture);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private void Draw(SKCanvas canvas, IReadOnlyList<WaveformEnvelope> blocks, DesktopLocalization localization)
    {
        canvas.Clear(SKColors.White);
        using var typeface = SKFontManager.Default.MatchCharacter('中') ?? SKTypeface.FromFamilyName("sans-serif");
        using var font = new SKFont(typeface, 3);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var line = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = .2f };
        void Text(string text, float x, float y, float size = 3, bool center = false, float width = 186)
        {
            font.Size = size;
            string fitted = Fit(text.Replace('\r', ' ').Replace('\n', ' '), font, width);
            canvas.DrawText(fitted, center ? x - font.MeasureText(fitted) / 2 : x, y, font, ink);
        }
        void Paragraph(string text, float x, float y, int lines, float size = 3)
        {
            font.Size = size;
            string remaining = text.Replace("\r", "", StringComparison.Ordinal);
            for (int index = 0; index < lines && remaining.Length > 0; index++)
            {
                int newline = remaining.IndexOf('\n');
                string candidate = newline < 0 ? remaining : remaining[..newline];
                string fitted = Fit(candidate, font, 186, ellipsis: index == lines - 1);
                Text(fitted, x, y + index * 4, size);
                if (index == lines - 1) { break; }
                int consumed = fitted.Length;
                if (consumed == candidate.Length && newline >= 0) { consumed++; }
                remaining = remaining[consumed..];
            }
        }
        float headerY = 12;
        if (!string.IsNullOrWhiteSpace(Options.Title))
        {
            Text(Options.Title, 105, headerY + 5, 5, center: true);
            headerY += 8;
        }
        if (!string.IsNullOrWhiteSpace(Options.Subtitle))
        {
            Text(Options.Subtitle, 105, headerY + 3.5f, 3.5f, center: true);
            headerY += 6;
        }
        if (Options.ShowCandidate) { Paragraph(Options.Candidate, 12, headerY + 3, 2); }
        DrawGrid(canvas);
        canvas.Save();
        canvas.ClipRect(new SKRect(GridLeftMm, GridTopMm, GridRightMm, GridBottomMm));
        float samplesLeft = SamplesLeftMm(Options.SpeedMmPerSecond);
        for (int lead = 0; lead < 12; lead++)
        {
            float baseline = BaselineMm(lead);
            float calibrationWidth = .2f * Options.SpeedMmPerSecond;
            // The calibration occupies reserved space and does not shift any lead's sample time.
            using var pulse = new SKPath();
            pulse.MoveTo(samplesLeft - calibrationWidth - 1, baseline);
            pulse.LineTo(samplesLeft - calibrationWidth - 1, baseline - Options.GainMmPerMillivolt);
            pulse.LineTo(samplesLeft - 1, baseline - Options.GainMmPerMillivolt);
            pulse.LineTo(samplesLeft - 1, baseline);
            canvas.DrawPath(pulse, line);
            canvas.Save();
            canvas.ClipRect(new SKRect(samplesLeft, GridTopMm, GridRightMm, GridBottomMm));
            using var trace = new SKPath();
            long? previousTime = null;
            foreach (var block in blocks)
            {
                var plane = block.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId((EcgLead)lead));
                long stepNs = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                for (int index = 0; index < plane.Samples.Count; index++)
                {
                    long timeNs = block.StartSimTimeNs + index * stepNs;
                    if (timeNs < Options.StartTimeNs || timeNs >= EndTimeNs) { continue; }
                    if (plane.Samples[index] is short.MinValue or short.MaxValue ||
                        plane.QualityRanges.Any(range => index >= range.FirstSampleOffset && index < (long)range.FirstSampleOffset + range.Count && range.QualityFlags != 0))
                    { previousTime = null; continue; }
                    double microvolts = (double)plane.Samples[index] * plane.ScaleNumerator / plane.ScaleDenominator +
                        (double)plane.OffsetNumerator / plane.OffsetDenominator;
                    float x = SampleX(timeNs, Options);
                    float y = baseline - (float)(microvolts / 1000 * Options.GainMmPerMillivolt);
                    if (previousTime is { } previous && timeNs - previous == stepNs) { trace.LineTo(x, y); }
                    else { trace.MoveTo(x, y); }
                    previousTime = timeNs;
                }
            }
            canvas.DrawPath(trace, line);
            canvas.Restore();
        }
        // Labels remain transparent so the paper grid is continuous underneath.
        for (int lead = 0; lead < 12; lead++)
        {
            Text(ProjectedEcgDemoSource.LeadNames[lead], samplesLeft + 1, BaselineMm(lead) - 5, 2.8f, width: 9);
        }
        Text(FormattableString.Invariant($"{Options.SpeedMmPerSecond} mm/s   {Options.GainMmPerMillivolt} mm/mV   {Options.StartTimeNs / 1e9:0.00}–{EndTimeNs / 1e9:0.00} s"), 13, GridTopMm + 3, 2.5f, width: 130);
        Text(localization.Get("print.simulation"), 151, GridTopMm + 3, 2.5f, width: 46);
        Text(localization.Get("print.teaching"), 13, GridBottomMm - 1.5f, 2.5f, width: 65);
        canvas.Restore();
        float answerY = GridBottomMm + 6;
        if (Options.ShowMeasurements)
        {
            Text(localization.Get("print.measurementCaption"), 12, answerY, 2.7f);
            for (int index = 0; index < Options.AnswerFields.Count; index++)
            {
                var field = Options.AnswerFields[index];
                string unit = field switch { Ecg12PrintAnswer.HeartRate => "bpm", Ecg12PrintAnswer.QrsAxis => "°", _ => "ms" };
                Text(localization.Get(AnswerKey(field)) + "  __________ " + unit, 12 + index % 3 * 62, answerY + 6 + index / 3 * 6, 2.8f, width: 60);
            }
            answerY += 6 + (Options.AnswerFields.Count + 2) / 3 * 6;
        }
        Text(localization.Get("print.diagnosis"), 12, answerY, 3);
        Paragraph(Options.Diagnosis, 12, answerY + 7, (int)((287 - answerY - 7) / 4) + 1);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        using var minor = new SKPaint { Color = Options.Color ? SKColor.Parse("#F0D1D7") : SKColor.Parse("#B5B5B5"), StrokeWidth = .1f, IsAntialias = true };
        using var major = new SKPaint { Color = Options.Color ? SKColor.Parse("#DFA0AC") : SKColor.Parse("#999999"), StrokeWidth = .15f, IsAntialias = true };
        for (int x = (int)GridLeftMm; x <= GridRightMm; x++)
        {
            bool large = (x - GridLeftMm) % 5 == 0;
            if (Options.Color || large) { canvas.DrawLine(x, GridTopMm, x, GridBottomMm, large ? major : minor); }
        }
        for (int y = (int)GridTopMm; y <= GridBottomMm; y++)
        {
            bool large = (y - GridTopMm) % 5 == 0;
            if (Options.Color || large) { canvas.DrawLine(GridLeftMm, y, GridRightMm, y, large ? major : minor); }
            if (!Options.Color)
            {
                for (int x = (int)GridLeftMm; x <= GridRightMm; x++) { canvas.DrawCircle(x, y, .12f, minor); }
            }
        }
    }

    private static string Fit(string text, SKFont font, float width, bool ellipsis = true)
    {
        if (font.MeasureText(text) <= width) { return text; }
        string suffix = ellipsis ? "…" : "";
        int length = text.Length;
        while (length > 0 && font.MeasureText(text[..length] + suffix) > width)
        {
            length--;
            if (length > 0 && char.IsHighSurrogate(text[length - 1])) { length--; }
        }
        return text[..length] + suffix;
    }

    internal static string AnswerKey(Ecg12PrintAnswer field) => field switch
    {
        Ecg12PrintAnswer.HeartRate => "print.answerHeartRate",
        Ecg12PrintAnswer.Pr => "print.answerPr",
        Ecg12PrintAnswer.Qrs => "print.answerQrs",
        Ecg12PrintAnswer.Qt => "print.answerQt",
        Ecg12PrintAnswer.Qtc => "print.answerQtc",
        Ecg12PrintAnswer.QrsAxis => "print.answerQrsAxis",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    public void Dispose() => _picture.Dispose();
}
