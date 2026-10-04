// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class OxygenationTransportSpecifications
{
    private static readonly Guid Pleth = PhysiologyIllustrationSource.ChannelId(2);
    private static readonly Guid Sensor = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly VentilationTransportPlan Ventilation = new(450_000, 150_000, 210_000);
    public static Specification[] All =>
    [
        new(nameof(TransportPreservesPhysicalVolumesAcrossPartitions), TransportPreservesPhysicalVolumesAcrossPartitions),
        new(nameof(TransportSeparatesEffortDeadSpaceAndAirway), TransportSeparatesEffortDeadSpaceAndAirway),
        new(nameof(TransportFollowsEjectionsWithoutRunoffOrDisplayGain), TransportFollowsEjectionsWithoutRunoffOrDisplayGain),
        new(nameof(TransportRestoresRejectsAndQueriesFarTime), TransportRestoresRejectsAndQueriesFarTime),
        new(nameof(OxygenTraceOwnsHistoryAndRejectsExtrapolation), OxygenTraceOwnsHistoryAndRejectsExtrapolation),
        new(nameof(OpticsQueryEveryOriginalSampleTime), OpticsQueryEveryOriginalSampleTime),
        new(nameof(OpticsRejectMissingMismatchedAndInvalidOxygenation), OpticsRejectMissingMismatchedAndInvalidOxygenation),
        new(nameof(PreviewOxygenationPreservesPacingAndPauseSemantics), PreviewOxygenationPreservesPacingAndPauseSemantics),
        new(nameof(PreviewMissingOxygenHistoryLeavesAcquisitionRetryable), PreviewMissingOxygenHistoryLeavesAcquisitionRetryable),
    ];

    private static PhysiologyTransportSource Transport(PhysiologyIllustrationConfiguration? configuration = null,
        VentilationTransportPlan? ventilation = null) => PhysiologyIllustrationSource.CreateTransport(
            configuration ?? PhysiologyIllustrationConfiguration.Default, ventilation ?? Ventilation, 66_667);

    private static void TransportPreservesPhysicalVolumesAcrossPartitions()
    {
        var source = Transport();
        var breath = source.Integrate(0, 3_750_000_000);
        Check.That(breath.DeliveredVolumeNanolitersBtps == 450_000_000 && breath.AlveolarVentilationNanolitersBtps == 300_000_000,
            "one breath carries explicit 450 mL VT and 300 mL alveolar washout, not impedance counts");
        var whole = source.Integrate(0, 15_000_000_000);
        long tidal = 0, alveolar = 0, blood = 0;
        for (long from = 0; from < whole.ToExclusiveSimTimeNs;)
        {
            long to = Math.Min(whole.ToExclusiveSimTimeNs, from + 73_000_003);
            var part = source.Integrate(from, to);
            tidal += part.DeliveredVolumeNanolitersBtps;
            alveolar += part.AlveolarVentilationNanolitersBtps;
            blood += part.EffectiveBloodVolumeNanoliters;
            from = to;
        }
        Check.That(tidal == whole.DeliveredVolumeNanolitersBtps && alveolar == whole.AlveolarVentilationNanolitersBtps &&
            blood == whole.EffectiveBloodVolumeNanoliters, "nanoliter integrals telescope exactly at arbitrary partition boundaries");
        Check.That(source.Integrate(240_000_000, 480_000_000).EffectiveBloodVolumeNanoliters == 66_667_000 &&
            source.Integrate(480_000_000, 1_040_000_000).EffectiveBloodVolumeNanoliters == 0,
            "ejection integrates to the prescribed stroke volume; interbeat zero flow is not a missing beat");
    }

    private static void TransportSeparatesEffortDeadSpaceAndAirway()
    {
        foreach (var activity in new[] { RespiratoryActivity.Absent, RespiratoryActivity.EffortOnly })
        {
            var source = Transport(PhysiologyIllustrationConfiguration.Default with
            { RespiratoryActivity = activity, ActivityAfterBreaths = 2, ActivityDurationBreaths = 2 });
            Check.That(source.Integrate(7_500_000_000, 15_000_000_000).AlveolarVentilationNanolitersBtps == 0 &&
                source.Integrate(15_000_000_000, 18_750_000_000).AlveolarVentilationNanolitersBtps == 300_000_000,
                "ineffective effort/apnea removes delivered gas and resumes on the original breath grid");
        }
        var shallow = Transport(ventilation: Ventilation with { TidalVolumeMicrolitersBtps = 100_000 }).Integrate(0, 3_750_000_000);
        Check.That(shallow.DeliveredVolumeNanolitersBtps == 100_000_000 && shallow.AlveolarVentilationNanolitersBtps == 0,
            "sub-dead-space tidal excursion does not imply alveolar ventilation");
        var closed = Transport(ventilation: Ventilation with { AirwayOpen = false }).Integrate(0, 3_750_000_000);
        Check.That(!closed.AirwayOpen && closed.DeliveredVolumeNanolitersBtps == 0 && closed.EffectiveBloodVolumeNanoliters > 0,
            "airway closure is independent of respiratory effort and circulation");
        var paused = Transport(PhysiologyIllustrationConfiguration.Default with { InspiratoryPauseMilliseconds = 500 });
        Check.That(paused.Integrate(1_375_000_000, 1_875_000_000).DeliveredVolumeNanolitersBtps == 0 &&
            paused.Integrate(0, 3_750_000_000).DeliveredVolumeNanolitersBtps == 450_000_000,
            "inspiratory hold carries no flow but does not change full-breath volume");
        var patterned = Transport(PhysiologyIllustrationConfiguration.Default with { RespiratoryPattern = RespiratoryPattern.CheyneStokesIllustration });
        Check.That(patterned.Integrate(0, 3_750_000_000).DeliveredVolumeNanolitersBtps == 90_000_000 &&
            patterned.Integrate(0, 3_750_000_000).AlveolarVentilationNanolitersBtps == 0 &&
            patterned.Integrate(33_750_000_000, 37_500_000_000).DeliveredVolumeNanolitersBtps == 0,
            "authored depth scales explicit VT before subtracting dead space; absent slots remain absent");
    }

    private static void TransportFollowsEjectionsWithoutRunoffOrDisplayGain()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 10, MechanicalDurationCycles = 5 };
        var source = Transport(configuration);
        Check.That(source.Integrate(8_000_000_000, 12_240_000_000).EffectiveBloodVolumeNanoliters == 0 &&
            source.Integrate(12_240_000_000, 12_480_000_000).EffectiveBloodVolumeNanoliters == 66_667_000,
            "mechanical pause has no invented mean-flow tail and first resumed ejection carries one stroke");
        var alteredDisplay = Transport(configuration with { RespAmplitudeCounts = -500, AbpPulsePermille = 2000, PaPulsePermille = 500 });
        Check.That(source.Integrate(0, 15_000_000_000) == alteredDisplay.Integrate(0, 15_000_000_000),
            "respiratory polarity and pressure morphology gains do not change gas or blood transport");
        foreach (var rhythm in new[] { PhysiologyIllustrationConfiguration.PrematureVentricular,
            PhysiologyIllustrationConfiguration.SvtPreset, PhysiologyIllustrationConfiguration.VtPreset,
            PhysiologyIllustrationConfiguration.SinusArrhythmiaPreset, PhysiologyIllustrationConfiguration.SinusArrestPreset,
            PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = 2 },
            PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.AtrialOnly } })
        {
            var input = Transport(rhythm).Integrate(0, 8_000_000_000);
            Check.That(input.EffectiveBloodVolumeNanoliters >= 0, "existing rhythm-specific event schedules are accepted without using ECG rate");
        }
        Check.That(Transport(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.AtrialOnly })
            .Integrate(0, 8_000_000_000).EffectiveBloodVolumeNanoliters == 0, "atrial electrical/mechanical events do not pump ventricular stroke volume");
    }

    private static void TransportRestoresRejectsAndQueriesFarTime()
    {
        var source = Transport();
        var state = JsonSerializer.Deserialize<PhysiologyTransportState>(JsonSerializer.Serialize(source.CaptureState()))!;
        var restored = PhysiologyTransportSource.Restore(state);
        long far = long.MaxValue - 1_000_000_000;
        Check.That(source.Integrate(far, long.MaxValue) == restored.Integrate(far, long.MaxValue),
            "far-time transport has bounded event reconstruction and preserved epoch-relative clocks");
        Reject(() => source.Integrate(0, 10_000_000_000, 1));
        Reject(() => source.Integrate(10, 9));
        Reject(() => source.Integrate(0, 60_000_000_001));
        Reject(() => PhysiologyTransportSource.Restore(state with { ModelId = "unknown" }));
        Reject(() => Transport(ventilation: Ventilation with { InspiredOxygenMillionths = -1 }));
        Reject(() => PhysiologyTransportSource.Restore(state with { BloodFlow = state.BloodFlow with { Response = StrokeVolumeResponse.AtrialFibrillation } }));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { source.Integrate(0, 1000, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && source.Integrate(0, 8_000_000_000) == restored.Integrate(0, 8_000_000_000),
            "budget, configuration and cancellation failures have no mutable transport cursor");
    }

    private static void OxygenTraceOwnsHistoryAndRejectsExtrapolation()
    {
        int[] values = [98000, 96000, 94000];
        var trace = new SampledArterialOxygenation(new(1_000_000_000, 1_000_000_000, values));
        values[0] = 75000;
        Check.That(trace.ReadAt(1_500_000_000) == new ArterialOxygenationSample(1_500_000_000, 97000),
            "trace owns its samples and interpolates at the requested source time");
        Check.That(new SampledArterialOxygenation(new(0, 8_000_000, [98001, 98000])).ReadAt(4_000_000).SaturationMilliPercent == 98000,
            "half-way interpolation rounds the final value to even, not merely the delta");
        var restored = new SampledArterialOxygenation(JsonSerializer.Deserialize<SampledArterialOxygenationState>(JsonSerializer.Serialize(trace.CaptureState()))!);
        Check.That(restored.ReadAt(3_000_000_000) == trace.ReadAt(3_000_000_000), "trace endpoint and checkpoint preserve original times");
        Reject(() => trace.ReadAt(999_999_999));
        Reject(() => trace.ReadAt(3_000_000_001));
        Reject(() => _ = new SampledArterialOxygenation(new(0, 1_000_000, [100001, 98000])));
        Reject(() => _ = new SampledArterialOxygenation(new(long.MaxValue, 1_000_000, [98000, 98000])));
        Check.That(new SampledArterialOxygenation(new(0, 8_000_000, [74000, 0])).ReadAt(0).SaturationMilliPercent == 74000,
            "physiological history supports deep hypoxia independently of current optical reporting range");
    }

    private static void OpticsQueryEveryOriginalSampleTime()
    {
        var reader = new ProbeOxygenation();
        var source = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, reader);
        byte[] input = Input(50);
        byte[] first = source.ConvertAcquiredPulse(input);
        Check.That(reader.Times.SequenceEqual(Enumerable.Range(0, 25).Select(i => 10_000_000_000 + i * 8_000_000L)),
            "optical queries use each original sample timestamp, without acquisition or pulse-transit delay twice");
        Check.That(first.SequenceEqual(new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 98000).ConvertAcquiredPulse(input)),
            "constant physiological saturation reproduces existing red/IR bytes");
        reader.Times.Clear();
        source.ConvertAcquiredPulse(Input(5));
        Check.That(source.ConvertAcquiredPulse(input).SequenceEqual(first), "late and out-of-order source queries do not change a sample's oxygenation");
    }

    private static void OpticsRejectMissingMismatchedAndInvalidOxygenation()
    {
        var probe = new ProbeOxygenation { AvailableThroughNs = 100_000_000 };
        var source = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, probe);
        Reject(() => source.ConvertAcquiredPulse(Input(0)));
        probe.AvailableThroughNs = long.MaxValue;
        probe.TimeOffsetNs = 1;
        Reject(() => source.ConvertAcquiredPulse(Input(0)));
        probe.TimeOffsetNs = 0;
        probe.SaturationMilliPercent = -1;
        Reject(() => source.ConvertAcquiredPulse(Input(0)));
        probe.SaturationMilliPercent = 100001;
        Reject(() => source.ConvertAcquiredPulse(Input(0)));
        foreach (int value in new[] { 74999, 70000, 40000, 0 })
        {
            probe.SaturationMilliPercent = value;
            Check.That(source.ConvertAcquiredPulse(Input(0)).SequenceEqual(new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, value).ConvertAcquiredPulse(Input(0))),
                "deep physiological oxygenation reaches the same optics as an explicit target without a75% floor");
        }
        probe.SaturationMilliPercent = 98000;
        Check.That(source.ConvertAcquiredPulse(Input(0)).SequenceEqual(new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 98000).ConvertAcquiredPulse(Input(0))),
            "missing history, mismatched clocks and invalid values do not silently clamp or mutate valid retry");
    }

    private static void PreviewOxygenationPreservesPacingAndPauseSemantics()
    {
        var trace = new SampledArterialOxygenation(new(0, 1_000_000_000,
            Enumerable.Range(0, 41).Select(second => second <= 8 ? 98000 : second >= 16 ? 90000 : 98000 - (second - 8) * 1000).ToArray()));
        var small = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, oxygenation: trace);
        var large = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, oxygenation: trace);
        for (int step = 0; step < 120; step++)
        {
            large.Advance(250_000_000);
            for (int sub = 0; sub < 10; sub++) { small.Advance(25_000_000); }
        }
        Check.That(small.Measurements == large.Measurements && small.FrontierNs == large.FrontierNs &&
            small.Blocks.SelectMany(b => b.Planes.SelectMany(p => p.Samples)).SequenceEqual(large.Blocks.SelectMany(b => b.Planes.SelectMany(p => p.Samples))),
            "UI chunk size does not change original-time oxygenation, measurements or display waveforms");
        var reading = small.Measurements!;
        Check.That(reading.SpO2.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.SpO2.SaturationMilliPercent!.Value - 90000) < 500 &&
            small.Measurements == reading, "source-time trajectory is measured via red/IR; paused reads do not advance physiology");
        Reject(() => _ = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), oxygenation: trace));
        Reject(() => _ = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, 98000, oxygenation: trace));
    }

    private static void PreviewMissingOxygenHistoryLeavesAcquisitionRetryable()
    {
        var probe = new ProbeOxygenation { AvailableThroughNs = 100_000_000 };
        var preview = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, oxygenation: probe);
        var baseline = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, 98000);
        for (int step = 0; step < 43; step++) { preview.Advance(50_000_000); baseline.Advance(50_000_000); }
        long before = preview.SimulationTimeNs;
        Reject(() => preview.Advance(50_000_000));
        Check.That(preview.SimulationTimeNs == before && preview.Blocks.Count == 0 && preview.DataRevision == 0,
            "missing old oxygen sample rejects the acquisition chunk before its source cursor commits");
        probe.AvailableThroughNs = long.MaxValue;
        preview.Advance(50_000_000);
        baseline.Advance(50_000_000);
        Check.That(preview.Measurements == baseline.Measurements && preview.Blocks.Count == baseline.Blocks.Count && preview.DataRevision == baseline.DataRevision,
            "restoring source history lets the identical acquisition chunk retry");
    }

    private sealed class ProbeOxygenation : IArterialOxygenationSource
    {
        public List<long> Times { get; } = [];
        public long AvailableThroughNs { get; set; } = long.MaxValue;
        public long TimeOffsetNs { get; set; }
        public int SaturationMilliPercent { get; set; } = 98000;
        public ArterialOxygenationSample ReadAt(long sourceSimTimeNs)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceSimTimeNs, AvailableThroughNs);
            Times.Add(sourceSimTimeNs);
            return new(sourceSimTimeNs + TimeOffsetNs, SaturationMilliPercent);
        }
    }

    private static byte[] Input(int block)
    {
        var plane = new WaveformPlane(Pleth, 125, 1, (ulong)block * 25, 1, 1, 0, 1, WaveformQualityEncoding.None,
            Enumerable.Range(0, 25).Select(i => (short)(i * 40)).ToArray(), []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Pleth, Pleth, 1, 1, (ulong)block, 1, block * 200_000_000L, 200_000_000, [plane]));
    }

    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception exception) when (exception is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, "invalid transport or oxygenation input is explicitly rejected");
    }
}
