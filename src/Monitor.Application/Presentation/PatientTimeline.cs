// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Therapy;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed record PatientTimelineTreatmentPolicy(bool AllowShock = true, bool AllowPacing = true,
    string? ShockSuccessTimelineId = null, string? ShockFailureTimelineId = null, string? PacingTimelineId = null)
{
    public void Validate()
    {
        foreach (string? id in new[] { ShockSuccessTimelineId, ShockFailureTimelineId, PacingTimelineId })
        {
            if (id is not null && !PatientTimelineDefinition.ValidId(id))
            { throw new ArgumentException("PatientTimeline.InvalidBranch"); }
        }
    }
}

public sealed record PatientTimelineDefinition(string Id, string Name, IReadOnlyList<PatientTimelineEvent> Events,
    PatientTimelineTreatmentPolicy Treatment)
{
    public PatientTimelineEvent? InitialState { get; init; }

    internal static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 80 &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public void Validate()
    {
        if (!ValidId(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl))
        { throw new ArgumentException("PatientTimeline.InvalidDefinition"); }
        ArgumentNullException.ThrowIfNull(Treatment);
        Treatment.Validate();
        if (InitialState is { } initial)
        {
            initial.Validate();
            if (initial.StartTimeNs != 0 || initial.DurationNs != 0 || initial.Trigger != PatientTimelineTrigger.TimelineTime ||
                initial.Treatment is not null)
            { throw new ArgumentException("PatientTimeline.InvalidInitialState"); }
        }
        _ = PatientTimelineEvent.Snapshot(Events);
    }
}

public sealed record PatientTimelineProgram(string InitialTimelineId, IReadOnlyList<PatientTimelineDefinition> Timelines)
{
    public const int MaximumTimelines = 16;
    public const int MaximumEvents = 256;
    public string? SeedHex { get; init; }

    public void Validate() => _ = Snapshot();

