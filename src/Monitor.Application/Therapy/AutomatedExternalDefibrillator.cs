// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Domain.Therapy;

namespace Monitor.Application.Therapy;

public enum AedPhase
{
    Off,
    Analyzing,
    WaitingForSignal,
    Charging,
    ShockAdvised,
    Cpr,
    Suspended
}
public enum AedAdvice { Undetermined, Shock, NoShock }

// Source-time ECG evidence, independent of template identity and conversion permissions.
public sealed record AedEcgEvidence(long SampleTimeNs, EcgHeartRateReading HeartRate, EcgMonitoringReading Monitoring);

// Semi-automatic teaching workflow. All time and physical delivery authority belong
// to the host/shared defibrillator; this controller never manufactures a shock.
public sealed class AutomatedExternalDefibrillator(ManualDefibrillator device)
{
    public const long AnalysisDurationNs = 8_000_000_000;
    public const long CprDurationNs = 120_000_000_000;
    private const long StableAdviceDurationNs = 2_000_000_000;
    private long _phaseStartedNs;
    private long _stableSinceNs;
    private long _lastSimulationTimeNs;
    private long _lastSafetyTimeNs;
    private long? _lastEvidenceNs;
    private AedAdvice _candidate;
    public AedPhase Phase { get; private set; }
    public AedAdvice Advice { get; private set; }
    public bool Enabled => Phase != AedPhase.Off;
    public int RemainingSeconds { get; private set; }
    public long CprElapsedNs => Phase == AedPhase.Cpr ? _lastSimulationTimeNs - _phaseStartedNs : 0;

    public void Enable(long safetyTimeNs, long simulationTimeNs)
    {
        ValidateTime(safetyTimeNs, simulationTimeNs);
        device.Disarm(safetyTimeNs);
        device.SetSynchronized(false, safetyTimeNs);
        StartAnalysis(simulationTimeNs);
    }

    public void Disable(long safetyTimeNs)
    {
        Suspend(safetyTimeNs);
        Phase = AedPhase.Off;
    }

    public void Suspend(long safetyTimeNs)
    {
        ValidateTime(safetyTimeNs, _lastSimulationTimeNs);
        device.Disarm(safetyTimeNs);
        if (Enabled) { Phase = AedPhase.Suspended; }
        Advice = AedAdvice.Undetermined;
        RemainingSeconds = 0;
        _lastEvidenceNs = null;
    }

    public void ReleaseShock(long safetyTimeNs)
    {
        if (Phase == AedPhase.ShockAdvised && device.State.Lease?.State is
            MomentaryLeaseState.Pressed or MomentaryLeaseState.Held) { Suspend(safetyTimeNs); }
        else { device.ReleaseShock(safetyTimeNs); }
    }

    public bool PressShock(Guid interactionId, long safetyTimeNs, long simulationTimeNs, AedEcgEvidence? evidence)
    {
        ValidateTime(safetyTimeNs, simulationTimeNs);
        if (Phase != AedPhase.ShockAdvised) { return false; }
        if (Classify(evidence, simulationTimeNs) != AedAdvice.Shock)
        { device.Disarm(safetyTimeNs); StartAnalysis(simulationTimeNs); return false; }
        return device.PressShock(interactionId, safetyTimeNs);
    }

