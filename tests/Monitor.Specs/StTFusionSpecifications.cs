// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class StTFusionSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static EcgStTFusionPlan Region(EcgStTFusionContour contour) =>
        new([null, null, null, null, null, contour, contour, null, null, null]);
    public static Specification[] All =>
    [
        new(nameof(FusionHasOneContinuousCrownAcrossTheOldSTTBoundary), FusionHasOneContinuousCrownAcrossTheOldSTTBoundary),
        new(nameof(FusionPreservesUnselectedLeadsAndUnrelatedSamples), FusionPreservesUnselectedLeadsAndUnrelatedSamples),
        new(nameof(FusionCancelsTheOldWilsonRepolarization), FusionCancelsTheOldWilsonRepolarization),
        new(nameof(FusionValidatesTimingAndOwnsContourInputs), FusionValidatesTimingAndOwnsContourInputs),
    ];

    private static void FusionHasOneContinuousCrownAcrossTheOldSTTBoundary()
    {
        foreach (int position in new[] { 1, 50, 500, 999 })
        {
            foreach (int sign in new[] { -1, 1 })
            {
                var bands = TextbookElectrodeReference.CreateElectrodes(fusion: Region(new(sign * 200, sign * 500, position)))[5].Bands;
                var source = EventWaveformComposition.Restore(new([bands[2]], [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
                long peak = 80_000_000 + 280_000L * position;
                Check.That(source.EvaluateAt(60_000_000) == 0 && source.EvaluateAt(80_000_000) == sign * 200 * FixedPointMath.Q32One &&
                    source.EvaluateAt(peak) == sign * 500 * FixedPointMath.Q32One && source.EvaluateAt(360_000_000) == 0,
                    "fusion hits J, an independently placed peak and the unchanged QT endpoint");
                for (int step = 1; step <= 100; step++)
                {
                    long a = 80_000_000 + (peak - 80_000_000) * (step - 1) / 100;
                    long b = 80_000_000 + (peak - 80_000_000) * step / 100;
                    Check.That(sign * (source.EvaluateAt(b) - source.EvaluateAt(a)) >= 0, "no dip before unified crown");
                    a = peak + (360_000_000 - peak) * (step - 1) / 100;
                    b = peak + (360_000_000 - peak) * step / 100;
                    Check.That(sign * (source.EvaluateAt(b) - source.EvaluateAt(a)) <= 0, "no second T hump after unified crown");
                }
                Check.That(source.EvaluateAt(180_000_000) != 0, "the old T onset is inside the continuous fused contour");
                foreach (long boundary in new[] { 60_000_000L, 80_000_000, peak, 180_000_000, 360_000_000 })
                { Check.That(Math.Abs(source.EvaluateAt(boundary + 1) - source.EvaluateAt(boundary - 1)) < FixedPointMath.Q32One / 100, "fusion joins have no voltage step"); }
            }
        }
    }

    private static IReadOnlyList<ElectrodeWaveformPlan> Rich(EcgStTFusionPlan? fusion = null) =>
        TextbookElectrodeReference.CreateElectrodes(new(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 30, 40, 50, 60, 70]),
            tWave: new([1000, 1000, 1000, 1000, 1000, -1250, 0, 2000, 1000, 1000]), tShape: new(375),
            stSegment: new([0, 0, 0, 0, 200, 200, 200, 200, 200, 200],
                [0, 0, 0, 0, -100, -100, -100, -100, -100, -100],
                [0, 0, 0, 0, 200, 200, 200, 200, 200, 200]),
            pWave: new([null, null, null, null, new(150, -150), null, null, null, null, null]), fusion: fusion);

    private static void FusionPreservesUnselectedLeadsAndUnrelatedSamples()
    {
        var original = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Rich()).GenerateBefore(1_600_000_000, 400, 100);
        foreach (var fusion in new[] { new EcgStTFusionPlan(new EcgStTFusionContour?[10]), Region(new(200, 500, 50)) })
        {
            var actual = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Rich(fusion)).GenerateBefore(1_600_000_000, 400, 100);
            var changedLeads = new HashSet<int>();
            for (int i = 0; i < actual.Count; i++)
            {
                Check.That(actual[i].Tick == original[i].Tick, "fusion cannot change sampling clocks");
                for (int lead = 0; lead < 12; lead++)
                {
                    bool equal = actual[i].MicrovoltValues[lead] == original[i].MicrovoltValues[lead];
                    if (fusion.Electrodes[5] is null || lead is not (7 or 8) || i % 200 < 55 || i % 200 >= 130)
                    { Check.That(equal, "unselected leads and P/early QRS/U remain byte-identical, including existing ST/T settings"); }
                    if (!equal) { changedLeads.Add(lead); }
                }
                var leads = actual[i].ExactLeads;
                Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator, "fusion preserves exact projection identities");
            }
            Check.That(fusion.Electrodes[5] is null ? changedLeads.Count == 0 : changedLeads.SetEquals([7, 8]),
                "only explicitly selected V2/V3 change");
        }
    }

    private static void FusionCancelsTheOldWilsonRepolarization()
    {
        foreach (int position in new[] { 50, 500, 950 })
        {
            var electrodes = Rich(Region(new(200, 500, position)));
            var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.VentricularElectrical, 0) };
            var projected = ElectrodeWaveformComposition.Restore(new(electrodes, events));
            var target = EventWaveformComposition.Restore(new([electrodes[5].Bands[2]], events));
            for (long time = 80_000_000; time <= 360_000_000; time += 1_000_000)
            {
                long value = projected.EvaluateAt(time).Leads[EcgLead.V2].ToQ32();
                Check.That(Math.Abs(value - target.EvaluateAt(time)) <= 16,
                    "final V2 follows the unified contour within Q32 rounding, with no residual Wilson T hump");
            }
        }
    }

    private static void FusionValidatesTimingAndOwnsContourInputs()
    {
        foreach (var invalid in new[] { new EcgStTFusionPlan(null!), new EcgStTFusionPlan([]),
            Region(new(4001, 500, 500)), Region(new(0, -4001, 500)), Region(new(0, 0, 0)), Region(new(0, 0, 1000)) })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(fusion: invalid); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgStTFusion.InvalidPlan"; }
            Check.That(rejected, "invalid arrays, potentials and peak fractions fail with stable reason");
        }
        bool collapsed = false;
        try { TextbookElectrodeReference.CreateElectrodes(timing: new(100, 1, 2, 4, 5, 1), fusion: Region(new(200, 500, 1))); }
        catch (EventWaveformException e) { collapsed = e.ReasonCode == "EcgStTFusion.InvalidPlan"; }
        Check.That(collapsed, "nanosecond peak rounding may not collapse a segment");
        var inputs = Region(new(200, 500, 500)).Electrodes.ToArray();
        var accepted = TextbookElectrodeReference.CreateElectrodes(fusion: new(inputs));
        long[] before = accepted[5].Bands[2].TableQ32.ToArray();
        inputs[5] = new(-4000, 4000, 1);
        Check.That(accepted[5].Bands[2].TableQ32.SequenceEqual(before), "accepted fusion has owned immutable tables and maps");
    }


}
