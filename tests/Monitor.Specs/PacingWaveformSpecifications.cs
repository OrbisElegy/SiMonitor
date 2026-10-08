// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class PacingWaveformSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PacingSeparatesOutputCaptureAndMechanics), PacingSeparatesOutputCaptureAndMechanics),
        new(nameof(PacingProjectionAndRecoveryAreDeterministic), PacingProjectionAndRecoveryAreDeterministic),
        new(nameof(PacingFailureDoesNotInventPerfusion), PacingFailureDoesNotInventPerfusion),
        new(nameof(PacingRejectsConflictsAndPreservesCursor), PacingRejectsConflictsAndPreservesCursor),
        new(nameof(DefibrillationHasFiniteSignedPhases), DefibrillationHasFiniteSignedPhases),
    ];

    private static void PacingSeparatesOutputCaptureAndMechanics()
    {
        foreach (var mode in Enum.GetValues<PacingIllustration>())
        {
            var events = RegularPhysiologyTimeline.Start(PacingReference.CreatePlan(mode)).AdvanceBefore(4_000_000_000, 100);
            long[] Times(PhysiologyCycleEventKind kind) => events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs).ToArray();
            long[] pulses = Times(PhysiologyCycleEventKind.VentricularPacingPulse);
            long[] electrical = Times(PhysiologyCycleEventKind.VentricularElectrical);
            long[] mechanical = Times(PhysiologyCycleEventKind.VentricularMechanical);
            int expectedBeats = mode switch
            {
                PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure => 0,
                PacingIllustration.VentricularOversensing or PacingIllustration.IntermittentVentricularNoncapture => 2,
                _ => 4,
            };
            Check.That(electrical.Length == expectedBeats && mechanical.SequenceEqual(electrical.Select(t => t + 80_000_000)), mode + " mechanics require capture");
            Check.That(pulses.Length == (mode is PacingIllustration.AtrialAai or PacingIllustration.VentricularOutputFailure ? 0 : mode == PacingIllustration.VentricularOversensing ? 2 : 4), mode + " output is independent of capture");
            Check.That(Times(PhysiologyCycleEventKind.AtrialElectrical).Length == (mode == PacingIllustration.AtrialNoncapture ? 0 : mode is PacingIllustration.RightVentricularVvi or PacingIllustration.LeadlessRightVentricular or PacingIllustration.TemporaryTransvenous ? 5 : 4), mode + " atrial capture or independent sinus events");
            if (mode == PacingIllustration.SelectiveHis)
            { Check.That(electrical.Zip(pulses).All(p => p.First - p.Second == 40_000_000), "selective His has an isoelectric stimulus-QRS interval"); }
            if (mode == PacingIllustration.VentricularUndersensing)
            { Check.That(pulses.Zip(electrical).All(p => p.First > p.Second && p.First < p.Second + 80_000_000), "unsensed intrinsic QRS receives an inappropriate refractory stimulus"); }
        }
    }

    private static void PacingProjectionAndRecoveryAreDeterministic()
    {
        foreach (var mode in Enum.GetValues<PacingIllustration>())
        {
            var plan = PacingReference.CreatePlan(mode);
            var electrodes = PacingReference.CreateElectrodes(mode);
            var projected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            var scalar = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PacingReference.CreateLeadIIBands(mode));
            var first = projected.GenerateBefore(4_204_000_000, 1100, 100);
            var ii = scalar.GenerateBefore(4_204_000_000, 1100, 100);
            Check.That(first.Select(s => s.MicrovoltValues[(int)EcgLead.II]).SequenceEqual(ii.Select(s => s.NormalizedValue)), mode + " monitor and 12 lead share exact II");
            if (mode == PacingIllustration.RightVentricularVvi)
            {
                var qrs = first.Where(s => s.Tick.SimTimeNs >= 208_000_000 && s.Tick.SimTimeNs < 368_000_000).ToArray();
                Check.That(qrs.Min(s => s.MicrovoltValues[(int)EcgLead.II]) <= -700 &&
                    qrs.Min(s => s.MicrovoltValues[(int)EcgLead.V1]) < -500 &&
                    qrs.Max(s => s.MicrovoltValues[(int)EcgLead.I]) > 300, "RV apex has superior axis and LBBB-like chest activation");
            }
            if (mode == PacingIllustration.SelectiveHis)
            {
                Check.That(first.Where(s => s.Tick.SimTimeNs >= 176_000_000 && s.Tick.SimTimeNs < 208_000_000)
                    .All(s => s.MicrovoltValues[(int)EcgLead.II] == 0), "selective His stimulus-QRS gap remains isoelectric");
            }
            var restored = ElectrodeSignalGenerator.Restore(projected.CaptureState());
            var next = projected.GenerateBefore(8_000_000_000, 1000, 100);
            var replay = restored.GenerateBefore(8_000_000_000, 1000, 100);
            Check.That(next.Zip(replay).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), mode + " checkpoint within a pulse resumes exactly");
            var lateState = projected.CaptureState();
            const long lateNs = 4_000_000_000_000;
            var late = ElectrodeSignalGenerator.Restore(lateState with
            {
                Timeline = lateState.Timeline with { CursorSimTimeNs = lateNs },
                Clock = lateState.Clock with { CursorSimTimeNs = lateNs, NextSampleIndex = 1_000_000, NextSampleSimTimeNs = lateNs }
            });
            Check.That(late.GenerateBefore(lateNs + 200_000_000, 50, 100).Count == 50, "bounded history at late timestamps");
        }
    }

    private static void PacingFailureDoesNotInventPerfusion()
    {
        foreach (var mode in Enum.GetValues<PacingIllustration>())
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { Pacing = mode });
            var samples = new Dictionary<int, List<short>> { [2] = [], [3] = [], [5] = [] };
            for (int step = 1; step <= 80; step++)
            {
                foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    var block = WaveformEnvelopeCodec.Decode(wire);
                    if (block.StartSimTimeNs < 12_000_000_000) { continue; }
                    foreach (var pair in samples)
                    { pair.Value.AddRange(block.Planes.Single(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(pair.Key)).Samples); }
                }
            }
            bool absent = mode is PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure;
            Check.That(samples.All(pair => absent
                ? pair.Key == 2 ? pair.Value.All(x => x == 0) : pair.Value.Zip(pair.Value.Skip(1)).All(p => p.First >= p.Second)
                : pair.Value.Max() > pair.Value.Min()), mode + " absent ejection leaves only passive vascular runoff and no pleth pulse");
        }
    }

    private static void PacingRejectsConflictsAndPreservesCursor()
    {
        var plan = PacingReference.CreatePlan(PacingIllustration.DualChamberDdd);
        Reject(() => RegularPhysiologyTimeline.Start(plan with { HeartPeriodNs = 900_000_000 }));
        Reject(() => RegularPhysiologyTimeline.Start(plan with { Pacing = (PacingIllustration)99 }));
        Reject(() => (PhysiologyIllustrationConfiguration.Default with { Pacing = PacingIllustration.DualChamberDdd, Svt = true }).ResolvePlan());
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        Reject(() => timeline.AdvanceBefore(4_000_000_000, 1));
        Check.That(timeline.CaptureState() == before, "failed budget does not advance timeline");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { timeline.AdvanceBefore(4_000_000_000, 100, cancelled.Token); throw new InvalidOperationException("Cancellation was ignored"); }
        catch (OperationCanceledException) { }
        Check.That(timeline.CaptureState() == before, "cancellation does not advance timeline");
        var noMechanics = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(4_000_000_000, 100);
        Check.That(noMechanics.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) && !noMechanics.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical), "electrical capture need not imply perfusion");
    }

    private static void DefibrillationHasFiniteSignedPhases()
    {
        foreach (var kind in Enum.GetValues<DefibrillationWaveformKind>())
        {
            bool mono = kind is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential;
            var plan = new DefibrillationWaveformPlan(kind, 20_000, 6_000_000, mono ? 0 : 4_000_000, 500, mono ? 0 : 150_000);
            var source = DefibrillationWaveform.Create(plan, 1_000_000_000);
            var samples = source.Sample(999_000_000, 1_012_000_000, 50_000, 1000);
            Check.That(samples.Any(s => s.CurrentMilliamps > 0) && samples.Any(s => s.CurrentMilliamps < 0) == !mono, kind + " polarity");
            Check.That(samples.All(s => Math.Abs(s.CurrentMilliamps) <= 20_000) && source.EvaluateCurrentMilliamps(999_999_999) == 0 && source.EvaluateCurrentMilliamps(source.EndSimTimeNs) == 0, "finite support and amplitude bounds");
            if (!mono)
            {
                Check.That(source.EvaluateCurrentMilliamps(1_006_000_000) == 0 && source.EvaluateCurrentMilliamps(1_006_149_999) == 0 && source.EvaluateCurrentMilliamps(1_006_150_000) == -10_000, "gap and reversed phase boundaries");
            }
            int[] firstPhase = samples.Where(s => s.SimTimeNs >= 1_000_000_000 && s.SimTimeNs < 1_006_000_000).Select(s => s.CurrentMilliamps).ToArray();
            if (kind == DefibrillationWaveformKind.RectilinearBiphasic)
            { Check.That(firstPhase.All(v => v == 20_000), "rectilinear first phase is constant"); }
            else if (kind == DefibrillationWaveformKind.MonophasicDampedSine)
            { Check.That(firstPhase[0] == 0 && firstPhase.Max() > 19_900 && firstPhase[^1] < firstPhase.Max() / 10, "damped sine rises and falls within a single positive phase"); }
            else
            { Check.That(firstPhase[0] == 20_000 && firstPhase.Zip(firstPhase.Skip(1)).All(p => p.First > p.Second), "truncated exponential decays until the phase boundary"); }
            var split = source.Sample(999_000_000, 1_005_000_000, 50_000, 1000)
                .Concat(source.Sample(1_005_000_000, 1_012_000_000, 50_000, 1000));
            Check.That(samples.SequenceEqual(split), "sampling chunk boundaries do not alter discharge");
            Check.That(samples.SequenceEqual(DefibrillationWaveform.Create(source.Plan, source.DeliveredAtSimTimeNs).Sample(999_000_000, 1_012_000_000, 50_000, 1000)), "immutable source reconstruction");
            Reject(() => source.Sample(0, long.MaxValue, 1, 10));
            Reject(() => DefibrillationWaveform.Create(plan, long.MaxValue));
            Reject(() => DefibrillationWaveform.Create(plan with { PeakCurrentMilliamps = 0 }, 0));
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid input rejection");
    }
}
