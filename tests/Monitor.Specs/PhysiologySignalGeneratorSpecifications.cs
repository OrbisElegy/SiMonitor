// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PhysiologySignalGeneratorSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static EventWaveformBand Breath => new(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, [0, 500 * Q, 1000 * Q, 500 * Q]);
    private static PhysiologySignalGenerator Start() => PhysiologySignalGenerator.Start(Plan, "AcqResp125@1", 7, [Breath]);
    public static Specification[] All =>
    [
        new(nameof(ContinuousSamplingPreservesEarlierTriggers), ContinuousSamplingPreservesEarlierTriggers),
        new(nameof(SamplePartitionsAndRestorePreserveWaveform), SamplePartitionsAndRestorePreserveWaveform),
        new(nameof(SamplingFailureRollsBackTimelineAndClock), SamplingFailureRollsBackTimelineAndClock),
        new(nameof(SamplingCheckpointRejectsMixedClocks), SamplingCheckpointRejectsMixedClocks),
    ];

    private static void ContinuousSamplingPreservesEarlierTriggers()
    {
        var source = Start();
        _ = source.GenerateBefore(1_000_000_000, 125, 100);
        var samples = source.GenerateBefore(2_000_000_000, 125, 100);
        var expected = EventWaveformComposition.Restore(new([Breath], RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(2_000_000_000, 100)));
        Check.That(samples.Count == 125 && samples[0].ValueQ32 > 0 && samples.All(sample => sample.ValueQ32 == expected.EvaluateAt(sample.Tick.SimTimeNs)),
            "a breath started before this batch continues rather than reverting to baseline");
        Check.That(source.CaptureState().Timeline.CursorSimTimeNs == source.CaptureState().Clock.CursorSimTimeNs,
            "source timeline and sampling cursor commit together");
    }

    private static void SamplePartitionsAndRestorePreserveWaveform()
    {
        var whole = Start();
        var expected = whole.GenerateBefore(8_000_000_000, 1000, 100);
        var split = Start();
        List<PhysiologySignalSample> actual = [];
        for (int batch = 1; batch <= 40; batch++)
        {
            actual.AddRange(split.GenerateBefore(batch * 200_000_000L, 25, 100));
            // Retain the first partial wave and both sides of expiration and breath onsets.
            if (batch is 1 or 5 or 9 or 10 or 18 or 19 or 28 or 29 or 37 or 38)
            {
                split = PhysiologySignalGenerator.Restore(split.CaptureState());
            }
        }
        Check.That(expected.SequenceEqual(actual) && Snapshot(whole) == Snapshot(split),
            "multiple breath boundaries and selected restoration points preserve full fixed-point/sample identity");
        Check.That(split.GenerateBefore(8_000_000_000, 1, 100).Count == 0, "repeated deadlines never replay samples");
    }

    private static void SamplingFailureRollsBackTimelineAndClock()
    {
        var source = Start();
        string before = Snapshot(source);
        Reject(() => source.GenerateBefore(200_000_000, 1, 100), "PhysiologySignal.SampleLimitExceeded");
        bool full = false;
        try { source.GenerateBefore(200_000_000, 25, 1); }
        catch (PhysiologyTimelineException exception) { full = exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded"; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { source.GenerateBefore(200_000_000, 25, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(full && cancelled && Snapshot(source) == before, "limits and cancellation retain both components");
        var loud = Breath with { TableQ32 = new long[] { 0, 20_000 * Q, 20_000 * Q, 0 } };
        var overlap = PhysiologySignalGenerator.Start(Plan, "AcqResp125@1", 1, [loud, loud]);
        string original = Snapshot(overlap);
        bool overflow = false;
        try { overlap.GenerateBefore(2_000_000_000, 250, 100); }
        catch (EventWaveformException exception) { overflow = exception.ReasonCode == "EventWaveform.AmplitudeOverflow"; }
        Check.That(overflow && Snapshot(overlap) == original && source.GenerateBefore(200_000_000, 25, 100).Count == 25,
            "late amplitude failure rolls back completed event and sample trials");
    }

    private static void SamplingCheckpointRejectsMixedClocks()
    {
        var source = Start();
        _ = source.GenerateBefore(1_000_000_000, 125, 100);
        var state = source.CaptureState();
        Reject(() => PhysiologySignalGenerator.Restore(state with { Timeline = state.Timeline with { CursorSimTimeNs = 999_999_999 } }), "PhysiologySignal.InvalidCheckpoint");
        Reject(() => PhysiologySignalGenerator.Restore(state with { Bands = new[] { Breath with { DelayNs = long.MaxValue } } }), "PhysiologySignal.InvalidCheckpoint");
        Reject(() => source.GenerateBefore(0, 1, 100), "PhysiologySignal.TimeRegression");
        bool immutable = false;
        try { ((IList<long>)state.Bands[0].TableQ32)[1] = 0; }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && Snapshot(source) == Snapshot(PhysiologySignalGenerator.Restore(state)), "checkpoint tables are owned and recovery remains available");
    }

    private static string Snapshot(PhysiologySignalGenerator source) => JsonSerializer.Serialize(source.CaptureState());
    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (PhysiologySignalException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, reason);
    }
}