    public DeliveredElectricalShock? Tick(long safetyTimeNs, long simulationTimeNs, AedEcgEvidence? evidence)
    {
        ValidateTime(safetyTimeNs, simulationTimeNs);
        if (Phase is AedPhase.Off or AedPhase.Suspended) { return null; }
        if (Phase == AedPhase.Cpr)
        {
            RemainingSeconds = SecondsLeft(CprDurationNs, simulationTimeNs);
            if (RemainingSeconds == 0) { StartAnalysis(simulationTimeNs); }
            return null;
        }
        var advice = Classify(evidence, simulationTimeNs);
        // A stream gap invalidates even apparently fresh advice from its next packet.
        bool gap = _lastEvidenceNs is { } last && evidence is { } next && next.SampleTimeNs - last > 200_000_000;
        _lastEvidenceNs = evidence?.SampleTimeNs;
        if (advice == AedAdvice.Undetermined || gap)
        {
            device.Disarm(safetyTimeNs);
            Phase = AedPhase.WaitingForSignal;
            Advice = AedAdvice.Undetermined;
            RemainingSeconds = 0;
            return null;
        }
        if (Phase == AedPhase.WaitingForSignal) { StartAnalysis(simulationTimeNs); }
        if (Phase is AedPhase.Charging or AedPhase.ShockAdvised)
        {
            if (advice != AedAdvice.Shock)
            { device.Disarm(safetyTimeNs); StartAnalysis(simulationTimeNs); return null; }
            var delivered = device.Tick(safetyTimeNs, simulationTimeNs, []);
            if (delivered is not null) { StartCpr(simulationTimeNs, AedAdvice.Shock); return delivered with { Automated = true }; }
            if (device.State.Energy == EnergyState.Ready) { Phase = AedPhase.ShockAdvised; }
            else if (device.State.Energy != EnergyState.Charging) { Suspend(safetyTimeNs); }
            return null;
        }
        if (_candidate != advice) { _candidate = advice; _stableSinceNs = simulationTimeNs; }
        RemainingSeconds = SecondsLeft(AnalysisDurationNs, simulationTimeNs);
        if (RemainingSeconds != 0 || simulationTimeNs - _stableSinceNs < StableAdviceDurationNs) { return null; }
        Advice = advice;
        if (advice == AedAdvice.NoShock) { StartCpr(simulationTimeNs, advice); }
        else if (device.BeginCharge(safetyTimeNs)) { Phase = AedPhase.Charging; }
        else { Suspend(safetyTimeNs); }
        return null;
    }

    public static AedAdvice Classify(AedEcgEvidence? evidence, long simulationTimeNs)
    {
        if (evidence is null || evidence.SampleTimeNs < 0 || evidence.SampleTimeNs > simulationTimeNs ||
            simulationTimeNs - evidence.SampleTimeNs > 200_000_000 ||
            evidence.HeartRate.Status is WaveformMeasurementStatus.NoData or WaveformMeasurementStatus.PoorSignal or WaveformMeasurementStatus.OutOfRange ||
            evidence.Monitoring.Status != WaveformMeasurementStatus.Valid) { return AedAdvice.Undetermined; }
        var monitoring = evidence.Monitoring;
        if (monitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.SuspectedVentricularFibrillation))
        { return AedAdvice.Shock; }
        if (monitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.Asystole)) { return AedAdvice.NoShock; }
        if (monitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.SupraventricularTachycardia) &&
            !monitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia))
        { return AedAdvice.NoShock; }
        // AED applies its own width/rate and freshness criteria rather than the
        // configurable monitoring alarm's run length or reference/PVC labels.
        // 100 ms is this detector's sampled-complex proxy, not clinical QRS duration.
        if (evidence.HeartRate is { Status: WaveformMeasurementStatus.Valid, MilliBeatsPerMinute: > 150_000 } &&
            evidence.HeartRate.LastBeatTimeNs is { } peak && peak <= simulationTimeNs && simulationTimeNs - peak <= 600_000_000 &&
            monitoring.LastBeat is { QrsWidthMilliseconds: >= 100, Label: not EcgBeatLabel.Paced })
        { return AedAdvice.Shock; }
        if (monitoring.Learning || evidence.HeartRate.Status != WaveformMeasurementStatus.Valid)
        { return AedAdvice.Undetermined; }
        return AedAdvice.NoShock;
    }

    private void StartAnalysis(long simulationTimeNs)
    {
        Phase = AedPhase.Analyzing;
        Advice = AedAdvice.Undetermined;
        _candidate = AedAdvice.Undetermined;
        _phaseStartedNs = simulationTimeNs;
        _stableSinceNs = simulationTimeNs;
        _lastEvidenceNs = null;
        RemainingSeconds = (int)(AnalysisDurationNs / 1_000_000_000);
    }

    private void StartCpr(long simulationTimeNs, AedAdvice advice)
    {
        Phase = AedPhase.Cpr;
        Advice = advice;
        _phaseStartedNs = simulationTimeNs;
        RemainingSeconds = (int)(CprDurationNs / 1_000_000_000);
    }

    private int SecondsLeft(long durationNs, long simulationTimeNs) =>
        (int)((Math.Max(0, durationNs - (simulationTimeNs - _phaseStartedNs)) + 999_999_999) / 1_000_000_000);

    private void ValidateTime(long safetyTimeNs, long simulationTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(safetyTimeNs, _lastSafetyTimeNs);
        ArgumentOutOfRangeException.ThrowIfLessThan(simulationTimeNs, _lastSimulationTimeNs);
        _lastSafetyTimeNs = safetyTimeNs;
        _lastSimulationTimeNs = simulationTimeNs;
    }
}
