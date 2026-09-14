// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EventWaveformSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static EventWaveformComposition Ecg() => EventWaveformComposition.Restore(new(
        [new(PhysiologyCycleEventKind.AtrialElectrical, 0, 80_000_000, [0, 50 * Q, 100 * Q, 50 * Q]),
         new(PhysiologyCycleEventKind.VentricularElectrical, 0, 80_000_000, [0, -200 * Q, 1000 * Q, -100 * Q]),
         new(PhysiologyCycleEventKind.VentricularElectrical, 160_000_000, 160_000_000, [0, 150 * Q, 300 * Q, 150 * Q])],
        RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(4_000_000_000, 100)));
    public static Specification[] All =>
    [
        new(nameof(ElectricalEventsAnchorSeparateWaveBands), ElectricalEventsAnchorSeparateWaveBands),
        new(nameof(RespirationOccupiesTheWholeBreath), RespirationOccupiesTheWholeBreath),
        new(nameof(EventWaveformsOwnInputsAndRejectInvalidComposition), EventWaveformsOwnInputsAndRejectInvalidComposition),
        new(nameof(EventWaveformsReachAcquisitionAndWire), EventWaveformsReachAcquisitionAndWire),
    ];

    private static void ElectricalEventsAnchorSeparateWaveBands()
    {
        var ecg = Ecg();
        Check.That(ecg.EvaluateAt(40_000_000) == 100 * Q && ecg.EvaluateAt(200_000_000) == 1000 * Q &&
            ecg.EvaluateAt(400_000_000) == 300 * Q && ecg.EvaluateAt(240_000_000) == 0 && ecg.EvaluateAt(480_000_000) == 0,
            "atrial, ventricular and delayed repolarization test bands retain independent timing and baseline");
        Check.That(ecg.EvaluateAt(1_000_000_000) == ecg.EvaluateAt(200_000_000), "successive ventricular events repeat their waveform at the shared 800ms period");
        var state = ecg.CaptureState();
        var overlap = EventWaveformComposition.Restore(state with { Bands = new[] { state.Bands[0], state.Bands[0] } });
        Check.That(overlap.EvaluateAt(40_000_000) == 200 * Q, "overlapping contributions sum in fixed point");
    }

    private static void RespirationOccupiesTheWholeBreath()
    {
        var events = RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(4_000_000_000, 100);
        var resp = EventWaveformComposition.Restore(new(
            [new(PhysiologyCycleEventKind.InspirationStart, 0, Plan.BreathPeriodNs, [0, 500 * Q, 1000 * Q, 500 * Q])], events));
        Check.That(resp.EvaluateAt(0) == 0 && resp.EvaluateAt(1_875_000_000) == 1000 * Q &&
            resp.EvaluateAt(937_500_000) == 500 * Q && resp.EvaluateAt(2_812_500_000) == 500 * Q &&
            resp.EvaluateAt(3_750_000_000) == 0 && resp.EvaluateAt(3_750_000_001) > 0,
            "continuous inspiration and expiration replace event spikes and return to baseline at the next breath");
    }

    private static void EventWaveformsOwnInputsAndRejectInvalidComposition()
    {
        long[] table = [0, 100 * Q, 0, 0];
        PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.AtrialElectrical, 0)];
        var source = EventWaveformComposition.Restore(new([new(PhysiologyCycleEventKind.AtrialElectrical, 0, 4, table)], events));
        table[1] = 0;
        events[0] = events[0] with { SimTimeNs = 10 };
        Check.That(source.EvaluateAt(1) == 100 * Q && EventWaveformComposition.Restore(source.CaptureState()).EvaluateAt(1) == 100 * Q,
            "composition and restored evidence own original input arrays");
        var state = source.CaptureState();
        Reject(() => EventWaveformComposition.Restore(state with { Events = new[] { state.Events[0], state.Events[0] } }), "EventWaveform.InvalidState");
        Reject(() => EventWaveformComposition.Restore(state with { Bands = new[] { state.Bands[0] with { TableQ32 = new long[] { 1, 0, 0, 0 } } } }), "EventWaveform.InvalidState");
        Reject(() => EventWaveformComposition.Restore(state with { Events = new[] { state.Events[0] with { SimTimeNs = long.MaxValue } } }), "EventWaveform.InvalidState");
        var loud = state.Bands[0] with { TableQ32 = new long[] { 0, 20_000 * Q, 0, 0 } };
        var clipped = EventWaveformComposition.Restore(state with { Bands = new[] { loud, loud } });
        Reject(() => clipped.EvaluateAt(1), "EventWaveform.AmplitudeOverflow");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { source.EvaluateAt(1, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && source.EvaluateAt(1) == 100 * Q, "cancellation and rejected compositions leave accepted source unchanged");
    }

    private static void EventWaveformsReachAcquisitionAndWire()
    {
        var source = Ecg();
        const string profile = "AcqECGMonitor250@1";
        var clock = SignalSampleClock.Start(profile, 1, 0);
        var delay = SignalAcquisitionDelayLine.Start(profile, 1, 0, 50);
        List<short> expected = [];
        foreach (var tick in clock.DrainBefore(200_000_000))
        {
            short sample = (short)FixedPointMath.RoundDivideTiesToEven(source.EvaluateAt(tick.SimTimeNs), Q);
            expected.Add(sample);
            delay.Enqueue(tick, sample, 0);
        }
        var id = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var assembler = WaveformBlockAssembler.Start(id, id, 1, 1, 1, 0, 0, 1, [new(id, profile, 1, 1, 0, 1)]);
        Check.That(assembler.Push(id, delay.DrainAvailable(235_999_999)).Count == 0, "event-driven samples retain acquisition latency");
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(assembler.Push(id, delay.DrainAvailable(236_000_000)).Single());
        Check.That(WaveformEnvelopeCodec.Decode(wire).Planes[0].Samples.SequenceEqual(expected), "event waveform survives native sampling, delay and 200ms binary encoding");
    }

    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, reason);
    }
}
