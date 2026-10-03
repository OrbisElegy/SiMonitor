// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RhythmFillingAuditSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SlowEscapeFillingDependsOnAvPhaseRatherThanRatePenalty), SlowEscapeFillingDependsOnAvPhaseRatherThanRatePenalty),
        new(nameof(BlockedAndDelayedConductionUsesAcceptedMechanicalIntervals), BlockedAndDelayedConductionUsesAcceptedMechanicalIntervals),
        new(nameof(SinusFillingFollowsIrregularAndSeededIntervals), SinusFillingFollowsIrregularAndSeededIntervals),
        new(nameof(FlutterRetainsAbnormalMechanicalTransportDistinctFromFibrillation), FlutterRetainsAbnormalMechanicalTransportDistinctFromFibrillation),
        new(nameof(SvtConcurrentAtrialContractionDoesNotReceiveNormalKick), SvtConcurrentAtrialContractionDoesNotReceiveNormalKick),
        new(nameof(AuditedRhythmsRestoreWithoutInventingEjections), AuditedRhythmsRestoreWithoutInventingEjections)
    ];

    private static void SlowEscapeFillingDependsOnAvPhaseRatherThanRatePenalty()
    {
        foreach (var plan in new[] { CompleteAvBlockJunctionalReference.CreatePlan(), CompleteAvBlockVentricularReference.CreatePlan() })
        {
            Check.That(Enumerable.Range(0, 8).Select(i => CardiacFillingPerfusion.GainPermille(plan, (ulong)i))
                .SequenceEqual([1000, 750, 1000, 750, 1000, 750, 1000, 750]),
                "long RR permits passive filling; changing AV phase changes the atrial contribution");
            var aligned = plan with { VentricularElectricalOffsetNs = 160_000_000, VentricularMechanicalOffsetNs = 240_000_000 };
            Check.That(CardiacFillingPerfusion.GainPermille(aligned, 0) == 1000, "slow escape does not receive a tachycardia penalty");
        }
        var atrialEscape = PhysiologyIllustrationConfiguration.AtrialEscapePreset.ResolvePlan();
        Check.That(Enumerable.Range(0, 8).All(i => CardiacFillingPerfusion.GainPermille(atrialEscape, (ulong)i) == 1000),
            "slow conducted atrial escape keeps effective atrial assistance and bounded volume");
    }

    private static void BlockedAndDelayedConductionUsesAcceptedMechanicalIntervals()
    {
        var baseline = AcceleratedAtrialReference.CreatePlan() with { HeartPeriodNs = 500_000_000 };
        var wenckebach = baseline with
        {
            ConductionPattern = AvConductionPattern.WenckebachFourToThreeIllustration,
            VentricularConductionRatio = 4,
            ConductedBeatsPerGroup = 3
        };
        var events = RegularPhysiologyTimeline.Start(wenckebach).AdvanceBefore(5_000_000_000, 100)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        Check.That(events.Take(4).Select(e => e.CycleIndex).SequenceEqual([0UL, 1UL, 2UL, 4UL]), "dropped P retains original ordinal");
        Check.That(events.Take(4).Select(e => CardiacFillingPerfusion.GainPermille(wenckebach, e.CycleIndex))
            .SequenceEqual([700, 817, 700, 1000]), "actual RR and progressively delayed ventricular systole jointly determine filling");
        var mobitz = baseline with
        {
            HeartPeriodNs = 800_000_000,
            ConductionPattern = AvConductionPattern.MobitzTwoFourToThreeIllustration,
            VentricularConductionRatio = 4,
            ConductedBeatsPerGroup = 3
        };
        Check.That(CardiacAtFirstAfterDrop(mobitz) == 1000, "long pause restores bounded filling without generating a dropped ejection");
        int CardiacAtFirstAfterDrop(RegularPhysiologyPlan plan) => CardiacFillingPerfusion.GainPermille(plan, 4);
        var highGrade = baseline with { VentricularConductionRatio = 3 };
        Check.That(CardiacAtFirstAfterDrop(highGrade) == 1000, "high-grade conducted beats retain atrial timing and adequate filling");
    }

    private static void SinusFillingFollowsIrregularAndSeededIntervals()
    {
        var irregular = SinusArrhythmiaReference.CreatePlan();
        Check.That(Enumerable.Range(0, 8).Select(i => CardiacFillingPerfusion.GainPermille(irregular, (ulong)i))
            .SequenceEqual([1000, 1000, 1000, 840, 1000, 1000, 1000, 840]), "short sinus interval reduces the correct beat, not the preceding one");
        var arrest = SinusArrestReference.CreatePlan();
        Check.That(CardiacAfterPause(arrest) == 1000, "pause filling is capped; no unbounded post-pause pressure surge");
        int CardiacAfterPause(RegularPhysiologyPlan plan) => CardiacFillingPerfusion.GainPermille(plan, 3);
        foreach (int bpm in new[] { 30, 75, 120, 180 })
        {
            var rate = new SeededCardiacRate(bpm, new string('a', 64), 50);
            var plan = (PhysiologyIllustrationConfiguration.Default with { SeededRate = rate }).ResolvePlan();
            for (ulong i = 0; i < 512; i++)
            {
                int gain = CardiacFillingPerfusion.GainPermille(plan, i);
                Check.That(gain is > 0 and <= 1000 && gain == rate.EjectionGainPermille(i),
                    "sinus source and indexed rate helper agree on actual RR and shortened AV timing");
                if (bpm == 30) { Check.That(gain == 1000, "bradycardia keeps adequate per-beat filling"); }
            }
        }
    }

    private static void FlutterRetainsAbnormalMechanicalTransportDistinctFromFibrillation()
    {
        Check.That(FlutterOneToOnePerfusionReference.StrokeVolumePermille == 175, "short filling window admits part of the flutter contraction");
        foreach (int ratio in new[] { 2, 3, 4 })
        {
            var plan = AtrialFlutterReference.CreatePlan(ratio);
            long atrialNs = AtrialFlutterMechanics.EffectiveAtrialDurationNs(plan, 0, 240_000_000);
            int flutterGain = ConductedFlutterPerfusion.GainPermille(plan, 0);
            int passiveGain = CardiacFillingPerfusion.StrokeVolumePermille(ratio * 200_000_000L, 0, 240_000_000);
            int normalGain = CardiacFillingPerfusion.StrokeVolumePermille(ratio * 200_000_000L, 120_000_000, 240_000_000);
            Check.That(atrialNs == 48_000_000 && flutterGain > passiveGain && flutterGain < normalGain,
                "organized flutter retains partial assistance without granting a normal atrial kick");
            var channel = FlutterOneToOnePerfusionReference.Venous.CreateChannel(plan, PhysiologyIllustrationSource.ChannelId(6), 0);
            var flutterBand = channel.Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
            Check.That(flutterBand.DurationNs == 80_000_000 && flutterBand.DelayNs == 80_000_000,
                "CVP carries a short flutter-specific mechanical component");
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(2_000_000_000, 100);
            var component = EventWaveformComposition.Restore(new([flutterBand], events));
            Check.That(component.EvaluateAt(120_000_000) > 0 && component.EvaluateAt(120_000_000) == component.EvaluateAt(320_000_000),
                "flutter pressure contractions follow the atrial clock, including nonconducted cycles");
        }
        var afPlan = AtrialFibrillationReference.CreatePlan();
        var afVenous = FlutterOneToOnePerfusionReference.Venous.CreateChannel(afPlan, PhysiologyIllustrationSource.ChannelId(6), 0);
        Check.That(afVenous.Bands.All(b => b.Trigger != PhysiologyCycleEventKind.AtrialElectrical) &&
            AtrialFibrillationPerfusion.GainPermille(afPlan.ConductionPattern, 0) == 750,
            "fibrillation has no invented coordinated atrial contraction or flutter pressure component");
    }

    private static void SvtConcurrentAtrialContractionDoesNotReceiveNormalKick()
    {
        var svt = SupraventricularTachycardiaReference.CreatePlan();
        Check.That(CardiacGain(0) == 235 && CardiacGain(1000) == 235, "SVT is filling-limited and its simultaneous atrial contraction is ineffective");
        int CardiacGain(ulong ordinal) => CardiacFillingPerfusion.GainPermille(svt, ordinal);
        var interrupted = svt with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2, MechanicalDurationCycles = 2 };
        Check.That(CardiacFillingPerfusion.GainPermille(interrupted, 4) > 235 &&
            CardiacFillingPerfusion.GainPermille(interrupted, 5) == 235, "post-interruption SVT uses the longer interval once, then resumes fast filling");
    }

    private static void AuditedRhythmsRestoreWithoutInventingEjections()
    {
        foreach (var config in new[] { PhysiologyIllustrationConfiguration.SinusArrhythmiaPreset,
            PhysiologyIllustrationConfiguration.SinusArrestPreset, PhysiologyIllustrationConfiguration.VariableFlutter,
            PhysiologyIllustrationConfiguration.Fibrillation(), PhysiologyIllustrationConfiguration.SvtPreset })
        {
            var original = PhysiologyIllustrationSource.Create(config);
            for (long t = 200_000_000; t <= 4_000_000_000; t += 200_000_000) { original.AdvanceTo(t, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(original.CaptureState());
            for (long t = 4_200_000_000; t <= 8_000_000_000; t += 200_000_000)
            {
                var a = original.AdvanceTo(t, 50, 1, 100); var b = restored.AdvanceTo(t, 50, 1, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second)), "filling and abnormal atrial mechanics restore exact acquired output");
            }
        }
        var stopped = CompleteAvBlockVentricularReference.CreatePlan() with { VentricularMechanicalEnabled = false };
        var pressure = VascularPressureSource.Create(stopped, FixedPerfusionPresets.FillingSinglePulse.Arterial);
        Check.That(pressure.EvaluateAt(60_000_000_000) < pressure.EvaluateAt(5_000_000_000) &&
            PlethRunoffSource.Create(stopped, FixedPerfusionPresets.FillingSinglePulse.Pleth).EvaluateAt(60_000_000_000) == 0,
            "filling never converts absent mechanical activity into an ejection");
    }
}
