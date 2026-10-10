// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class AedConversionSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(AedConversionHasIndependentThresholdsAndPause), AedConversionHasIndependentThresholdsAndPause),
        new(nameof(AedConversionPreferencesMigrateAndRemainIndependent), AedConversionPreferencesMigrateAndRemainIndependent)
    ];

    private static void AedConversionHasIndependentThresholdsAndPause()
    {
        var configuration = PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration);
        var profile = new EcgElectricalTherapyProfile("ecgTemplate.t021", ElectricalConversionSettings.Default)
        { AedSettings = new(true, 250, 100) { PostShockPauseMilliseconds = 1500 } };
        ElectricalConversionOutcome Evaluate(int energy, DefibrillationWaveformKind waveform, bool automated) =>
            EcgElectricalTherapy.Evaluate(profile, configuration, waveform, DefibrillationMode.ManualAsynchronous, energy, automated).Outcome;
        Check.That(Evaluate(200, DefibrillationWaveformKind.BiphasicTruncatedExponential, false) == ElectricalConversionOutcome.Disabled,
            "AED enable does not enable manual conversion");
        Check.That(Evaluate(100, DefibrillationWaveformKind.BiphasicTruncatedExponential, true) == ElectricalConversionOutcome.EnergyTooLow &&
            Evaluate(101, DefibrillationWaveformKind.BiphasicTruncatedExponential, true) == ElectricalConversionOutcome.Eligible &&
            Evaluate(200, DefibrillationWaveformKind.MonophasicDampedSine, true) == ElectricalConversionOutcome.EnergyTooLow,
            "AED uses its own strict and waveform-specific thresholds");
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), electricalTherapy: profile);
        for (int i = 0; i < 20; i++) { session.Advance(50_000_000); }
        var sinus = session.PrepareSinusAfterShock(new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default));
        var delivered = new DeliveredElectricalShock(1, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 200)
        { Automated = true };
        var result = session.ApplyElectricalShock(delivered, sinus);
        Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled && result.EffectiveSimTimeNs >= 2_700_000_000,
            "AED delivery selects its independent pause rather than manual zero pause");
        for (int i = 0; i < 8; i++) { session.Advance(50_000_000); }
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent, "AED pause enters actual cardiac silence");
        for (int i = 0; i < 40; i++) { session.Advance(50_000_000); }
        Check.That(session.ElectricalTherapy!.TemplateId == EcgElectricalTherapy.SinusTemplateId, "AED completes the shared sinus conversion path");
        profile = profile with { Settings = new(true, 0, 0), AedSettings = ElectricalConversionSettings.Default };
        Check.That(Evaluate(200, DefibrillationWaveformKind.BiphasicTruncatedExponential, true) == ElectricalConversionOutcome.Disabled &&
            Evaluate(200, DefibrillationWaveformKind.BiphasicTruncatedExponential, false) == ElectricalConversionOutcome.Eligible,
            "manual enable cannot bypass a disabled AED response");
        var atrial = new EcgElectricalTherapyProfile("ecgTemplate.t006", new(true, 0, 0))
        { AedSettings = new(true, 0, 0) };
        Check.That(EcgElectricalTherapy.Evaluate(atrial, PhysiologyIllustrationConfiguration.Default with
        { ConductionPattern = AvConductionPattern.AtrialFibrillationCoarseIllustration },
            DefibrillationWaveformKind.BiphasicTruncatedExponential, DefibrillationMode.ManualSynchronized, 200, automated: true).Outcome == ElectricalConversionOutcome.WrongMode,
            "an automated delivery cannot borrow a synchronized-only response even with a crafted profile");
    }

    private static void AedConversionPreferencesMigrateAndRemainIndependent()
    {
        var manual = new ElectricalConversionSettings(true, 200, 100) { PostShockPauseMilliseconds = 500 };
        var automated = new ElectricalConversionSettings(false, 300, 200) { PostShockPauseMilliseconds = 2500 };
        var generator = new MonitorGeneratorPreferences(21, "VF", 0, 0, "1", new Dictionary<string, decimal?>(),
            new Dictionary<string, bool>(), new Dictionary<string, int>())
        {
            ElectricalConversions = new Dictionary<string, ElectricalConversionSettings> { ["ecgTemplate.t021"] = manual },
            AedElectricalConversions = new Dictionary<string, ElectricalConversionSettings> { ["ecgTemplate.t021"] = automated }
        };
        string path = Path.Combine(Path.GetTempPath(), "monitor-aed-conversion-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: generator)), "save independent maps");
            var loaded = store.Load(out bool rejected).Generator!;
            Check.That(!rejected && loaded.AedElectricalConversions["ecgTemplate.t021"] == automated &&
                loaded.ElectricalConversions["ecgTemplate.t021"] == manual, "both response maps survive persistence independently");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["Version"] = 17;
            document["Generator"]!.AsObject().Remove("AedElectricalConversions");
            File.WriteAllText(path, document.ToJsonString());
            loaded = store.Load(out rejected).Generator!;
            Check.That(!rejected && loaded.AedElectricalConversions["ecgTemplate.t021"] == manual &&
                !ReferenceEquals(loaded.AedElectricalConversions, loaded.ElectricalConversions),
                "legacy shared response migrates into an independent owned AED map");
            document["Version"] = 18;
            document["Generator"]!["AedElectricalConversions"] = JsonNode.Parse("{\"ecgTemplate.t021\":{\"Enabled\":true,\"MonophasicThresholdJoules\":200,\"BiphasicThresholdJoules\":100,\"PostShockPauseMilliseconds\":10001}}");
            File.WriteAllText(path, document.ToJsonString());
            store.Load(out rejected);
            Check.That(rejected, "invalid AED pause is rejected before publishing a preference snapshot");
        }
        finally { File.Delete(path); }
    }
}
