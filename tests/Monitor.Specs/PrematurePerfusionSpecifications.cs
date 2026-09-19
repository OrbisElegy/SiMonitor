// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PrematurePerfusionSpecifications
{
    public static Specification[] All => [new(nameof(WeakBeatMorphologyUsesMatchingReferenceInput), WeakBeatMorphologyUsesMatchingReferenceInput), new(nameof(PlethFullSupportSurvivesPrematureOverlap), PlethFullSupportSurvivesPrematureOverlap), new(nameof(BeatPerfusionSharesGainsWithoutInventingEjection), BeatPerfusionSharesGainsWithoutInventingEjection), new(nameof(SupraventricularPerfusionPreservesBlockedSlotsAndRecovery), SupraventricularPerfusionPreservesBlockedSlotsAndRecovery), new(nameof(BeatDurationsPreserveNormalSupportAndHalfOpenEndpoints), BeatDurationsPreserveNormalSupportAndHalfOpenEndpoints)];
    private static void WeakBeatMorphologyUsesMatchingReferenceInput()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(PrematureVentricularReference.IsPattern)
            .Where(mode => mode != AvConductionPattern.ShortCoupledRonTPvcIllustration))
        {
            var physiology = PrematureVentricularReference.CreatePlan(mode);
            foreach (bool pulmonary in new[] { false, true })
            {
                var plan = pulmonary ? new VascularPressurePlan(40_000_000, 200_000_000, 700_000_000, 1000, 500, 5000, UsePrematureBeatPerfusion: true) :
                    new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true);
                var raw = VascularPressureSource.Create(physiology, plan);
                var zeroShape = VascularPressureSource.Create(physiology, plan with
                { Morphology = new(pulmonary ? VascularPressureMorphologyKind.PulmonaryArtery : VascularPressureMorphologyKind.Arterial, 420_000_000, 0) });
                var events = RegularPhysiologyTimeline.Start(physiology).AdvanceBefore(8_000_000_000, 200)
                    .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical && PrematureBeatPerfusion.GainPermille(mode, e.CycleIndex) == 200);
                foreach (var beat in events)
                {
                    for (long age = 0; age <= 400_000_000; age += 8_000_000)
                    {
                        long time = beat.SimTimeNs + plan.TransitDelayNs + age;
                        // Independent continuous-time reference. With these parameters
                        // the weak input equilibrium is below the nominal onset pressure,
                        // so the reference only decays and is clamped to that onset.
                        double period = 500_000_000, tau = plan.TimeConstantNs;
                        double onset = plan.EjectionEquilibriumCentiMmHg *
                            (Math.Exp(-(period - plan.EjectionDurationNs) / tau) - Math.Exp(-period / tau)) /
                            (1 - Math.Exp(-period / tau));
                        Check.That(plan.EjectionEquilibriumCentiMmHg * 0.2 < onset, "weak input cannot raise the reference above its onset");
                        Check.That(zeroShape.EvaluateAt(time) == raw.EvaluateAt(time),
                            "zero-height weak-beat shape must not depress the reservoir through a full-strength denominator");
                    }
                }
                var shapedPlan = plan with { Morphology = new(pulmonary ? VascularPressureMorphologyKind.PulmonaryArtery : VascularPressureMorphologyKind.Arterial, 420_000_000, pulmonary ? 1500 : 4000) };
                var source = PhysiologySignalGenerator.Start(physiology, "AcqPressure125@1", 1, [], shapedPlan);
                source.GenerateBefore(2_560_000_000, 320, 200);
                var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(8_000_000_000, 1000, 200).SequenceEqual(restored.GenerateBefore(8_000_000_000, 1000, 200)),
                    "weighted morphology recovery preserves actual pressure samples");
            }
        }
    }

    private static void PlethFullSupportSurvivesPrematureOverlap()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(PrematureBeatPerfusion.IsPattern))
        {
            var plan = PrematureVentricularReference.IsPattern(mode) ? PrematureVentricularReference.CreatePlan(mode) :
                PrematureJunctionalReference.IsPattern(mode) ? PrematureJunctionalReference.CreatePlan() with { ConductionPattern = mode } :
                PrematureAtrialReference.CreatePlan() with { ConductionPattern = mode };
            var band = new PlethPulsePlan(80_000_000, 512_000_000, 1250).CreateBands()[0] with { EjectionIllustration = mode };
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 200)
                .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            var combined = EventWaveformComposition.Restore(new([band], events));
            var isolated = events.Select(e => EventWaveformComposition.Restore(new([band], [e]))).ToArray();
            for (long time = 0; time < 8_000_000_000; time += 8_000_000)
            {
                long sum = isolated.Sum(wave => wave.EvaluateAt(time));
                Check.That(combined.EvaluateAt(time) == sum && sum >= 0 && sum < 2500 * FixedPointMath.Q32One,
                    "full Pleth contributions sum without clipping or truncating prior tails");
            }
            Check.That(isolated[0].EvaluateAt(800_000_000) > 0 && isolated[0].EvaluateAt(832_000_000) == 0,
                "startup normal pulse retains512ms half-open support after80ms transit");
            for (int i = 1; i < events.Length; i++)
            {
                long overlapTime = events[i].SimTimeNs + 88_000_000;
                if (events[i].SimTimeNs - events[i - 1].SimTimeNs == 500_000_000 &&
                    PrematureBeatPerfusion.GainPermille(mode, events[i - 1].CycleIndex) >= 800)
                {
                    Check.That(isolated[i - 1].EvaluateAt(overlapTime) > 0 && isolated[i].EvaluateAt(overlapTime) > 0,
                    "native tick inside12ms overlap retains both previous normal tail and new premature pulse");
                }
            }
            var source = PhysiologySignalGenerator.Start(plan, "AcqPleth125@1", 1, [band]);
            var expected = source.GenerateBefore(8_000_000_000, 1000, 200);
            var split = PhysiologySignalGenerator.Start(plan, "AcqPleth125@1", 1, [band]);
            var prefix = split.GenerateBefore(824_000_000, 103, 200);
            string checkpoint = System.Text.Json.JsonSerializer.Serialize(split.CaptureState());
            try { split.GenerateBefore(8_000_000_000, 1, 200); throw new InvalidOperationException("Sample limit ignored."); }
            catch (PhysiologySignalException) { }
            Check.That(checkpoint == System.Text.Json.JsonSerializer.Serialize(split.CaptureState()), "failed generation preserves overlapping tail state");
            split = PhysiologySignalGenerator.Restore(split.CaptureState());
            Check.That(expected.SequenceEqual(prefix.Concat(split.GenerateBefore(8_000_000_000, 1000, 200))),
                "native acquisition and recovery preserve full support and original beat gains");
        }
    }

    private static void BeatPerfusionSharesGainsWithoutInventingEjection()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(PrematureVentricularReference.IsPattern))
        {
            var physiology = PrematureVentricularReference.CreatePlan(mode);
            int count = PrematureVentricularReference.BeatsPerGroup(mode);
            bool shortCoupled = mode == AvConductionPattern.ShortCoupledRonTPvcIllustration;
            Check.That(PrematureBeatPerfusion.GainPermille(mode, 0) == 800, "no phantom recovery at startup");
            Check.That(PrematureBeatPerfusion.GainPermille(mode, (ulong)count - 1) == (shortCoupled ? 0 : 200), "ectopic authored output");
            Check.That(PrematureBeatPerfusion.GainPermille(mode, (ulong)count) == (mode == AvConductionPattern.InterpolatedPvcIllustration ? 800 : 1000), "recovery enhancement explicitly absent for interpolated example");
            var plan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true);
            var pressure = VascularPressureSource.Create(physiology, plan);
            var events = RegularPhysiologyTimeline.Start(physiology).AdvanceBefore(8_000_000_000, 200).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            for (long t = 0; t < 8_000_000_000; t += 19_000_000)
            {
                double time = Math.Max(0, t - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-time / tau);
                foreach (var beat in events.Where(e => e.SimTimeNs <= time))
                {
                    ulong slot = beat.CycleIndex % (ulong)count;
                    bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
                    double gain = ectopic ? shortCoupled ? 0 : 0.2 : beat.CycleIndex > 0 && mode != AvConductionPattern.InterpolatedPvcIllustration && (slot == 0 || count == 8 && slot == 4) ? 1 : 0.8;
                    double age = time - beat.SimTimeNs;
                    long duration = ectopic ? 180_000_000 : 240_000_000;
                    expected += 30000 * gain * (1 - Math.Exp(-Math.Min(age, duration) / tau)) * Math.Exp(-Math.Max(0, age - duration) / tau);
                }
                Check.That(Math.Abs((double)pressure.EvaluateAt(t) / FixedPointMath.Q32One - expected) < 0.01, "independent beat-weighted pressure sum including absent ectopic input");
            }
            var band = new PlethPulsePlan(80_000_000, 512_000_000, 1250).CreateBands()[0] with { EjectionIllustration = mode };
            foreach (var beat in events.Take(2 * count))
            {
                var weighted = EventWaveformComposition.Restore(new([band], [beat]));
                var reference = EventWaveformComposition.Restore(new([band with { EjectionIllustration = null, DurationNs = PrematureBeatPerfusion.DurationNs(mode, beat.CycleIndex, band.DurationNs) }], [beat]));
                long t = beat.SimTimeNs + 200_000_000;
                long expected = (long)FixedPointMath.RoundDivideTiesToEven((Int128)reference.EvaluateAt(t) * PrematureBeatPerfusion.GainPermille(mode, beat.CycleIndex), 1000);
                Check.That(weighted.EvaluateAt(t) == expected, "Pleth uses same beat strength and retains normal support");
            }
            var source = PhysiologySignalGenerator.Start(physiology, "AcqPressure125@1", 1, [], plan);
            source.GenerateBefore(2_056_000_000, 1000, 200);
            var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(8_000_000_000, 1000, 200).SequenceEqual(restored.GenerateBefore(8_000_000_000, 1000, 200)), "pressure recovery preserves strength phase");
            var shaped = VascularPressureSource.Create(physiology, plan with { Morphology = new(VascularPressureMorphologyKind.Arterial, shortCoupled ? 600_000_000 : 420_000_000, 4000) });
            Check.That(shaped.EvaluateAt(long.MaxValue) >= 0, "bounded remote shaped reconstruction");
        }
        var ordinary = PrematureVentricularReference.CreatePlan();
        var shortPlan = PrematureVentricularReference.CreatePlan(AvConductionPattern.ShortCoupledRonTPvcIllustration);
        try { VascularPressureSource.Create(shortPlan, new(80_000_000, 801_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true)); throw new InvalidOperationException("Unsafe support accepted."); }
        catch (EventWaveformException) { }
        var wrongBand = new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands()[0] with { EjectionIllustration = AvConductionPattern.ShortCoupledRonTPvcIllustration };
        try { PhysiologySignalGenerator.Start(ordinary, "AcqPleth125@1", 1, [wrongBand]); throw new InvalidOperationException("Mismatched beat phase accepted."); }
        catch (PhysiologySignalException) { }
        try { EventWaveformComposition.Restore(new([wrongBand with { Trigger = PhysiologyCycleEventKind.VentricularElectrical }], [])); throw new InvalidOperationException("Electrical waveform gained perfusion weighting."); }
        catch (EventWaveformException) { }
    }
    private static void SupraventricularPerfusionPreservesBlockedSlotsAndRecovery()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(m => PrematureAtrialReference.IsPattern(m) || PrematureJunctionalReference.IsPattern(m)))
        {
            bool junctional = PrematureJunctionalReference.IsPattern(mode);
            bool blocked = mode == AvConductionPattern.BlockedPrematureAtrialIllustration;
            long group = junctional ? 3_200_000_000 : 3_100_000_000;
            var physiology = junctional ? PrematureJunctionalReference.CreatePlan(mode) : PrematureAtrialReference.CreatePlan(blocked) with { ConductionPattern = mode };
            var all = RegularPhysiologyTimeline.Start(physiology).AdvanceBefore(2 * group, 100);
            var actual = all.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            long[] offsets = blocked ? [240_000_000, 1_040_000_000, 1_840_000_000] : [240_000_000, 1_040_000_000, 1_840_000_000, 2_340_000_000];
            Check.That(actual.Select(e => e.SimTimeNs).SequenceEqual(offsets.Concat(offsets.Select(t => t + group))), "explicit mechanical times, blocked slot absent in both groups");
            Check.That(PrematureBeatPerfusion.GainPermille(mode, 0) == 800 && PrematureBeatPerfusion.GainPermille(mode, 3) == (blocked ? 0 : 400) && PrematureBeatPerfusion.GainPermille(mode, 4) == 1000, "original slot4 remains recovery even when slot3 never ejects");
            var plan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true);
            var pressure = VascularPressureSource.Create(physiology, plan);
            for (long t = 0; t < 2 * group; t += 17_000_000)
            {
                double time = Math.Max(0, t - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-time / tau);
                for (int g = 0; g < 2; g++)
                {
                    for (int slot = 0; slot < offsets.Length; slot++)
                    {
                        double age = time - (g * group + offsets[slot]);
                        if (age < 0) { continue; }
                        double gain = slot == 3 ? 0.4 : g > 0 && slot == 0 ? 1 : 0.8;
                        long duration = slot == 3 ? 180_000_000 : 240_000_000;
                        expected += 30000 * gain * (1 - Math.Exp(-Math.Min(age, duration) / tau)) * Math.Exp(-Math.Max(0, age - duration) / tau);
                    }
                }
                Check.That(Math.Abs((double)pressure.EvaluateAt(t) / FixedPointMath.Q32One - expected) < 0.01, "independent PAC/PJC pressure sum retains runoff through omitted beat");
            }
            var band = new PlethPulsePlan(80_000_000, 420_000_000, 1250).CreateBands()[0] with { EjectionIllustration = mode };
            foreach (var beat in actual)
            {
                int gain = beat.CycleIndex % 4 == 3 ? 400 : beat.CycleIndex == 4 ? 1000 : 800;
                var source = EventWaveformComposition.Restore(new([band], [beat]));
                var reference = EventWaveformComposition.Restore(new([band with { EjectionIllustration = null, DurationNs = PrematureBeatPerfusion.DurationNs(mode, beat.CycleIndex, band.DurationNs) }], [beat]));
                long t = beat.SimTimeNs + 180_000_000;
                Check.That(source.EvaluateAt(t) == FixedPointMath.RoundDivideTiesToEven((Int128)reference.EvaluateAt(t) * gain, 1000), "same weights drive Pleth independently of P timing or QRS shape");
            }
            if (blocked)
            {
                var synthetic = new PhysiologyCycleEvent(2_340_000_000, PhysiologyCycleEventKind.VentricularMechanical, 3);
                Check.That(EventWaveformComposition.Restore(new([band], [synthetic])).EvaluateAt(2_520_000_000) == 0, "blocked-slot strength is zero even for isolated band evaluation");
            }
            foreach (long boundary in new[] { 2_304_000_000L, 2_344_000_000, group + 248_000_000 })
            {
                var source = PhysiologySignalGenerator.Start(physiology, "AcqPressure125@1", 1, [], plan);
                source.GenerateBefore(boundary, 1000, 100);
                var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(2 * group, 1000, 100).SequenceEqual(restored.GenerateBefore(2 * group, 1000, 100)), "restore preserves blocked-slot and recovery ordinals");
            }
            var shaped = VascularPressureSource.Create(physiology, plan with { Morphology = new(VascularPressureMorphologyKind.Arterial, 420_000_000, 4000) });
            Check.That(shaped.EvaluateAt(long.MaxValue) >= 0, "late indexed recovery stays bounded");
        }
        try { VascularPressureSource.Create(PrematureAtrialReference.CreatePlan() with { ConductionPattern = AvConductionPattern.FixedPr }, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true)); throw new InvalidOperationException("Unrelated rhythm accepted perfusion illustration."); }
        catch (EventWaveformException) { }
    }

    private static void BeatDurationsPreserveNormalSupportAndHalfOpenEndpoints()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(PrematureBeatPerfusion.IsPattern))
        {
            int count = PrematureVentricularReference.IsPattern(mode) ? PrematureVentricularReference.BeatsPerGroup(mode) : 4;
            for (ulong ordinal = 0; ordinal < (ulong)(2 * count); ordinal++)
            {
                ulong slot = ordinal % (ulong)count;
                bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
                long expected = ectopic ? 300_000_000 : 400_000_000;
                Check.That(PrematureBeatPerfusion.DurationNs(mode, ordinal, 400_000_000) == expected, "only original ectopic slots shorten; recovery and startup retain baseline");
                var beat = new PhysiologyCycleEvent(1_000_000_000, PhysiologyCycleEventKind.VentricularMechanical, ordinal);
                var band = new EventWaveformBand(beat.Kind, 80_000_000, 400_000_000, [0, 100 * FixedPointMath.Q32One, 200 * FixedPointMath.Q32One, 100 * FixedPointMath.Q32One], EjectionIllustration: mode);
                var wave = EventWaveformComposition.Restore(new([band], [beat]));
                long start = beat.SimTimeNs + band.DelayNs;
                Check.That(wave.EvaluateAt(start - 1) == 0 && wave.EvaluateAt(start + expected) == 0, "transit delay retained and per-beat support half-open");
                if (PrematureBeatPerfusion.GainPermille(mode, ordinal) != 0)
                {
                    Check.That(wave.EvaluateAt(start + expected - 1000) > 0, "tail persists until selected endpoint");
                    long peak = wave.EvaluateAt(start + expected / 2);
                    Check.That(peak == 200 * FixedPointMath.Q32One * PrematureBeatPerfusion.GainPermille(mode, ordinal) / 1000, "duration changes phase, not gain or peak");
                    var mapped = EventWaveformComposition.Restore(new([band with { PhasePoints = [new(0, 0), new(200_000_000, 1), new(400_000_000, 4)] }], [beat]));
                    Check.That(mapped.EvaluateAt(start + expected / 2) == peak / 2, "per-beat duration preserves authored phase-map landmark");
                    Check.That(EventWaveformComposition.Restore(mapped.CaptureState()).EvaluateAt(start + expected / 2) == peak / 2, "phase-map recovery preserves local duration");
                }
            }
            Check.That(PrematureBeatPerfusion.DurationNs(mode, 3, 1) == 1 && PrematureBeatPerfusion.DurationNs(mode, 3, long.MaxValue) > 0, "positive support and Int128 overflow safety");
        }
        var plan = PrematureAtrialReference.CreatePlan();
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, Morphology: new(VascularPressureMorphologyKind.Arterial, 420_000_000, 4000), UsePrematureBeatPerfusion: true));
        long endpoint = 2_340_000_000 + 80_000_000 + 315_000_000;
        Check.That(Math.Abs(pressure.EvaluateAt(endpoint) - pressure.EvaluateAt(endpoint - 1)) < FixedPointMath.Q32One, "shortened morphology rejoins reservoir continuously at its own endpoint");
        try { PrematureBeatPerfusion.DurationNs(AvConductionPattern.PrematureAtrialIllustration, 3, 0); throw new InvalidOperationException("Empty duration accepted."); }
        catch (ArgumentOutOfRangeException) { }
    }

}