    public PatientTimelineProgram Snapshot()
    {
        string? seed = string.IsNullOrEmpty(SeedHex) ? null : SeedHex;
        if (seed is not null && (seed.Length != 64 ||
            seed.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
        { throw new ArgumentException("PatientTimeline.InvalidSeed"); }
        ArgumentNullException.ThrowIfNull(Timelines);
        if (Timelines.Count is < 1 or > MaximumTimelines)
        { throw new ArgumentException("PatientTimeline.InvalidProgram"); }
        var owned = Timelines.Select(timeline =>
        {
            ArgumentNullException.ThrowIfNull(timeline);
            timeline.Validate();
            return timeline with { Events = PatientTimelineEvent.Snapshot(timeline.Events) };
        }).ToArray();
        var ids = owned.Select(timeline => timeline.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != owned.Length || !ids.Contains(InitialTimelineId) || owned.Sum(timeline => timeline.Events.Count) > MaximumEvents)
        { throw new ArgumentException("PatientTimeline.InvalidProgram"); }
        foreach (var timeline in owned)
        {
            foreach (var treatment in timeline.Events.Select(item => item.Treatment)
                .Prepend(timeline.Treatment).OfType<PatientTimelineTreatmentPolicy>())
            {
                foreach (string? target in new[] { treatment.ShockSuccessTimelineId,
                    treatment.ShockFailureTimelineId, treatment.PacingTimelineId })
                {
                    if (target is not null && !ids.Contains(target))
                    { throw new ArgumentException("PatientTimeline.InvalidBranch"); }
                }
            }
        }
        return this with { SeedHex = seed, Timelines = Array.AsReadOnly(owned) };
    }
}

// Authoring data only. Runtime cursors and waveform state are never preferences.
public sealed record PatientVitalTargets
{
    public int? HeartRateBpm { get; init; }
    public int? RespiratoryRatePerMinute { get; init; }
    public int? SaturationMilliPercent { get; init; }
    public int? EtCo2MmHg { get; init; }
    public VascularPressureTarget? Abp { get; init; }
    public VascularPressureTarget? Pa { get; init; }
    public int? CvpCentiMmHg { get; init; }
    public ManualNibp? Nibp { get; init; }
    public int? TemperatureDeciCelsius { get; init; }

    public bool IsEmpty => HeartRateBpm is null && RespiratoryRatePerMinute is null && SaturationMilliPercent is null &&
        EtCo2MmHg is null && Abp is null && Pa is null && CvpCentiMmHg is null && Nibp is null && TemperatureDeciCelsius is null;

    public void Validate()
    {
        if (HeartRateBpm is < 30 or > 180 || RespiratoryRatePerMinute is < 6 or > 60 ||
            SaturationMilliPercent is < 0 or > 100000 || EtCo2MmHg is < 5 or > 80 ||
            CvpCentiMmHg is < -500 or > 3000 || TemperatureDeciCelsius is < 0 or > 500)
        { throw new ArgumentException("PatientTimeline.InvalidTarget"); }
        ValidatePressure(Abp, true);
        ValidatePressure(Pa, false);
    }

    private static void ValidatePressure(VascularPressureTarget? target, bool arterial)
    {
        if (target is null) { return; }
        var systolic = arterial ? VascularPressureTarget.ArterialSystolicRange : VascularPressureTarget.PulmonarySystolicRange;
        var diastolic = arterial ? VascularPressureTarget.ArterialDiastolicRange : VascularPressureTarget.PulmonaryDiastolicRange;
        if (target.SystolicCentiMmHg < systolic.MinimumCentiMmHg || target.SystolicCentiMmHg > systolic.MaximumCentiMmHg ||
            target.DiastolicCentiMmHg < diastolic.MinimumCentiMmHg || target.DiastolicCentiMmHg > diastolic.MaximumCentiMmHg ||
            target.SystolicCentiMmHg - target.DiastolicCentiMmHg < VascularPressureTarget.MinimumPulseCentiMmHg)
        { throw new ArgumentException("PatientTimeline.InvalidTarget"); }
    }
}

public sealed record PatientTimelineSource(PhysiologyIllustrationConfiguration Configuration,
    EcgElectricalTherapyProfile? ElectricalTherapy);

public enum PatientTimelineTrigger { TimelineTime, AfterPrevious, Manual }

public sealed record PatientTimelineEvent(string Name, long StartTimeNs, long DurationNs, PatientVitalTargets Targets)
{
    public const long TimeStepNs = 200_000_000;
    public const long MaximumTimeNs = 3_600_000_000_000;
    public const int MaximumEvents = 64;
    public string? EcgTemplateId { get; init; }
    public int? RespirationTemplate { get; init; }
    public int? EjectionTemplate { get; init; }
    public int? PlethTemplate { get; init; }
    public int? CapnogramTemplate { get; init; }
    public VentilationTransportPlan? Ventilation { get; init; }
    public PatientTimelineTreatmentPolicy? Treatment { get; init; }
    public PatientTimelineTrigger Trigger { get; init; } = PatientTimelineTrigger.TimelineTime;

    public bool ChangesTemplate => EcgTemplateId is not null || RespirationTemplate is not null ||
        EjectionTemplate is not null || PlethTemplate is not null || CapnogramTemplate is not null;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl) ||
            StartTimeNs is < 0 or > MaximumTimeNs || DurationNs is < 0 or > MaximumTimeNs ||
            StartTimeNs > MaximumTimeNs - DurationNs || StartTimeNs % TimeStepNs != 0 || DurationNs % TimeStepNs != 0 ||
            !Enum.IsDefined(Trigger) || Trigger == PatientTimelineTrigger.Manual && StartTimeNs != 0)
        { throw new ArgumentException("PatientTimeline.InvalidEvent"); }
        ArgumentNullException.ThrowIfNull(Targets);
        Targets.Validate();
        Treatment?.Validate();
        if (EcgTemplateId is not null && (string.IsNullOrWhiteSpace(EcgTemplateId) || EcgTemplateId.Length > 128) ||
            RespirationTemplate is < 0 or > 6 || EjectionTemplate is < 0 or > 4 ||
            PlethTemplate is < 0 or > 2 || CapnogramTemplate is < 0 or > 12)
        { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
        if (Ventilation is { } ventilation && (Targets.SaturationMilliPercent is not null ||
            ventilation.TidalVolumeMicrolitersBtps is < 0 or > 1_500_000 ||
            ventilation.DeadSpaceMicrolitersBtps is < 0 or > 500_000 ||
            ventilation.InspiredOxygenMillionths is < 100_000 or > 1_000_000))
        { throw new ArgumentException("PatientTimeline.InvalidVentilation"); }
        if (Targets.IsEmpty && !ChangesTemplate && Ventilation is null && Treatment is null && Trigger == PatientTimelineTrigger.TimelineTime)
        { throw new ArgumentException("PatientTimeline.EmptyEvent"); }
    }

    public static IReadOnlyList<PatientTimelineEvent> Snapshot(IReadOnlyList<PatientTimelineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count > MaximumEvents) { throw new ArgumentException("PatientTimeline.TooManyEvents"); }
        foreach (var item in events)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.Validate();
        }
        // Legacy absolute timelines retain their stable chronological order.
        // Relative and manual nodes are an authored sequence, not sortable delays.
        var owned = events.All(item => item.Trigger == PatientTimelineTrigger.TimelineTime)
            ? events.OrderBy(item => item.StartTimeNs).ToArray() : events.ToArray();
        long previous = 0;
        foreach (var item in owned)
        {
            long start = item.Trigger switch
            {
                PatientTimelineTrigger.Manual => 0,
                PatientTimelineTrigger.AfterPrevious => previous + item.StartTimeNs,
                _ => item.StartTimeNs
            };
            if (item.Trigger == PatientTimelineTrigger.TimelineTime && start < previous || start > MaximumTimeNs - item.DurationNs)
            { throw new ArgumentException("PatientTimeline.InvalidEvent"); }
            previous = start;
        }
        return Array.AsReadOnly(owned);
    }
}
