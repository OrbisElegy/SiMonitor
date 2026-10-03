// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PWaveComponentSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(PComponentsProduceRoundedNotchedAndBiphasicSources), PComponentsProduceRoundedNotchedAndBiphasicSources),
        new(nameof(PComponentsPreserveOtherBandsAndProjection), PComponentsPreserveOtherBandsAndProjection),
        new(nameof(PComponentsValidateAndSnapshotInputs), PComponentsValidateAndSnapshotInputs),
    ];

    private static EcgPWavePlan Single(int early, int late) =>
        new(Enumerable.Range(0, 10).Select(i => i == 4 ? new EcgPWaveComponents(early, late) : null).ToArray());

    private static void PComponentsProduceRoundedNotchedAndBiphasicSources()
    {
        foreach (var (early, late) in new[] { (150, 150), (150, -150), (-150, 150), (0, 0), (4000, -4000) })
        {
            var bands = TextbookElectrodeReference.CreateElectrodes(pWave: Single(early, late))[4].Bands;
            var source = EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.AtrialElectrical, 0)]));
            Check.That(source.EvaluateAt(31_250_000) == early * FixedPointMath.Q32One &&
                source.EvaluateAt(68_750_000) == late * FixedPointMath.Q32One, "separate component peaks retain exact signed amplitudes");
            Check.That(source.EvaluateAt(0) == 0 && source.EvaluateAt(100_000_000) == 0 &&
                source.EvaluateAt(160_000_000) == 0, "P remains inside original P support, not PR");
            if (early == late && early > 0)
            { Check.That(source.EvaluateAt(50_000_000) < source.EvaluateAt(31_250_000), "overlapping equal rounded lobes produce a central notch"); }
            if (early == -late)
            { Check.That(Math.Abs(source.EvaluateAt(50_000_000)) <= 2, "opposite lobes cross the isoelectric line continuously"); }
            // Zero-slope endpoints from the original rounded LUT: no finite
            // voltage step at the late onset or early termination.
            foreach (long boundary in new[] { 0L, 37_500_000, 62_500_000, 100_000_000 })
            {
                long before = source.EvaluateAt(Math.Max(0, boundary - 1));
                long after = source.EvaluateAt(boundary + 1);
                Check.That(Math.Abs(after - before) < FixedPointMath.Q32One / 100, "lobe support boundaries have no abrupt voltage jump");
            }
        }
    }

    private static void PComponentsPreserveOtherBandsAndProjection()
    {
        var baseline = TextbookElectrodeReference.CreateElectrodes();
        var untouched = TextbookElectrodeReference.CreateElectrodes(pWave: new(new EcgPWaveComponents?[10]));
        var changed = TextbookElectrodeReference.CreateElectrodes(pWave: Single(150, -150));
        var original = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, baseline).GenerateBefore(1_600_000_000, 400, 100);
        var normal = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, untouched).GenerateBefore(1_600_000_000, 400, 100);
        var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, changed).GenerateBefore(1_600_000_000, 400, 100);
        bool differs = false;
        for (int index = 0; index < samples.Count; index++)
        {
            Check.That(original[index].MicrovoltValues.SequenceEqual(normal[index].MicrovoltValues), "null electrode components preserve old bytes");
            Check.That(samples[index].Tick == original[index].Tick, "P shape cannot change sample clocks");
            for (int lead = 0; lead < 12; lead++)
            {
                bool same = samples[index].MicrovoltValues[lead] == original[index].MicrovoltValues[lead];
                if (lead != (int)EcgLead.V1 || index % 200 >= 25) { Check.That(same, "only C1 P changes, not other electrodes or QRS/ST/T/U"); }
                differs |= !same;
            }
            var leads = samples[index].ExactLeads;
            Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator, "Einthoven relation remains exact");
        }
        Check.That(differs, "biphasic morphology reaches acquired lead samples");
    }

    private static void PComponentsValidateAndSnapshotInputs()
    {
        foreach (var invalid in new EcgPWavePlan[] { new(null!), new([]), Single(-4001, 0), Single(0, 4001) })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(pWave: invalid); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgPWave.InvalidPlan"; }
            Check.That(rejected, "malformed arrays/amplitudes reject with stable reason");
        }
        foreach (long duration in new[] { 1L, 2L })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(timing: new(100, duration, 10, 10, 30, 10), pWave: Single(100, -100)); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgPWave.InvalidPlan"; }
            Check.That(rejected, "collapsed rounded overlap rejects");
        }
        var inputs = new EcgPWaveComponents?[10];
        inputs[4] = new(100, -100);
        var accepted = TextbookElectrodeReference.CreateElectrodes(pWave: new(inputs));
        long peak = accepted[4].Bands[0].TableQ32.Max();
        inputs[4] = new(4000, 4000);
        Check.That(accepted[4].Bands[0].TableQ32.Max() == peak, "accepted tables own source amplitudes");
        var odd = TextbookElectrodeReference.CreateElectrodes(timing: new(100, 7, 10, 10, 30, 10), pWave: Single(100, -100))[4].Bands;
        Check.That(odd[0].DurationNs == 4 && odd[^1].DelayNs == 3 && odd[^1].DurationNs == 4, "ties-even nanosecond timing retains exact P endpoint");
    }

}
