// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PtAndUWaveSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static ElectrodeSignalGenerator Source(EcgUWavePlan? u = null) => ElectrodeSignalGenerator.Start(
        Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u));
    public static Specification[] All =>
    [
        new(nameof(PtReferenceMeetsAmplitudeAndPolarityConstraints), PtReferenceMeetsAmplitudeAndPolarityConstraints),
        new(nameof(OptionalUStaysOutsideQtAndDefaultsOff), OptionalUStaysOutsideQtAndDefaultsOff),
        new(nameof(OptionalUHasFastRiseAndOwnedRecovery), OptionalUHasFastRiseAndOwnedRecovery),
        new(nameof(OptionalURejectsInvalidTimingAndElectrodes), OptionalURejectsInvalidTimingAndElectrodes),
    ];

    private static void PtReferenceMeetsAmplitudeAndPolarityConstraints()
    {
        var frames = Source().GenerateBefore(800_000_000, 200, 100);
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        {
            int p = frames.Where(frame => frame.Tick.SimTimeNs < 100_000_000).Max(frame => Math.Abs((int)frame.MicrovoltValues[(int)lead]));
            Check.That(p < (lead < EcgLead.V1 ? 250 : 200), "P amplitude stays below the applicable limb/chest reference ceiling");
        }
        foreach (EcgLead lead in new[] { EcgLead.I, EcgLead.II, EcgLead.AVF, EcgLead.V4, EcgLead.V5, EcgLead.V6 })
        { Check.That(frames[12].MicrovoltValues[(int)lead] > 0, "required P polarities remain upright"); }
        foreach (EcgLead lead in new[] { EcgLead.I, EcgLead.II, EcgLead.V4, EcgLead.V5, EcgLead.V6 })
        {
            int t = frames.Where(frame => frame.Tick.SimTimeNs is >= 340_000_000 and < 520_000_000)
                .Max(frame => (int)frame.MicrovoltValues[(int)lead]);
            int r = frames.Where(frame => frame.Tick.SimTimeNs is >= 160_000_000 and < 240_000_000)
                .Max(frame => (int)frame.MicrovoltValues[(int)lead]);
            Check.That(t > 0 && 10 * t >= r, "applicable upright T amplitudes reach one tenth of the lead R peak");
        }
        Check.That(frames[12].MicrovoltValues[(int)EcgLead.AVR] < 0 && frames[113].MicrovoltValues[(int)EcgLead.AVR] < 0,
            "aVR retains negative P and T");
    }

    private static EcgUWavePlan U(long[]? amplitudes = null) => new(30_000_000, 120_000_000,
        amplitudes ?? [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]);

    private static void OptionalUStaysOutsideQtAndDefaultsOff()
    {
        var off = Source().GenerateBefore(800_000_000, 200, 100);
        var on = Source(U()).GenerateBefore(800_000_000, 200, 100);
        for (int index = 0; index < off.Count; index++)
        {
            long time = off[index].Tick.SimTimeNs;
            if (time < 550_000_000 || time >= 670_000_000)
            { Check.That(off[index].MicrovoltValues.SequenceEqual(on[index].MicrovoltValues), "U cannot change P/QRS/ST/T or the next cycle"); }
            if (time >= 520_000_000)
            { Check.That(off[index].MicrovoltValues.All(value => value == 0), "default reference has no U waveform"); }
        }
        Check.That(on.Any(frame => frame.MicrovoltValues[(int)EcgLead.V3] > 0 && frame.Tick.SimTimeNs > 550_000_000) &&
            TextbookEcgReference.Timing.QtIntervalNs == 360_000_000, "optional U is separate from QT and not enabled by default");
    }

    private static void OptionalUHasFastRiseAndOwnedRecovery()
    {
        long[] amplitudes = [0, 0, 0, 0, 10, 40, 60, 20, 20, 20];
        var electrodes = TextbookElectrodeReference.CreateElectrodes(U(amplitudes));
        amplitudes[6] = 999;
        var composition = ElectrodeWaveformComposition.Restore(new(electrodes,
            RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(800_000_000, 100)));
        long Value(long time) => composition.EvaluateAt(time).Leads[EcgLead.V3].ToQ32();
        Check.That(Value(595_000_000) == 60L << 32 && Value(572_500_000) == 30L << 32 && Value(632_500_000) == 30L << 32,
            "U rises to its peak in 45ms and falls in 75ms, with owned explicit amplitude");
        var source = Source(U());
        _ = source.GenerateBefore(573_000_000, 144, 100);
        var expected = source.GenerateBefore(800_000_000, 100, 100);
        var split = Source(U());
        _ = split.GenerateBefore(573_000_000, 144, 100);
        var actual = ElectrodeSignalGenerator.Restore(split.CaptureState()).GenerateBefore(800_000_000, 100, 100);
        Check.That(expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick &&
            pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "restore retains an already active optional U");
    }

    private static void OptionalURejectsInvalidTimingAndElectrodes()
    {
        foreach (var invalid in new[] { U() with { DelayAfterTNs = -1 }, U() with { DurationNs = 0 },
            U() with { DurationNs = long.MaxValue }, U() with { ElectrodeAmplitudesMicrovolts = new long[9] } })
        {
            bool rejected = false;
            try { _ = TextbookElectrodeReference.CreateElectrodes(invalid); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EcgUWave.InvalidPlan"; }
            Check.That(rejected, "invalid U timing, next-cycle overlap or incomplete electrodes reject before construction");
        }
    }
}
