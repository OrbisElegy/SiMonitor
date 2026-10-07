// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class MonitorContinuationSpecifications
{
    internal static readonly Specification[] All =
    [
        new(nameof(AtrialFibrillationContinuationAppliesTheCurrentSegmentImmediately), AtrialFibrillationContinuationAppliesTheCurrentSegmentImmediately),
        new(nameof(ApplyContinuesAtTheOldSourcesLastMoment), ApplyContinuesAtTheOldSourcesLastMoment),
        new(nameof(PendingChangesReplaceAndRejectAtomically), PendingChangesReplaceAndRejectAtomically),
        new(nameof(StartupDiscardDoesNotPublishTransientSamples), StartupDiscardDoesNotPublishTransientSamples),
        new(nameof(ContinuationSurvivesCheckpointAndPreservesUnchangedSources), ContinuationSurvivesCheckpointAndPreservesUnchangedSources),
        new(nameof(RealtimeContinuationRetainsOxygenReserves), RealtimeContinuationRetainsOxygenReserves),
        new(nameof(CvpBaselineContinuationPreservesHistoryAndOtherChannels), CvpBaselineContinuationPreservesHistoryAndOtherChannels),
        new(nameof(CvpBaselineContinuationSurvivesBufferedCheckpoint), CvpBaselineContinuationSurvivesBufferedCheckpoint),
    ];

    private static LocalMonitorPreviewSession Session(PhysiologyIllustrationConfiguration? config = null,
        RealtimeOxygenationConfiguration? oxygenation = null) => new(config ?? PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableMeasurements: true, realtimeOxygenation: oxygenation);

    private static void Advance(LocalMonitorPreviewSession session, long target)
    {
        while (session.SimulationTimeNs < target) { session.Advance(Math.Min(50_000_000, target - session.SimulationTimeNs)); }
    }

    private static void AtrialFibrillationContinuationAppliesTheCurrentSegmentImmediately()
    {
        foreach (long boundary in new[] { 6_052_000_000, AtrialFibrillationReference.SegmentDurationNs - 100_000_000 })
            foreach (var shape in new[] { EcgVentricularIllustration.Reference, EcgVentricularIllustration.RightHypertrophyWithStrain,
                EcgVentricularIllustration.SevereRightQr, EcgVentricularIllustration.PulmonaryHeartSigns })
            {
                var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
                { VentricularShape = shape });
                long cursor = 0;
                while (cursor < boundary)
                {
                    cursor = Math.Min(cursor + 50_000_000, boundary);
                    source.AdvanceTo(cursor, 50, 1, 100);
                }
                foreach (var configuration in new[] { PhysiologyIllustrationConfiguration.Fibrillation(), PhysiologyIllustrationConfiguration.Fibrillation(),
                    PhysiologyIllustrationConfiguration.Fibrillation(true), PhysiologyIllustrationConfiguration.Default,
                    PhysiologyIllustrationConfiguration.Fibrillation() })
                {
                    source.ContinueWith(PhysiologyIllustrationSource.Create(configuration));
                    var state = source.CaptureState().Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(0)).Generator;
                    var actual = PhysiologySignalGenerator.Restore(state).GenerateBefore(cursor + 200_000_000, 50, 1000);
                    // Remove only f activity: all old triggered P/QRS/T tails and new
                    // irregular QRS events remain an independent control signal.
                    var withoutF = PhysiologySignalGenerator.Restore(state with
                    {
                        Bands = state.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.AtrialFibrillationSegment).ToArray(),
                        History = state.History.Select(h => h with
                        { Bands = h.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.AtrialFibrillationSegment).ToArray() }).ToArray()
                    }).GenerateBefore(cursor + 200_000_000, 50, 1000);
                    var fBands = state.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialFibrillationSegment).ToArray();
                    var expectedF = fBands.Length == 0 ? null : PhysiologySignalGenerator.Restore(state with
                    { Bands = fBands, ActiveFromEventTimeNs = null, History = [] }).GenerateBefore(cursor + 200_000_000, 50, 1000);
                    Check.That(actual.Count == 50 && actual.Select((sample, index) => sample.ValueQ32 - withoutF[index].ValueQ32)
                        .SequenceEqual(expectedF?.Select(sample => sample.ValueQ32) ?? Enumerable.Repeat(0L, 50)),
                        "f activity follows the new definition from its first sample, without missing, doubled or outgoing segments");
                    if (expectedF is not null)
                    { Check.That(expectedF.Any(sample => sample.ValueQ32 != 0), "the transition window contains measurable f activity"); }
                    var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
                    for (int step = 0; step < 8; step++)
                    {
                        cursor += 50_000_000;
                        var expected = source.AdvanceTo(cursor, 50, 1, 100);
                        var replay = restored.AdvanceTo(cursor, 50, 1, 100);
                        Check.That(expected.Count == replay.Count && expected.Zip(replay).All(p => p.First.SequenceEqual(p.Second)),
                            "rapid AF transitions preserve buffered samples and checkpoint replay");
                    }
                }
            }
    }

    private static void ApplyContinuesAtTheOldSourcesLastMoment()
    {
        var live = Session();
        var control = Session();
        live.DiscardStartup();
        control.DiscardStartup();
        Advance(live, 8_030_000_000);
        Advance(control, 8_030_000_000);
        var history = live.Blocks.ToArray();
        var readings = live.Measurements;
        var stopped = Session(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent });
        long boundary = live.ScheduleSource(stopped, 970_000_000);
        Check.That(boundary == 9_000_000_000 && live.SimulationTimeNs == 8_030_000_000 &&
            live.Blocks.SequenceEqual(history) && live.Measurements == readings && stopped.SimulationTimeNs == 0,
            "Apply only queues a definition, preserving time, samples and measurements");
        Advance(live, boundary);
        Advance(control, boundary);
        Check.That(live.Samples(0, 0, boundary).SequenceEqual(control.Samples(0, 0, boundary)), "old source runs until the boundary");
        Advance(live, 15_000_000_000);
        Advance(control, 15_000_000_000);
        Check.That(live.Samples(0, 0, boundary).SequenceEqual(control.Samples(0, 0, boundary)), "old buffered samples remain unchanged after activation");
        Check.That(live.Samples(0, boundary + 2_000_000_000, 12_000_000_000).All(s => s.Value == 0), "new absent rhythm supplies subsequent samples");
        double before = live.Samples(3, boundary - 8_000_000, boundary).Single().Value;
        double after = live.Samples(3, boundary, boundary + 8_000_000).Single().Value;
        Check.That(Math.Abs(after - before) < 5 && after > 20 &&
            live.Samples(3, boundary + 3_000_000_000, boundary + 3_008_000_000).Single().Value < after,
            "pressure continues from the outgoing reservoir and decays instead of restarting or using a pregenerated flat trace");
        for (int i = 1; i < live.Blocks.Count; i++)
        {
            var previous = live.Blocks[i - 1];
            var current = live.Blocks[i];
            Check.That(current.StartSimTimeNs == previous.StartSimTimeNs + previous.DurationNs &&
                current.BlockSequence == previous.BlockSequence + 1 && current.InstanceId == previous.InstanceId,
                "continuous stream has no missing, repeated or reset packets");
            foreach (var plane in current.Planes)
            {
                var old = previous.Planes.Single(p => p.ChannelId == plane.ChannelId);
                Check.That(plane.FirstSampleIndex == old.FirstSampleIndex + (ulong)old.Samples.Count, "all channel sample clocks continue");
            }
        }
    }

    private static void PendingChangesReplaceAndRejectAtomically()
    {
        var live = Session();
        Advance(live, 5_000_000_000);
        live.ScheduleSource(Session(), 3_000_000_000);
        Check.That(live.PendingSourceTimeNs == 8_000_000_000, "paused source does not consume delay");
        var stopped = Session(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent });
        live.ScheduleSource(stopped, 1_000_000_000);
        try { live.ScheduleSource(Session(), -1); throw new InvalidOperationException("invalid delay accepted"); }
        catch (ArgumentOutOfRangeException) { }
        Check.That(live.PendingSourceTimeNs == 6_000_000_000, "invalid edit preserves the replacement pending definition");
        Advance(live, 10_000_000_000);
        Check.That(live.PendingSourceTimeNs is null && live.Samples(0, 7_000_000_000, 7_800_000_000).All(s => s.Value == 0),
            "only the newest accepted definition activates");
    }

    private static void StartupDiscardDoesNotPublishTransientSamples()
    {
        var prepared = Session();
        prepared.DiscardStartup();
        Check.That(prepared.SimulationTimeNs == 0 && prepared.FrontierNs == 0 && prepared.Blocks.Count == 0 &&
            prepared.DetectedBeats.Count == 0 && prepared.DetectedPulses.Count == 0, "startup pre-roll is never published");
        var raw = Session();
        Advance(raw, LocalMonitorPreviewSession.StartupDiscardNs + 5_000_000_000);
        Advance(prepared, 5_000_000_000);
        for (int channel = 0; channel < 7; channel++)
        {
            var expected = raw.Samples(channel, LocalMonitorPreviewSession.StartupDiscardNs,
                LocalMonitorPreviewSession.StartupDiscardNs + prepared.FrontierNs)
                .Select(s => (s.TimeNs - LocalMonitorPreviewSession.StartupDiscardNs, s.Value));
            Check.That(prepared.Samples(channel, 0, prepared.FrontierNs).SequenceEqual(expected),
                "startup discards all initial transients and rebases every channel to zero");
        }
        Check.That(prepared.Blocks[0].StartSimTimeNs == 0 && prepared.Blocks[0].Planes.All(p => p.FirstSampleIndex == 0),
            "first retained samples begin at the public origin");
    }

    private static void ContinuationSurvivesCheckpointAndPreservesUnchangedSources()
    {
        var source = PhysiologyIllustrationSource.Create();
        for (long time = 50_000_000; time <= 6_000_000_000; time += 50_000_000) { source.AdvanceTo(time, 50, 1, 100); }
        var unchanged = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        for (long time = 6_050_000_000; time <= 6_800_000_000; time += 50_000_000)
        {
            var actual = source.AdvanceTo(time, 50, 1, 100);
            var expected = unchanged.AdvanceTo(time, 50, 1, 100);
            Check.That(actual.Count == expected.Count && actual.Zip(expected).All(p => p.First.SequenceEqual(p.Second)),
                "reapplying an unchanged definition does not alter phase or reservoir state");
        }
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { CardiacActivity = CardiacActivity.Absent }));
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        for (long time = 6_850_000_000; time <= 10_000_000_000; time += 50_000_000)
        {
            var expected = source.AdvanceTo(time, 50, 1, 100);
            var actual = restored.AdvanceTo(time, 50, 1, 100);
            Check.That(actual.Count == expected.Count && actual.Zip(expected).All(p => p.First.SequenceEqual(p.Second)),
                "pending acquired samples and triggered responses survive checkpoint restoration");
        }
        _ = PhysiologyWaveformGroup.Restore(source.CaptureState());
        var seeded = PhysiologyIllustrationConfiguration.Default with { SeededRate = new(75, new string('a', 64), 30) };
        var varying = PhysiologyIllustrationSource.Create(seeded);
        for (long time = 50_000_000; time <= 4_000_000_000; time += 50_000_000) { varying.AdvanceTo(time, 50, 1, 100); }
        var control = varying.Fork();
        varying.ContinueWith(PhysiologyIllustrationSource.Create(seeded with { SeededRate = new(75, new string('a', 64), 30) }));
        for (long time = 4_050_000_000; time <= 7_000_000_000; time += 50_000_000)
        {
            var actual = varying.AdvanceTo(time, 50, 1, 100);
            var expected = control.AdvanceTo(time, 50, 1, 100);
            Check.That(actual.Count == expected.Count && actual.Zip(expected).All(p => p.First.SequenceEqual(p.Second)),
                "equivalent seeded definitions preserve every source sample");
        }
    }

    private static void CvpBaselineContinuationPreservesHistoryAndOtherChannels()
    {
        foreach (int baseline in new[] { -500, 0, 1250, 3000 })
        {
            var live = Session();
            var control = Session();
            live.DiscardStartup();
            control.DiscardStartup();
            Advance(live, 8_030_000_000);
            var history = live.Blocks.ToArray();
            long boundary = live.ScheduleSource(Session(PhysiologyIllustrationConfiguration.Default with
            { CvpBaselineCentiMmHg = baseline }), 970_000_000);
            Check.That(boundary == 9_000_000_000 && live.Blocks.SequenceEqual(history),
                "CVP change queues without replacing existing acquisition blocks");
            Advance(live, 25_000_000_000);
            Advance(control, 25_000_000_000);
            for (int channel = 0; channel < 7; channel++)
            {
                var actual = live.Samples(channel, 0, live.FrontierNs).ToArray();
                var expected = control.Samples(channel, 0, live.FrontierNs).ToArray();
                Check.That(actual.Length > 0 && actual.Length == expected.Length && actual.Zip(expected).All(pair =>
                    pair.First.TimeNs == pair.Second.TimeNs && Math.Abs(pair.First.Value - pair.Second.Value -
                        (channel == 6 && pair.First.TimeNs >= boundary ? (baseline - 600) / 100d : 0)) < 1e-9),
                    "only CVP samples at or after activation shift; acquired history and all other channels stay identical");
            }
            Check.That(live.Measurements!.CvpMean.MeanCentiMmHg == control.Measurements!.CvpMean.MeanCentiMmHg + baseline - 600,
                "CVP displayed mean derives the new sampled baseline");
            var planes = live.Blocks.SelectMany(block => block.Planes).Where(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(6));
            Check.That(planes.All(plane => plane.OffsetNumerator == 0 && plane.OffsetDenominator == 1),
                "wire calibration stays fixed across baseline changes");
        }
    }

    private static void CvpBaselineContinuationSurvivesBufferedCheckpoint()
    {
        var source = PhysiologyIllustrationSource.Create();
        for (long time = 50_000_000; time <= 6_050_000_000; time += 50_000_000) { source.AdvanceTo(time, 50, 1, 100); }
        long cursor = 6_050_000_000;
        foreach (int baseline in new[] { 1250, -500, -500, 3000 })
        {
            source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
            { CvpBaselineCentiMmHg = baseline }));
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            for (int step = 0; step < 5; step++)
            {
                cursor += 50_000_000;
                var expected = source.AdvanceTo(cursor, 50, 1, 100);
                var actual = restored.AdvanceTo(cursor, 50, 1, 100);
                Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                    "repeated CVP changes retain pending old samples and replay identical envelopes");
                restored = PhysiologyWaveformGroup.Restore(restored.CaptureState());
            }
        }
    }

    private static void RealtimeContinuationRetainsOxygenReserves()
    {
        var live = Session(oxygenation: RealtimeOxygenationConfiguration.ReferenceAdult);
        Advance(live, 10_000_000_000);
        var before = live.Oxygenation!.Value;
        var next = Session(oxygenation: RealtimeOxygenationConfiguration.ReferenceAdult with
        { Ventilation = new(0, 150000, 210000) });
        live.ScheduleSource(next, 0);
        live.Advance(8_000_000);
        Check.That(live.Oxygenation!.Value.SourceSimTimeNs == 10_008_000_000 &&
            live.Oxygenation.Value.Reservoirs != next.Oxygenation!.Value.Reservoirs && before.SourceSimTimeNs == 10_000_000_000,
            "live reconfiguration preserves evolved reserves and their clock");
        // Repeated optical changes inside the two-second acquisition buffer.
        live.ScheduleSource(Session(), 0);
        Advance(live, 10_250_000_000);
        live.ScheduleSource(Session(oxygenation: RealtimeOxygenationConfiguration.ReferenceAdult), 0);
        Advance(live, 14_000_000_000);
        Check.That(live.Measurements!.SampleTimeNs > 11_000_000_000, "buffered optics stay paired across rapid changes");
    }
}
