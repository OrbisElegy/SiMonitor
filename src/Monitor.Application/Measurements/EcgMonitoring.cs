// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Measurements;

public enum EcgBeatLabel
{
    Learning,
    Normal,
    SupraventricularPremature,
    Ventricular,
    Paced,
    Unknown
}
public enum EcgMonitoringTransition
{
    Started,
    Ended,
    Interrupted,
    Occurred
}
public enum EcgQtCorrectionMethod { Bazett, Fridericia }

[Flags]
public enum EcgMonitoringConditions : ulong
{
    None = 0,
    Asystole = 1UL << 0,
    SuspectedVentricularFibrillation = 1UL << 1,
    ExtremeBradycardia = 1UL << 2,
    ExtremeTachycardia = 1UL << 3,
    HeartRateLow = 1UL << 4,
    HeartRateHigh = 1UL << 5,
    Pause = 1UL << 6,
    MissedBeat = 1UL << 7,
    VentricularTachycardia = 1UL << 8,
    NonSustainedVentricularTachycardia = 1UL << 9,
    VentricularRhythm = 1UL << 10,
    RunPvcs = 1UL << 11,
    PairPvcs = 1UL << 12,
    VentricularBigeminy = 1UL << 13,
    VentricularTrigeminy = 1UL << 14,
    MultiformPvcs = 1UL << 15,
    PvcsPerMinuteHigh = 1UL << 16,
    RonTPvc = 1UL << 17,
    SupraventricularTachycardia = 1UL << 18,
    StHigh = 1UL << 19,
    StLow = 1UL << 20,
    QtcHigh = 1UL << 21,
    DeltaQtcHigh = 1UL << 22,
    PacerNotCaptured = 1UL << 23,
    PacerNotPacing = 1UL << 24
}

public enum EcgPacingEvidenceOrigin { Acquisition, Simulation }

// Complete packet sideband on the acquired sample clock. Null means unavailable;
// empty ventricular pulses means available with no ventricular output observed.
// Simulation provenance is explicit and never represents device detection.
public sealed record EcgPacingEvidence(IReadOnlyList<long> PulseTimesNs)
{
    public IReadOnlyList<long> AtrialPulseTimesNs { get; init; } = [];
    public EcgPacingEvidenceOrigin Origin { get; init; }
    public bool VentricularPacingExpected { get; init; } = true;
    // Simulation-only startup reference; never substitutes for measured HR.
    public long? ExpectedVentricularIntervalNs { get; init; }
}

public sealed record EcgBeatMorphology(EcgBeatLabel Label, int QrsWidthMilliseconds, int TemplateDifferencePermille);
public sealed record DetectedEcgMonitoringEvent(EcgMonitoringConditions Condition, EcgMonitoringTransition Transition,
    long EvidenceFromNs, long ConfirmedAtNs, EcgRhythmInterruption Interruption = EcgRhythmInterruption.None);
public sealed record EcgRepolarizationReading(WaveformMeasurementStatus StStatus, int? StMicrovolts,
    WaveformMeasurementStatus QtStatus, int? QtMilliseconds, int? QtcMilliseconds, int? DeltaQtcMilliseconds);
public sealed record EcgMonitoringReading(WaveformMeasurementStatus Status, bool Learning,
    EcgMonitoringConditions ActiveConditions, int? PvcsLastMinute, EcgBeatMorphology? LastBeat,
    EcgRepolarizationReading Repolarization)
{
    public bool PacingEvidenceAvailable { get; init; }
    public EcgPacingEvidenceOrigin? PacingEvidenceOrigin { get; init; }
    public static EcgMonitoringReading NoData { get; } = new(WaveformMeasurementStatus.NoData, true,
        EcgMonitoringConditions.None, null, null, new(WaveformMeasurementStatus.NoData, null,
            WaveformMeasurementStatus.NoData, null, null, null));
}

// Independent teaching settings; these defaults are not a Philips device profile.
public sealed record EcgMonitoringSettings
{
    public bool PacedMode { get; init; }
    public int AsystoleMilliseconds { get; init; } = 4000;
    public int PauseMilliseconds { get; init; } = 2000;
    public int LowHeartRate { get; init; } = 50;
    public int HighHeartRate { get; init; } = 120;
    public int ExtremeLowHeartRate { get; init; } = 30;
    public int ExtremeHighHeartRate { get; init; } = 180;
    public int VtachHeartRate { get; init; } = 100;
    public int VtachRunBeats { get; init; } = 6;
    public int VentricularRhythmRunBeats { get; init; } = 5;
    public int SvtHeartRate { get; init; } = 150;
    public int SvtRunBeats { get; init; } = 5;
    public int PvcsPerMinuteLimit { get; init; } = 10;
    public int StLowMicrovolts { get; init; } = -100;
    public int StHighMicrovolts { get; init; } = 100;
    public int StOffsetMilliseconds { get; init; } = 60;
    public int QtcHighMilliseconds { get; init; } = 500;
    public int DeltaQtcHighMilliseconds { get; init; } = 60;
    public int? QtcBaselineMilliseconds { get; init; }
    public EcgQtCorrectionMethod QtCorrection { get; init; } = EcgQtCorrectionMethod.Bazett;
    public int RhythmEndDelayMilliseconds { get; init; } = 5000;

    public void Validate()
    {
        if (AsystoleMilliseconds is < 2500 or > 4000 || AsystoleMilliseconds % 250 != 0 ||
            PauseMilliseconds is < 1500 or > 2500 || PauseMilliseconds % 250 != 0 ||
            ExtremeLowHeartRate < 1 || ExtremeLowHeartRate >= LowHeartRate || LowHeartRate >= HighHeartRate ||
            HighHeartRate >= ExtremeHighHeartRate || ExtremeHighHeartRate > 350 ||
            VtachHeartRate is < 20 or > 300 || VtachRunBeats is < 3 or > 99 ||
            VentricularRhythmRunBeats is < 3 or > 99 || SvtHeartRate is < 120 or > 300 ||
            SvtRunBeats is < 3 or > 99 || PvcsPerMinuteLimit is < 1 or > 99 ||
            StLowMicrovolts is < -2000 or > 0 || StHighMicrovolts is < 0 or > 2000 ||
            StLowMicrovolts >= StHighMicrovolts || StOffsetMilliseconds is not (60 or 80) ||
            QtcHighMilliseconds is < 200 or > 800 || DeltaQtcHighMilliseconds is < 1 or > 500 ||
            QtcBaselineMilliseconds is < 200 or > 800 || !Enum.IsDefined(QtCorrection) ||
            RhythmEndDelayMilliseconds is < 0 or > 1800000)
        { throw new ArgumentException("EcgMonitoring.InvalidSettings"); }
    }
}
