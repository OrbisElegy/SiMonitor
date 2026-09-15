// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TWaveShapeSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(TPeakMovesWithoutChangingAmplitudeOrSupport), TPeakMovesWithoutChangingAmplitudeOrSupport),
        new(nameof(TPeakDefaultAndNonRepolarizationSamplesStayIdentical), TPeakDefaultAndNonRepolarizationSamplesStayIdentical),
        new(nameof(TPeakInvalidAndSubnanosecondMapsReject), TPeakInvalidAndSubnanosecondMapsReject),
        new(nameof(TPeakSignedIndependentRecoveryPreservesSamples), TPeakSignedIndependentRecoveryPreservesSamples),
    ];

    private static void TPeakMovesWithoutChangingAmplitudeOrSupport()
    {
        var original = TextbookElectrodeReference.CreateElectrodes();
        foreach (int peak in new[] { 100, 375, 500, 625, 900 })
        {
            var electrodes = TextbookElectrodeReference.CreateElectrodes(tShape: new(peak));
            for (int i = 0; i < 10; i++)
            {
                var t = electrodes[i].Bands[2];
                var old = original[i].Bands[2];
                Check.That(t.Trigger == old.Trigger && t.DelayNs == old.DelayNs && t.DurationNs == old.DurationNs && t.TableQ32.SequenceEqual(old.TableQ32),
                    "T shape changes only the shared phase map, not timing or electrode gains");
                long peakTime = (long)FixedPointMath.RoundDivideTiesToEven((Int128)t.DurationNs * peak, 1000);
                var source = EventWaveformComposition.Restore(new([t], [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
                long value = source.EvaluateAt(t.DelayNs + peakTime);
                Check.That(Math.Abs(value) == t.TableQ32.Max(v => Math.Abs(v)) && source.EvaluateAt(t.DelayNs) == 0 && source.EvaluateAt(t.DelayNs + t.DurationNs) == 0,
                    "the explicit peak retains electrode amplitude and both finite support endpoints");
                Check.That(t.PhasePoints![1].OffsetNs == peakTime, "all electrodes share the same peak time including zero/inverted gains");
            }
        }
    }

    private static void TPeakDefaultAndNonRepolarizationSamplesStayIdentical()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]);
        var normal = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(1_600_000_000, 400, 100);
        foreach (int peak in new[] { 250, 625, 900 })
        {
            var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u, tShape: new(peak))).GenerateBefore(1_600_000_000, 400, 100);
            bool different = false;
            for (int i = 0; i < samples.Count; i++)
            {
                bool same = samples[i].MicrovoltValues.SequenceEqual(normal[i].MicrovoltValues);
                Check.That(samples[i].Tick == normal[i].Tick, "T shape never changes acquisition ticks");
                if (peak == 625 || i % 200 < 85 || i % 200 >= 130) { Check.That(same, "reference peak and P/QRS/U outside T support remain identical"); }
                different |= !same;
                var leads = samples[i].ExactLeads;
                Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator, "shared shape retains exact lead identity");
            }
            Check.That(different == (peak != 625), "manual peak changes sampled T shape");
        }
    }

    private static void TPeakInvalidAndSubnanosecondMapsReject()
    {
        foreach (int peak in new[] { int.MinValue, 99, 901, int.MaxValue })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(tShape: new(peak)); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgTWave.InvalidShape"; }
            Check.That(rejected, "out-of-range peak maps reject before creating the source");
        }
        bool collapsed = false;
        try { TextbookElectrodeReference.CreateElectrodes(timing: new(100, 1, 1, 1, 2, 1), tShape: new(500)); }
        catch (EventWaveformException e) { collapsed = e.ReasonCode == "EcgTWave.InvalidShape"; }
        Check.That(collapsed, "nanosecond rounding cannot collapse a phase interval");
    }

    private static void TPeakSignedIndependentRecoveryPreservesSamples()
    {
        var plan = Plan with { IndependentVentricularPeriodNs = 1_100_000_000, VentricularElectricalOffsetNs = 900_000_000, VentricularMechanicalOffsetNs = 980_000_000 };
        var electrodes = TextbookElectrodeReference.CreateElectrodes(tWave: new([1000, 1000, 1000, 1000, -1000, 0, 1250, 1000, 1000, 1000]), tShape: new(375));
        var expected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(2_800_000_000, 700, 100);
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 175; step++)
        {
            actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
            source = ElectrodeSignalGenerator.Restore(source.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            "signed T phase maps retain late tails and exact samples through every-frame recovery");
    }
}
