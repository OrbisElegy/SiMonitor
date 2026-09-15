// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ShortCycleEcgSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(Reported158BpmTimingFitsWithoutClipping), Reported158BpmTimingFitsWithoutClipping),
        new(nameof(ShortWaveDurationsReachNativeLeadsAndRecover), ShortWaveDurationsReachNativeLeadsAndRecover),
        new(nameof(InconsistentShortCyclesStillReject), InconsistentShortCyclesStillReject),
    ];

    private static EcgCycleTiming Timing(int rate = 158, int qtc = 386)
    {
        long rr = (long)FixedPointMath.RoundDivideTiesToEven(60_000_000_000, rate);
        long qt = new EcgQtCorrection(EcgQtCorrection.Bazett, qtc * 1_000_000L, rr).ResolveQtIntervalNs();
        return new(rr, 72_000_000, 94_000_000, 68_000_000, qt, 105_000_000);
    }

    private static void Reported158BpmTimingFitsWithoutClipping()
    {
        var timing = Timing();
        timing.Validate();
        // Independent Decimal70-digit calculation, rounding RR then QT to ns.
        Check.That(timing.RrIntervalNs == 379_746_835 && timing.QtIntervalNs == 237_867_105 &&
            timing.StDurationNs == 64_867_105, "reported QT238ms is consistent with rounded Bazett QTc386ms at158bpm");
        var bands = TextbookEcgReference.CreateBands(timing);
        Check.That(bands[0].DurationNs == 72_000_000 && bands[1].DurationNs == 68_000_000 &&
            bands[2].DurationNs == 105_000_000 && bands[2].DelayNs == 132_867_105,
            "explicit P/QRS/T durations and the QT-derived ST gap reach the generator unchanged");
        foreach (var value in new[] { Timing(180), Timing(200, 360) }) { value.Validate(); }
    }

    private static ElectrodeSignalGenerator Source(EcgCycleTiming timing) => ElectrodeSignalGenerator.Start(
        new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000, timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000),
        "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: timing));

    private static void ShortWaveDurationsReachNativeLeadsAndRecover()
    {
        var timing = Timing();
        var expected = Source(timing).GenerateBefore(1_200_000_000, 300, 100);
        Check.That(expected.Take(18).Any(frame => frame.MicrovoltValues[(int)EcgLead.II] > 0) &&
            expected.Skip(18).Take(6).All(frame => frame.MicrovoltValues.All(value => value == 0)) &&
            expected.Skip(24).Take(17).Any(frame => frame.MicrovoltValues[(int)EcgLead.V5] > 1500) &&
            expected.Skip(58).Take(25).Any(frame => frame.MicrovoltValues[(int)EcgLead.II] > 0) &&
            expected.Skip(83).Take(12).All(frame => frame.MicrovoltValues.All(value => value == 0)),
            "native250Hz P/PR/QRS/T and final baseline occupy the short-cycle timing rather than adult reference durations");
        var source = Source(timing);
        var first = source.GenerateBefore(401_000_000, 101, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var actual = first.Concat(restored.GenerateBefore(1_200_000_000, 200, 100)).ToArray();
        Check.That(actual.Length == expected.Count && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick &&
            pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "fractional RR and short waves recover identically in all12 leads");
    }

    private static void InconsistentShortCyclesStillReject()
    {
        var valid = Timing();
        foreach (var invalid in new[] { valid with { PDurationNs = 95_000_000 }, valid with { TDurationNs = 180_000_000 },
            valid with { QtIntervalNs = 300_000_000 }, valid with { QrsDurationNs = 0 } })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(timing: invalid); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EcgTiming.InconsistentIntervals"; }
            Check.That(rejected, "explicit durations do not bypass P/PR, QRS/T or next-cycle fit constraints");
        }
    }
}
