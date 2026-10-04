// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RealtimeOxygenationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RealtimeGridHistoryAndControlChangesAreDeterministic), RealtimeGridHistoryAndControlChangesAreDeterministic),
        new(nameof(RealtimeVentilationUsesCurrentPhysicalInputs), RealtimeVentilationUsesCurrentPhysicalInputs),
        new(nameof(RealtimePreviewMeasuresDesaturationAndRecovery), RealtimePreviewMeasuresDesaturationAndRecovery),
        new(nameof(RealtimePreviewRetainsPulseQualityGates), RealtimePreviewRetainsPulseQualityGates),
        new(nameof(RealtimeInvalidInputDoesNotPublishState), RealtimeInvalidInputDoesNotPublishState),
    ];
    private static readonly VentilationTransportPlan Normal = new(450000, 150000, 210000);
    private static RealtimeOxygenationSource Create(PhysiologyIllustrationConfiguration? configuration = null) =>
        new(PhysiologyIllustrationSource.CreateTransport(configuration ?? PhysiologyIllustrationConfiguration.Default, Normal, 66667), new());

    private static void RealtimeGridHistoryAndControlChangesAreDeterministic()
    {
        var large = Create();
        var small = Create();
        for (long now = 50_000_000; now <= 10_000_000_000; now += 50_000_000)
        {
            large.AdvanceTo(now);
            small.AdvanceTo(now - 25_000_000);
            small.AdvanceTo(now);
            if (now == 50_000_000)
            {
                var before = large.Snapshot;
                long effective = large.ChangeVentilation(Normal with { TidalVolumeMicrolitersBtps = 0 }, now);
                small.ChangeVentilation(Normal with { TidalVolumeMicrolitersBtps = 0 }, now);
                Check.That(effective == 56_000_000 && large.Snapshot == before, "control change waits for the next grid interval without resetting stores");
            }
            Check.That(large.Snapshot == small.Snapshot, "fixed 8ms RK4 is independent of frame subdivision");
        }
        var snapshot = large.Snapshot;
        Check.That(snapshot.RetainedSamples == RealtimeOxygenationSource.HistoryCapacity &&
            Math.Abs(snapshot.OxygenBalanceResidualMl) < .000000000000000001m, "bounded history and oxygen ledger");
        long sampleTime = snapshot.SourceSimTimeNs - 2_003_000_000;
        var first = large.ReadAt(sampleTime);
        Check.That(first == large.ReadAt(sampleTime) && first.SourceSimTimeNs == sampleTime && large.Snapshot == snapshot,
            "delayed/interpolated sensor reads are pure");
        Reject(() => large.ReadAt(0));
        Reject(() => large.ReadAt(snapshot.SourceSimTimeNs + 1));
        var fork = large.Fork();
        fork.AdvanceTo(snapshot.SourceSimTimeNs + 250_000_000);
        Check.That(large.Snapshot == snapshot && fork.Snapshot != snapshot, "trial advance does not mutate the published source");
    }

    private static void RealtimeVentilationUsesCurrentPhysicalInputs()
    {
        var normal = Create();
        var slow = Create(PhysiologyIllustrationConfiguration.Default with { BreathPeriodMilliseconds = 10000 });
        var oxygen = Create();
        oxygen.ChangeVentilation(Normal with { InspiredOxygenMillionths = 1000000 }, 0);
        var deadSpace = Create();
        deadSpace.ChangeVentilation(Normal with { DeadSpaceMicrolitersBtps = 450000 }, 0);
        for (long time = 1_000_000_000; time <= 90_000_000_000; time += 1_000_000_000)
        {
            normal.AdvanceTo(time);
            slow.AdvanceTo(time);
            oxygen.AdvanceTo(time);
            deadSpace.AdvanceTo(time);
        }
        Check.That(normal.Snapshot.SaturationMilliPercent > 96000 && oxygen.Snapshot.SaturationMilliPercent > normal.Snapshot.SaturationMilliPercent &&
            slow.Snapshot.SaturationMilliPercent < normal.Snapshot.SaturationMilliPercent - 3000 &&
            deadSpace.Snapshot.SaturationMilliPercent < slow.Snapshot.SaturationMilliPercent,
            "current RR, FiO2 and effective VT minus VD change the solved arterial oxygenation");
    }

    private static void RealtimePreviewMeasuresDesaturationAndRecovery()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(),
            true, realtimeOxygenation: RealtimeOxygenationConfiguration.ReferenceAdult);
        bool warning = false, critical = false, deep = false;
        while (session.SimulationTimeNs < 300_000_000_000)
        {
            session.Advance(250_000_000);
            var reading = session.Measurements!.SpO2;
            var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, reading);
            warning |= notice?.Level == MonitorNoticeLevel.Warning;
            critical |= notice?.Level == MonitorNoticeLevel.Critical;
            if (reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent < 70000)
            { deep = true; Check.That(notice?.Level == MonitorNoticeLevel.Critical, "valid deep realtime numbers keep their alarm"); }
            if (session.SimulationTimeNs >= 8_000_000_000)
            { Check.That(reading.Status == WaveformMeasurementStatus.Valid, "ventilation changes do not reset optical measurement or signal quality"); }
            if (session.SimulationTimeNs is 30_000_000_000 or 180_000_000_000)
            {
                var before = session.Oxygenation;
                var measured = session.Measurements;
                long time = session.SimulationTimeNs;
                session.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = time == 30_000_000_000 ? 0 : 450000 });
                Check.That(session.Oxygenation == before && session.Measurements == measured && session.SimulationTimeNs == time,
                    "live update preserves stores, history, measurement window and clock");
            }
        }
        Check.That(warning && critical && deep && session.Measurements!.SpO2.SaturationMilliPercent > 95000 &&
            SpO2LimitNotice.Evaluate(true, 92000, 85000, session.Measurements.SpO2) is null,
            "realtime normal/apnea/recovery traverses warning, critical, deep number and clear");
        Check.That(Math.Abs(session.Oxygenation!.Value.OxygenBalanceResidualMl) < .000000000000000001m, "full scenario conserves oxygen");
    }

    private static void RealtimePreviewRetainsPulseQualityGates()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 15, MechanicalDurationCycles = 15 };
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
            realtimeOxygenation: RealtimeOxygenationConfiguration.ReferenceAdult);
        bool lost = false, recovered = false;
        while (session.SimulationTimeNs < 36_000_000_000)
        {
            session.Advance(250_000_000);
            if (session.SimulationTimeNs == 22_000_000_000)
            {
                lost = session.Measurements!.SpO2.Status == WaveformMeasurementStatus.PoorSignal &&
                    session.Measurements.SpO2.SaturationMilliPercent is null && session.Oxygenation!.Value.SaturationMilliPercent > 0;
            }
            if (session.SimulationTimeNs == 36_000_000_000) { recovered = session.Measurements!.SpO2.Status == WaveformMeasurementStatus.Valid; }
        }
        Check.That(lost && recovered, "latent arterial oxygen cannot bypass pulsatility requirements during mechanical arrest");
    }

    private static void RealtimeInvalidInputDoesNotPublishState()
    {
        var source = Create();
        source.AdvanceTo(50_000_000);
        var before = source.Snapshot;
        Reject(() => source.ChangeVentilation(Normal with { InspiredOxygenMillionths = 0 }, 50_000_000));
        Reject(() => source.ChangeVentilation(Normal, 60_000_000));
        Reject(() => source.AdvanceTo(2_000_000_000));
        Check.That(source.Snapshot == before, "invalid edits and time jumps leave reservoirs/history untouched");
        var transport = PhysiologyIllustrationSource.CreateTransport(PhysiologyIllustrationConfiguration.Default,
            Normal, 250000, 1_000_000);
        var excessiveFlow = new RealtimeOxygenationSource(transport, new());
        before = excessiveFlow.Snapshot;
        Reject(() => excessiveFlow.AdvanceTo(1_000_000_000));
        Check.That(excessiveFlow.Snapshot == before, "a rejected RK4 input leaves the entire trial advance unpublished");
        var fast = Create(PhysiologyIllustrationConfiguration.Default with
        { BreathPeriodMilliseconds = 1000, InspirationMilliseconds = 100 });
        before = fast.Snapshot;
        Reject(() => fast.ChangeVentilation(Normal with { TidalVolumeMicrolitersBtps = 1500000 }, 0));
        Check.That(fast.Snapshot == before, "excessive gas flow is rejected before a control change is accepted");
        Reject(() => _ = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(),
            true, opticalSaturationMilliPercent: 98000, realtimeOxygenation: RealtimeOxygenationConfiguration.ReferenceAdult));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected argument rejection.");
    }
}
