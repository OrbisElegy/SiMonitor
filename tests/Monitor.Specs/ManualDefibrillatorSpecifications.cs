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

internal static class ManualDefibrillatorSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ChargeHoldReleaseAndSyncUseAuthoritativeDelivery), ChargeHoldReleaseAndSyncUseAuthoritativeDelivery),
        new(nameof(DeviceProfilesValidatePersistAndOverrideDrafts), DeviceProfilesValidatePersistAndOverrideDrafts),
        new(nameof(ShockSinusTargetPreservesNoncardiacInputs), ShockSinusTargetPreservesNoncardiacInputs)
    ];

    private static ManualDefibrillator Device() => new(Guid.Parse("ac556473-7fbd-4c02-9178-e71a73827c01"),
        DefibrillatorConfiguration.Default with { ChargeDurationMilliseconds = 1000, AutoDisarmSeconds = 20 }, 200);
    private static readonly Guid Interaction = Guid.Parse("ac556473-7fbd-4c02-9178-e71a73827c02");

    private static void ChargeHoldReleaseAndSyncUseAuthoritativeDelivery()
    {
        var device = Device();
        Check.That(!device.PressShock(Interaction, 0), "uncharged shock is rejected");
        Check.That(device.BeginCharge(0) && !device.BeginCharge(0), "only one charge starts");
        Check.That(device.Tick(500_000_000, 0, []) is null && device.ChargeProgressPermille == 500, "charge reports actual progress without delivery");
        device.Tick(1_000_000_000, 0, []);
        Check.That(device.State.Energy == EnergyState.Ready && device.PressShock(Interaction, 1_000_000_000), "ready accepts press");
        Check.That(device.Tick(1_499_999_999, 1_000_000_000, []) is null, "short hold cannot deliver");
        device.ReleaseShock(1_500_000_000);
        Check.That(device.Tick(2_000_000_000, 2_000_000_000, []) is null && device.State.Energy == EnergyState.Idle,
            "release at threshold before the timer wins and disarms");
        device.BeginCharge(2_000_000_000);
        device.Tick(3_000_000_000, 3_000_000_000, []);
        device.PressShock(Interaction, 3_000_000_000);
        var delivery = device.Tick(3_500_000_000, 3_500_000_000, []);
        Check.That(delivery is { DeliverySequence: 1, EnergyJoules: 200, Mode: DefibrillationMode.ManualAsynchronous } &&
            device.State.Attempt == DefibrillationAttemptState.Delivered && device.ChargeProgressPermille == 0,
            "threshold emits one delivery with the charged energy and mode");
        Check.That(device.Tick(4_000_000_000, 4_000_000_000, []) is null, "held input cannot duplicate delivery");
        device.SetSynchronized(true, 4_000_000_000);
        device.BeginCharge(4_000_000_000);
        device.Tick(5_000_000_000, 5_000_000_000, []);
        device.PressShock(Interaction, 5_000_000_000);
        Check.That(device.Tick(5_500_000_000, 5_500_000_000, [5_400_000_000]) is null &&
            device.State.Attempt == DefibrillationAttemptState.AwaitingSync, "sync never uses a QRS from before the hold threshold");
        Check.That(device.Tick(5_600_000_000, 5_600_000_000, [5_500_000_000, 6_000_000_000]) is null,
            "old and future QRS cannot authorize sync delivery");
        delivery = device.Tick(5_700_000_000, 5_700_000_000, [5_600_000_000]);
        Check.That(delivery is { DeliverySequence: 2, Mode: DefibrillationMode.ManualSynchronized }, "new acquired QRS delivers once");
        device.BeginCharge(6_000_000_000);
        device.Tick(7_000_000_000, 7_000_000_000, []);
        device.PressShock(Interaction, 7_000_000_000);
        device.Tick(7_500_000_000, 7_500_000_000, []);
        Check.That(device.Tick(17_000_000_000, 8_000_000_000, [7_600_000_000]) is null &&
            device.State.Attempt == DefibrillationAttemptState.Expired, "safety-clock timeout beats a QRS even when simulation lags");
        device.BeginCharge(18_000_000_000);
        device.Tick(40_000_000_000, 8_000_000_000, []);
        Check.That(device.State.Energy == EnergyState.Idle, "stalled host expires charge from its scheduled ready time");
        device.BeginCharge(41_000_000_000);
        device.SelectEnergy(150, 41_500_000_000);
        Check.That(device.State.Energy == EnergyState.Idle && device.EnergyJoules == 150, "changing a step cancels charging");
        device.BeginCharge(42_000_000_000);
        device.SetSynchronized(false, 42_500_000_000);
        Check.That(device.State.Energy == EnergyState.Idle, "mode changes cancel charge");
    }

    private static void DeviceProfilesValidatePersistAndOverrideDrafts()
    {
        int[] steps = [5, 20, 50, 100, 200, 360];
        var configured = new DefibrillatorConfiguration(steps, DefibrillationWaveformKind.MonophasicDampedSine, 2500, 45) { EcgRecoveryMilliseconds = 700 };
        var resolved = DefibrillatorConfiguration.Resolve(DefibrillatorConfiguration.Default, configured);
        steps[0] = 99;
        Check.That(resolved.EnergyStepsJoules[0] == 5 && resolved.Waveform == configured.Waveform && resolved.NearestEnergy(150) == 100,
            "device overrides are owned snapshots and unavailable energies choose the lower tied step");
        foreach (int[] invalid in new int[][] { [], [1, 1], [10, 5], [0, 10], [1, 1001] })
        {
            try { (resolved with { EnergyStepsJoules = invalid }).Snapshot(); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid energy steps accepted");
        }
        string path = Path.Combine(Path.GetTempPath(), "defibrillator-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Defibrillator: resolved)), "profile saves");
            var loaded = store.Load(out bool rejected);
            Check.That(!rejected && loaded.Defibrillator!.EnergyStepsJoules.SequenceEqual(resolved.EnergyStepsJoules) &&
                loaded.Defibrillator.Waveform == resolved.Waveform && loaded.Defibrillator.ChargeDurationMilliseconds == 2500 &&
                loaded.Defibrillator.AutoDisarmSeconds == 45 && loaded.Defibrillator.EcgRecoveryMilliseconds == 700, "device profile roundtrips independently of a generator");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["Version"] = 15;
            document.AsObject().Remove("Defibrillator");
            File.WriteAllText(path, document.ToJsonString());
            loaded = store.Load(out rejected);
            Check.That(!rejected && loaded.Defibrillator!.EnergyStepsJoules.SequenceEqual(DefibrillatorConfiguration.Default.EnergyStepsJoules),
                "legacy preferences receive the generic default profile");
            document["Version"] = 16;
            File.WriteAllText(path, document.ToJsonString());
            store.Load(out rejected);
            Check.That(rejected, "new documents cannot silently omit their device configuration");
        }
        finally { File.Delete(path); }
    }

    private static void ShockSinusTargetPreservesNoncardiacInputs()
    {
        var vf = PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration) with
        { BreathPeriodMilliseconds = 5000, InspirationMilliseconds = 2200, Co2EndExpiratoryMmHg = 32, CvpBaselineCentiMmHg = 800 };
        var session = new LocalMonitorPreviewSession(vf, MonitorDisplayConfiguration.Default(), true,
            opticalSaturationMilliPercent: 96000, electricalTherapy: new("ecgTemplate.t021", new(true, 250, 150)));
        var sinus = session.PrepareSinusAfterShock(new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default) { PacingAllowed = true });
        Check.That(sinus.Configuration.BreathPeriodMilliseconds == 5000 && sinus.Configuration.InspirationMilliseconds == 2200 &&
            sinus.Configuration.Co2EndExpiratoryMmHg == 32 && sinus.Configuration.CvpBaselineCentiMmHg == 800,
            "sinus preparation retains noncardiac inputs from the active source");
        var result = session.ApplyElectricalShock(new(1, 0, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 200), sinus);
        Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled, "confirmed delivery schedules a valid sinus source");
        session.Advance(200_000_000);
        session.Advance(200_000_000);
        Check.That(session.ElectricalTherapy!.TemplateId == EcgElectricalTherapy.SinusTemplateId && session.PacingAllowed &&
            session.Configuration == sinus.Configuration && session.ManualVitals == sinus.ManualVitals, "conversion publishes the sinus profile with preserved inputs");
    }
}
