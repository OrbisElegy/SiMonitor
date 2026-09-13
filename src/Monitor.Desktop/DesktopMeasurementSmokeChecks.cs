// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class DesktopMeasurementSmokeChecks
{
    public static void Verify()
    {
        if (MeasurementReadout.Exact(new(1, 3)) != "1/3" ||
            MeasurementReadout.Exact(new(1, 1_000_000)) != "0.000001" ||
            MeasurementReadout.Exact(new(1, 10_000_000)) != "1/10000000" ||
            MeasurementReadout.Exact(new(-50, 1000)) != "-0.05" ||
            MeasurementReadout.Exact(new(0, 7)) != "0" ||
            MeasurementReadout.Exact(new(BigInteger.Parse("1000000000000000000000000000001", System.Globalization.CultureInfo.InvariantCulture), 1)) != "1000000000000000000000000000001")
        { throw new InvalidOperationException("Readout rounded or truncated exact evidence."); }
        RecordMeasurementDisplay ready = new("RecordMeasurement.Ready", null, null,
            new(new(100, 1), new(-1, 20), new(600, 1)));
        if (MeasurementReadout.Format(ready) != "Δt：100 ms    ΔV（终点−起点）：-0.05 mV    辅助频率：600 次/分")
        { throw new InvalidOperationException("Permitted auxiliary result was not explicitly labelled."); }
        if (MeasurementReadout.Format(ready with { Measurement = ready.Measurement! with { AuxiliaryRatePerMinute = null } })!.Contains("辅助", StringComparison.Ordinal))
        { throw new InvalidOperationException("Absent auxiliary result was fabricated."); }
        foreach (string reason in new[] { "RecordMeasurement.Disabled", "RecordMeasurement.CourseLocked", "RecordMeasurement.NoCursorPair" })
        {
            if (MeasurementReadout.Format(ready with { ReasonCode = reason }) is not null)
            { throw new InvalidOperationException("Unavailable measurement leaked its readout."); }
        }
        Console.WriteLine("ok: exact measurement formatting, signed amplitude and optional auxiliary gating");
    }
}
