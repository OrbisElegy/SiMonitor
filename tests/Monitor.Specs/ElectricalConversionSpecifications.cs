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

internal static class ElectricalConversionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ElectricalConversionUsesStrictIndependentThresholds), ElectricalConversionUsesStrictIndependentThresholds),
        new(nameof(VtachAsynchronousResponseRetainsTemplateAndEnergyGuards), VtachAsynchronousResponseRetainsTemplateAndEnergyGuards),
        new(nameof(ElectricalConversionPreservesClockAndRejectsReplay), ElectricalConversionPreservesClockAndRejectsReplay),
        new(nameof(ElectricalConversionPreferencesSurviveTemplateChanges), ElectricalConversionPreferencesSurviveTemplateChanges),
    ];

    private static readonly ElectricalConversionSettings Enabled = new(true, 300, 150);
    private static readonly PhysiologyIllustrationConfiguration Vf = PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration);
    private static EcgElectricalTherapyProfile Profile(string id) => new(id, Enabled);

    private static void ElectricalConversionUsesStrictIndependentThresholds()
    {
        foreach (var waveform in Enum.GetValues<DefibrillationWaveformKind>())
        {
            bool mono = waveform is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential;
            int threshold = mono ? 300 : 150;
            foreach (int energy in new[] { threshold - 1, threshold, threshold + 1 })
            {
                var result = EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021"), Vf, waveform, DefibrillationMode.ManualAsynchronous, energy);
                Check.That(result.Outcome == (energy > threshold ? ElectricalConversionOutcome.Eligible : ElectricalConversionOutcome.EnergyTooLow), "strict independent waveform thresholds");
                Check.That(result.TargetTemplateId == (energy > threshold ? EcgElectricalTherapy.SinusTemplateId : null), "only success proposes a sinus target");
            }
        }
        Check.That(EcgElectricalTherapy.Descriptors.Count == 24 && EcgElectricalTherapy.Descriptors.Select(d => d.TemplateId).Distinct().Count() == 24, "bounded unique eligible catalogue");
        var wave = DefibrillationWaveformKind.BiphasicTruncatedExponential;
        foreach (bool pulse in new[] { true, false })
        {
            var vt = PhysiologyIllustrationConfiguration.VtPreset with { VentricularMechanicalEnabled = pulse };
            Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t026"), vt, wave, DefibrillationMode.ManualAsynchronous, 200).Outcome == ElectricalConversionOutcome.Eligible,
                "ejection does not prohibit an authored asynchronous VT response");
            Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t026"), vt, wave, DefibrillationMode.ManualSynchronized, 200).Outcome ==
                (pulse ? ElectricalConversionOutcome.Eligible : ElectricalConversionOutcome.WrongMode), "synchronized VT response retains its ejection context");
        }
        var af = PhysiologyIllustrationConfiguration.Fibrillation();
        Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t006"), af, wave, DefibrillationMode.ManualAsynchronous, 1000).Outcome == ElectricalConversionOutcome.WrongMode, "high energy cannot bypass synchronization");
        Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t006"), af with { VentricularMechanicalEnabled = false }, wave, DefibrillationMode.ManualSynchronized, 1000).Outcome == ElectricalConversionOutcome.NotShockable, "organized PEA is excluded");
        Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021") with { Settings = Enabled with { Enabled = false } }, Vf, wave, DefibrillationMode.ManualAsynchronous, 1000).Outcome == ElectricalConversionOutcome.Disabled, "disabled response cannot be overridden by energy");
        Check.That(EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021"), PhysiologyIllustrationConfiguration.Default, wave, DefibrillationMode.ManualAsynchronous, 1000).Outcome == ElectricalConversionOutcome.NotShockable, "stale template identity does not make sinus shockable");
        Reject(() => EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021"), Vf, (DefibrillationWaveformKind)99, DefibrillationMode.ManualAsynchronous, 200));
        Reject(() => EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021"), Vf, wave, (DefibrillationMode)99, 200));
        Reject(() => EcgElectricalTherapy.Evaluate(Profile("ecgTemplate.t021"), Vf, wave, DefibrillationMode.ManualAsynchronous, 0));
        Reject(() => new ElectricalConversionSettings(true, -1, 10).Validate());
        Reject(() => new ElectricalConversionSettings(false, 10, 1001).Validate());
    }

    private static void VtachAsynchronousResponseRetainsTemplateAndEnergyGuards()
    {
        foreach (int index in new[] { 26, 27, 28, 29 })
        {
            var configuration = PhysiologyIllustrationConfiguration.VtPreset with
            { VtFusion = index == 27, VtCapture = index == 28, VtBidirectional = index == 29 };
            var profile = Profile($"ecgTemplate.t{index:D3}");
            foreach (var waveform in Enum.GetValues<DefibrillationWaveformKind>())
            {
                int threshold = waveform is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential ? 300 : 150;
                foreach (int energy in new[] { threshold, threshold + 1 })
                {
                    Check.That(EcgElectricalTherapy.Evaluate(profile, configuration, waveform, DefibrillationMode.ManualAsynchronous, energy).Outcome ==
                        (energy > threshold ? ElectricalConversionOutcome.Eligible : ElectricalConversionOutcome.EnergyTooLow),
                        "every organized VT variant retains strict waveform-specific thresholds for asynchronous delivery");
                }
                Check.That(EcgElectricalTherapy.Evaluate(profile with { Settings = Enabled with { Enabled = false } }, configuration,
                    waveform, DefibrillationMode.ManualAsynchronous, 1000).Outcome == ElectricalConversionOutcome.Disabled,
                    "allowing asynchronous VT does not override the authored permission");
                Check.That(EcgElectricalTherapy.Evaluate(profile, configuration, waveform, DefibrillationMode.ManualAsynchronous, 1000, automated: true).Outcome == ElectricalConversionOutcome.Disabled,
                    "manual VT permission cannot enable an AED response");
                Check.That(EcgElectricalTherapy.Evaluate(profile with { AedSettings = Enabled }, configuration,
                    waveform, DefibrillationMode.ManualAsynchronous, 1000, automated: true).Outcome == ElectricalConversionOutcome.Eligible,
                    "an independently enabled AED VT response uses the same actual asynchronous mode");
            }
        }
    }

    private static void ElectricalConversionPreservesClockAndRejectsReplay()
    {
        var display = MonitorDisplayConfiguration.Default();
        var session = new LocalMonitorPreviewSession(Vf, display, true, electricalTherapy: Profile("ecgTemplate.t021"));
        session.DiscardStartup();
        for (int i = 0; i < 20; i++) { session.Advance(200_000_000); }
        var sinus = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, display, true,
            electricalTherapy: Profile(EcgElectricalTherapy.SinusTemplateId));
        var delivery = new DeliveredElectricalShock(1, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential, DefibrillationMode.ManualAsynchronous, 150);
        var history = session.Blocks.ToArray();
        Check.That(session.ApplyElectricalShock(delivery, sinus).Outcome == ElectricalConversionOutcome.EnergyTooLow && session.PendingSourceTimeNs is null, "equal energy has no effect");
        Check.That(session.ApplyElectricalShock(delivery with { EnergyJoules = 200 }, sinus).Outcome == ElectricalConversionOutcome.DuplicateDelivery, "one physical delivery cannot be reinterpreted");
        Check.That(session.ApplyElectricalShock(delivery with { DeliverySequence = 2, EnergyJoules = 150 }, sinus).Outcome == ElectricalConversionOutcome.EnergyTooLow, "successive shocks do not add energy");
        var wrong = new LocalMonitorPreviewSession(Vf, display, true, electricalTherapy: Profile("ecgTemplate.t021"));
        Reject(() => session.ApplyElectricalShock(delivery with { DeliverySequence = 3, EnergyJoules = 151 }, wrong));
        Check.That(session.PendingSourceTimeNs is null, "invalid target rejects without publishing transition or consuming delivery");
        var result = session.ApplyElectricalShock(delivery with { DeliverySequence = 3, EnergyJoules = 151 }, sinus);
        Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled && result.EffectiveSimTimeNs == session.SimulationTimeNs + 200_000_000 && session.Blocks.SequenceEqual(history), "conversion uses live continuation without clearing history");
        Check.That(session.ApplyElectricalShock(delivery with { DeliverySequence = 4, EnergyJoules = 151 }, sinus).Outcome == ElectricalConversionOutcome.SourceChangePending, "pending conversion blocks another transition");
        for (int i = 0; i < 50; i++) { session.Advance(200_000_000); }
        Check.That(session.SimulationTimeNs == 14_000_000_000 && session.ElectricalTherapy?.TemplateId == EcgElectricalTherapy.SinusTemplateId,
            "automatic continuation acquires sinus at the existing clock");
        // Artifact quality interrupts the detector. Allow a complete fresh rate
        // window plus acquisition latency rather than assume instantaneous HR.
        for (int i = 0; i < 20; i++) { session.Advance(200_000_000); }
        Check.That(session.Measurements!.HeartRate.MilliBeatsPerMinute == 75000,
            "a complete post-recovery measurement window reacquires the sinus rate");
        Check.That(session.ApplyElectricalShock(delivery with { DeliverySequence = 5, DeliveredAtSimTimeNs = session.SimulationTimeNs, EnergyJoules = 1000 }, sinus).Outcome == ElectricalConversionOutcome.NotShockable, "sinus cannot repeatedly convert");
    }

    private static void ElectricalConversionPreferencesSurviveTemplateChanges()
    {
        var entries = EcgElectricalTherapy.Descriptors.Select((d, i) => (d.TemplateId, Value: new ElectricalConversionSettings(i % 2 == 0, 200 + i, 100 + i) { PostShockPauseMilliseconds = i * 100 })).ToDictionary(p => p.TemplateId, p => p.Value);
        var generator = new MonitorGeneratorPreferences(21, "室颤（粗波）", 0, 0, "", new Dictionary<string, decimal?>(), new Dictionary<string, bool>(), new Dictionary<string, int>())
        { ElectricalConversions = EcgElectricalTherapy.Snapshot(entries) };
        entries.Clear();
        Check.That(generator.ElectricalConversions.Count == 24, "snapshot owns its map");
        string directory = Path.Combine(Path.GetTempPath(), "monitor-conversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            var sinus = generator with { Ecg = 0, EcgName = "窦性参考" };
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: sinus)), "save after automatic template change");
            var loaded = store.Load(out bool rejected).Generator!;
            Check.That(!rejected && loaded.Ecg == 0 && loaded.ElectricalConversions.OrderBy(p => p.Key).SequenceEqual(generator.ElectricalConversions.OrderBy(p => p.Key)), "all dormant template settings survive disk round trip");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["Version"] = 13;
            document["Generator"]!.AsObject().Remove("ElectricalConversions");
            File.WriteAllText(path, document.ToJsonString());
            Check.That(store.Load(out rejected).Generator!.ElectricalConversions.Count == 0 && !rejected, "legacy files load with disabled defaults");
            document["Version"] = 14;
            document["Generator"]!["ElectricalConversions"] = JsonNode.Parse("{\"ecgTemplate.t072\":{\"Enabled\":true,\"MonophasicThresholdJoules\":0,\"BiphasicThresholdJoules\":0}}");
            File.WriteAllText(path, document.ToJsonString());
            store.Load(out rejected);
            Check.That(rejected, "unsupported template settings are rejected");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid electrical conversion rejection");
    }
}
