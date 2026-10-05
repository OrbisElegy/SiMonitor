// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Frozen twelve-lead paper geometry: 25 mm/s and 10 mm/mV at 4 px/mm. The
// renderer and the calipers share it, so a pointer maps to the lead and time
// drawn under it. Calibration pulses mask the first 30 px of each column.
public sealed class Ecg12PaperLayout(bool sixRows)
{
    public const double PixelsPerSecond = 100;
    public const double PixelsPerMillivolt = 40;
    // Lead indices 0-11 follow EcgLead order; 12 is the long lead II rhythm strip.
    public const int LongLeadIndex = 12;
    public const int LeadIIIndex = 1;
    public const double GridLeft = 32;
    public const double FirstBaseline = 136;
    public const double RowPitch = 120;
    public const double CalibrationWidth = 30;
    public const double LongSamplesLeft = 62;
    private const long NanosecondsPerPixel = 10_000_000;

    public bool SixRows { get; } = sixRows;
    public int Rows => SixRows ? 6 : 3;
    public int Columns => SixRows ? 2 : 4;
    public int ColumnWidth => SixRows ? 530 : 280;
    public double Width => SixRows ? 1124 : 1184;
    public double Height => SixRows ? 956 : 596;
    public long LongDurationNs => SixRows ? 10_300_000_000 : 10_900_000_000;
    public double LongBaseline => Height - 60;
    public long ShortDurationNs => (ColumnWidth - (long)CalibrationWidth) * NanosecondsPerPixel;

    // Calibration masks paper time on the common axis; it never inserts time.
    public long ColumnStartNs(int column) => column * ColumnWidth * NanosecondsPerPixel;

    public Ecg12PaperRegion Region(int lead)
    {
        if (lead == LongLeadIndex) { return new(lead, LeadIIIndex, LongSamplesLeft, LongBaseline, 0, LongDurationNs); }
        if (lead is < 0 or >= LongLeadIndex) { throw new ArgumentOutOfRangeException(nameof(lead)); }
        int column = lead / Rows;
        int row = lead % Rows;
        return new(lead, lead, GridLeft + column * ColumnWidth + CalibrationWidth, FirstBaseline + row * RowPitch,
            ColumnStartNs(column), ColumnStartNs(column) + ShortDurationNs);
    }

    // Null outside every lead's sample area, including calibration pulses and margins.
    public Ecg12PaperRegion? HitTest(double x, double y)
    {
        for (int lead = 0; lead <= LongLeadIndex; lead++)
        {
            var region = Region(lead);
            if (x >= region.Left && x < region.Right && y >= region.Top && y < region.Bottom) { return region; }
        }
        return null;
    }
}

// SourceLead is the acquired lead drawn in this region; the long strip draws lead II.
public sealed record Ecg12PaperRegion(int Lead, int SourceLead, double Left, double Baseline, long StartNs, long EndExclusiveNs)
{
    public double Right => XAt(EndExclusiveNs);
    public double Top => Baseline - Ecg12PaperLayout.RowPitch / 2;
    public double Bottom => Baseline + Ecg12PaperLayout.RowPitch / 2;
    public double XAt(long timeNs) => Left + (timeNs - StartNs) / 1e9 * Ecg12PaperLayout.PixelsPerSecond;
    public double YAt(long numeratorMicrovolts, uint denominator) =>
        Baseline - (double)numeratorMicrovolts / denominator / 1000 * Ecg12PaperLayout.PixelsPerMillivolt;
    public long TimeAt(double x) => StartNs + (long)Math.Round((x - Left) / Ecg12PaperLayout.PixelsPerSecond * 1e9);
}
