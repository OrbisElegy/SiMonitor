// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RegularRhythmPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    public static Specification[] All =>
    [
        new(nameof(RegularPerfusionPreservesSupportsAndBoundsOverlap), RegularPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(RegularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), RegularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(RegularVenousAndRespiratoryComponentsRespectTheirClocks), RegularVenousAndRespiratoryComponentsRespectTheirClocks),
    ];

    private sealed record PerfusionCase(string Name, RegularPhysiologyPlan Plan, FixedPerfusionPreset Preset,
        long RrNs, long AtrialPeriodNs, long InvalidDurationNs)
    {
        public int PressureHistoryLimit { get; init; } = 350;
        public long TailTimeNs { get; init; } = 1_100_000_000;
        public int FirstGainPermille { get; init; } = 1000;
        public int SecondGainPermille { get; init; } = 1000;
        public Func<IReadOnlyList<EventWaveformBand>>? EctopicLeadII { get; init; }
    }

    private static PerfusionCase[] Cases =>
    [
        new("accelerated atrial", AcceleratedAtrialReference.CreatePlan(), FixedPerfusionPresets.AcceleratedSupraventricular,
            600_000_000, 600_000_000, 760_000_000) { EctopicLeadII = AcceleratedAtrialReference.CreateLeadIIBands },
        new("atrial escape", AtrialEscapeReference.CreatePlan(), FixedPerfusionPresets.SinglePulse,
            1_200_000_000, 1_200_000_000, 1_280_000_000)
        {
            PressureHistoryLimit = 180, TailTimeNs = 1_700_000_000,
            EctopicLeadII = AtrialEscapeReference.CreateLeadIIBands
        },
        new("accelerated junctional", AcceleratedJunctionalReference.CreatePlan(), FixedPerfusionPresets.AcceleratedSupraventricular,
            600_000_000, 800_000_000, 760_000_000) { FirstGainPermille = 840, SecondGainPermille = 630 },
        new("accelerated ventricular", AcceleratedVentricularReference.CreatePlan(), FixedPerfusionPresets.AcceleratedVentricular,
            750_000_000, 800_000_000, 760_000_000) { FirstGainPermille = 969, SecondGainPermille = 868 },
    ];

    private static void RegularPerfusionPreservesSupportsAndBoundsOverlap()
    {
        foreach (var item in Cases)
        {
            var preset = item.Preset;
            Check.That(preset.Pleth.PulseDurationNs == 512_000_000 && preset.Arterial.Morphology!.DurationNs == 600_000_000 &&
                preset.Pulmonary.Morphology!.DurationNs == 640_000_000, $"{item.Name}: full morphology supports retained");
            _ = VascularPressureSource.Create(item.Plan, preset.Arterial);
            _ = VascularPressureSource.Create(item.Plan, preset.Pulmonary);
            _ = preset.Venous.CreateChannel(item.Plan, ChannelId, 0);
            var invalidArterial = preset.Arterial with { Morphology = preset.Arterial.Morphology! with { DurationNs = item.InvalidDurationNs } };
            var invalidPulmonary = preset.Pulmonary with
            {
                Morphology = item.RrNs == 600_000_000
                    ? preset.Pulmonary.Morphology! with { MaximumPulseOverlap = 1 }
                    : preset.Pulmonary.Morphology! with { DurationNs = item.InvalidDurationNs }
            };
            foreach (var pressure in new[] { invalidArterial, invalidPulmonary })
            {
                try { VascularPressureSource.Create(item.Plan, pressure); }
                catch (EventWaveformException) { continue; }
                throw new InvalidOperationException($"{item.Name}: oversized pressure pulse accepted.");
            }
        }
    }

    private static void RegularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        foreach (var item in Cases)
        {
            var preset = item.Preset;
            var pleth = PlethRunoffSource.Create(item.Plan, preset.Pleth);
            Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000,
                $"{item.Name}: optical history bounded independently of run duration");
            // Pleth is independent of the pressure channel: evaluate each phase once.
            for (long phase = 0; phase < item.RrNs; phase += 10_000_000)
                Check.That(Math.Abs(pleth.EvaluateAt(300_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= Q,
                    $"{item.Name}: late optical samples remain phase-equivalent at {phase} ns");
            foreach (var pressure in new[] { preset.Arterial, preset.Pulmonary })
            {
                Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + item.RrNs - 1) / item.RrNs < item.PressureHistoryLimit,
                    $"{item.Name}: finite pressure history stays bounded");
                var source = VascularPressureSource.Create(item.Plan, pressure);
                var values = new List<long>();
                for (long phase = 0; phase < item.RrNs; phase += 10_000_000)
                {
                    long a = source.EvaluateAt(300_000_000_000 + phase);
                    long b = source.EvaluateAt(86_400_000_000_000 + phase);
                    Check.That(Math.Abs(a - b) <= Q && a > pressure.AsymptoticPressureCentiMmHg * Q && a < short.MaxValue * Q,
                        $"{item.Name}: pressure remains periodic and unsaturated after one day at {phase} ns");
                    values.Add(a);
                }
                Check.That(values.Max() - values.Min() > Q, $"{item.Name}: pressure pulses retain modulation");
            }
            var singlePlan = item.Plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
            // Independent atrial clocks use fixed golden filling gains against an unscaled single pulse.
            var singlePleth = item.AtrialPeriodNs != item.RrNs ? preset.Pleth with { UseCardiacFillingPerfusion = false } : preset.Pleth;
            var single = PlethRunoffSource.Create(singlePlan, singlePleth);
            long expected = (long)FixedPointMath.RoundDivideTiesToEven((Int128)single.EvaluateAt(item.TailTimeNs) * item.FirstGainPermille, 1000) +
                (long)FixedPointMath.RoundDivideTiesToEven((Int128)single.EvaluateAt(item.TailTimeNs - item.RrNs) * item.SecondGainPermille, 1000);
            Check.That(Math.Abs(pleth.EvaluateAt(item.TailTimeNs) - expected) <= 3,
                $"{item.Name}: overlapping optical pulses retain earlier tails and each beat's filling gain");
            var interrupted = VascularPressureSource.Create(singlePlan, preset.Arterial);
            Check.That(interrupted.EvaluateAt(800_000_000) > preset.Arterial.AsymptoticPressureCentiMmHg * Q,
                $"{item.Name}: missing subsequent ejection retains pressure runoff");
        }
    }

    private static void RegularVenousAndRespiratoryComponentsRespectTheirClocks()
    {
        foreach (var item in Cases)
        {
            var bands = item.Preset.Venous.CreateChannel(item.Plan, ChannelId, 0).Bands;
            var events = RegularPhysiologyTimeline.Start(item.Plan).AdvanceBefore(3_000_000_000, 100);
            var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
            var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
            var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
            Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical &&
                bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical),
                $"{item.Name}: CVP retains distinct atrial and ventricular triggers");
            for (long timeNs = 800_000_000; timeNs < 1_500_000_000; timeNs += 10_000_000)
            {
                Check.That(atrial.EvaluateAt(timeNs) == atrial.EvaluateAt(timeNs + item.AtrialPeriodNs), $"{item.Name}: a wave follows atrial period");
                Check.That(ventricular.EvaluateAt(timeNs) == ventricular.EvaluateAt(timeNs + item.RrNs), $"{item.Name}: c/x/v/y follow ventricular period");
                Check.That(whole.EvaluateAt(timeNs) == atrial.EvaluateAt(timeNs) + ventricular.EvaluateAt(timeNs),
                    $"{item.Name}: superposition preserves atrioventricular timing without artificial gain");
            }
            if (item.EctopicLeadII is not null)
            {
                var pPrime = EventWaveformComposition.Restore(new(item.EctopicLeadII()
                    .Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray(), events));
                Check.That(pPrime.EvaluateAt(40_000_000) < 0 && atrial.EvaluateAt(140_000_000) > 0,
                    $"{item.Name}: negative electrical P-prime does not invert mechanical a wave");
                Check.That(atrial.EvaluateAt(300_000_000) == 0 && ventricular.EvaluateAt(300_000_000) > 0 && ventricular.EvaluateAt(140_000_000) == 0,
                    $"{item.Name}: pressure components retain 80/240 ms trigger separation");
            }
            else
                Check.That(atrial.EvaluateAt(140_000_000) > 0 && atrial.EvaluateAt(515_000_000) == 0,
                    $"{item.Name}: a wave is not retriggered by each ventricle");
            var resp = new RespirationPlan(1000, 200).CreateChannel(item.Plan, ChannelId, 0);
            Check.That(resp.Bands[1].DurationNs == item.RrNs && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical,
                $"{item.Name}: cardiac artifact fits the ventricular cycle");
            var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
            for (long timeNs = 0; timeNs < 3_000_000_000; timeNs += 4_000_000)
                Check.That(Math.Abs(artifact.EvaluateAt(timeNs)) <= 200 * Q, $"{item.Name}: artifact stays within single-pulse amplitude");
        }
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), ChannelId, 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }
}
