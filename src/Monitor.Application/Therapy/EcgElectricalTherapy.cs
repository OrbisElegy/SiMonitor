// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Application.Therapy;

public enum ElectricalShockRequirement { Synchronized, Unsynchronized, VentricularTachycardia }
public enum ElectricalConversionOutcome
{
    Eligible,
    ConversionScheduled,
    NotShockable,
    Disabled,
    WrongMode,
    EnergyTooLow,
    SourceChangePending,
    DuplicateDelivery
}

// Authored deterministic teaching response, not a clinical energy recommendation.
public sealed record ElectricalConversionSettings(bool Enabled, int MonophasicThresholdJoules, int BiphasicThresholdJoules)
{
    public const int MaximumEnergyJoules = 1000;
    public static ElectricalConversionSettings Default { get; } = new(false, 200, 150);
    public void Validate()
    {
        if (MonophasicThresholdJoules is < 0 or > MaximumEnergyJoules || BiphasicThresholdJoules is < 0 or > MaximumEnergyJoules)
        { throw new ArgumentException("ElectricalConversion.InvalidThreshold"); }
    }
}

public sealed record EcgElectricalTherapyDescriptor(string TemplateId, ElectricalShockRequirement Requirement);
public sealed record EcgElectricalTherapyProfile(string TemplateId, ElectricalConversionSettings Settings)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TemplateId) || TemplateId.Length > 128 || Settings is null)
        { throw new ArgumentException("ElectricalConversion.InvalidProfile"); }
        Settings.Validate();
    }
}
public sealed record DeliveredElectricalShock(ulong DeliverySequence, long DeliveredAtSimTimeNs,
    DefibrillationWaveformKind Waveform, DefibrillationMode Mode, int EnergyJoules);
public sealed record ElectricalConversionResult(ElectricalConversionOutcome Outcome, string? TargetTemplateId = null, long? EffectiveSimTimeNs = null);

public static class EcgElectricalTherapy
{
    public const string SinusTemplateId = "ecgTemplate.t000";
    public static IReadOnlyList<EcgElectricalTherapyDescriptor> Descriptors { get; } = Array.AsReadOnly(
        new[] { 6, 7, 8, 9, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 37, 38, 39, 66, 67, 68, 69, 70, 71 }
            .Select(index => new EcgElectricalTherapyDescriptor($"ecgTemplate.t{index:D3}", index switch
            {
                20 or 21 or 22 or 30 => ElectricalShockRequirement.Unsynchronized,
                >= 26 and <= 29 => ElectricalShockRequirement.VentricularTachycardia,
                _ => ElectricalShockRequirement.Synchronized
            })).ToArray());

    public static EcgElectricalTherapyDescriptor? Find(string templateId) => Descriptors.SingleOrDefault(d => d.TemplateId == templateId);

    public static IReadOnlyDictionary<string, ElectricalConversionSettings> Snapshot(IReadOnlyDictionary<string, ElectricalConversionSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > Descriptors.Count) { throw new ArgumentException("ElectricalConversion.InvalidTemplates", nameof(settings)); }
        var owned = new Dictionary<string, ElectricalConversionSettings>(StringComparer.Ordinal);
        foreach (var (key, value) in settings)
        {
            if (Find(key) is null || value is null) { throw new ArgumentException("ElectricalConversion.InvalidTemplates", nameof(settings)); }
            value.Validate();
            owned.Add(key, value);
        }
        return new ReadOnlyDictionary<string, ElectricalConversionSettings>(owned);
    }

    // Called only after actual delivery has been confirmed by the therapy owner.
    // A selected/charged energy or an unfulfilled synchronization request is not delivery.
    public static ElectricalConversionResult Evaluate(EcgElectricalTherapyProfile? profile,
        PhysiologyIllustrationConfiguration configuration, DefibrillationWaveformKind waveform,
        DefibrillationMode mode, int deliveredEnergyJoules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(waveform) || !Enum.IsDefined(mode) || deliveredEnergyJoules is <= 0 or > ElectricalConversionSettings.MaximumEnergyJoules)
        { throw new ArgumentException("ElectricalConversion.InvalidDelivery"); }
        profile?.Validate();
        _ = configuration.ResolvePlan();
        if (profile is null || Find(profile.TemplateId) is not { } descriptor || !Matches(profile.TemplateId, configuration))
        { return new(ElectricalConversionOutcome.NotShockable); }
        bool ventricular = descriptor.Requirement is ElectricalShockRequirement.VentricularTachycardia or ElectricalShockRequirement.Unsynchronized;
        // A non-ventricular organized ECG with no ejection is a PEA illustration.
        if (!ventricular && !configuration.VentricularMechanicalEnabled) { return new(ElectricalConversionOutcome.NotShockable); }
        if (!profile.Settings.Enabled) { return new(ElectricalConversionOutcome.Disabled); }
        bool asynchronous = descriptor.Requirement == ElectricalShockRequirement.Unsynchronized ||
            descriptor.Requirement == ElectricalShockRequirement.VentricularTachycardia && !configuration.VentricularMechanicalEnabled;
        if (mode != (asynchronous ? DefibrillationMode.ManualAsynchronous : DefibrillationMode.ManualSynchronized))
        { return new(ElectricalConversionOutcome.WrongMode); }
        bool monophasic = waveform is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential;
        int threshold = monophasic ? profile.Settings.MonophasicThresholdJoules : profile.Settings.BiphasicThresholdJoules;
        return deliveredEnergyJoules > threshold ? new(ElectricalConversionOutcome.Eligible, SinusTemplateId)
            : new(ElectricalConversionOutcome.EnergyTooLow);
    }

    private static bool Matches(string templateId, PhysiologyIllustrationConfiguration configuration) => templateId switch
    {
        "ecgTemplate.t006" or "ecgTemplate.t007" or "ecgTemplate.t066" or "ecgTemplate.t067" or
        "ecgTemplate.t068" or "ecgTemplate.t069" or "ecgTemplate.t070" or "ecgTemplate.t071" => AtrialFibrillationReference.IsPattern(configuration.ConductionPattern),
        "ecgTemplate.t008" or "ecgTemplate.t009" or "ecgTemplate.t037" or "ecgTemplate.t038" or "ecgTemplate.t039" => AtrialFlutterReference.IsPattern(configuration.ConductionPattern),
        "ecgTemplate.t020" or "ecgTemplate.t021" or "ecgTemplate.t022" => VentricularDisorganizationReference.IsPattern(configuration.ConductionPattern),
        "ecgTemplate.t023" or "ecgTemplate.t024" or "ecgTemplate.t025" => configuration.Svt,
        "ecgTemplate.t026" or "ecgTemplate.t027" or "ecgTemplate.t028" or "ecgTemplate.t029" => configuration.Vt && !configuration.VtTwisting,
        "ecgTemplate.t030" => configuration.Vt && configuration.VtTwisting,
        _ => false
    };
}
