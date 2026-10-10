// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AedSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(AedRequiresAnalysisAndHeldConfirmation), AedRequiresAnalysisAndHeldConfirmation),
        new(nameof(AedInvalidatesAdviceBeforeDelivery), AedInvalidatesAdviceBeforeDelivery),
        new(nameof(AedNoShockAndCprCyclesReanalyze), AedNoShockAndCprCyclesReanalyze),
        new(nameof(AedClassifiesEcgEvidenceConservatively), AedClassifiesEcgEvidenceConservatively),
        new(nameof(AedUsesLiveWaveformsWithoutConversionMetadata), AedUsesLiveWaveformsWithoutConversionMetadata)
    ];

    private static readonly Guid Interaction = Guid.Parse("1424f029-6cd5-4ce8-b563-565020ba2001");
    private const long StepNs = 50_000_000;
    private static ManualDefibrillator Device() => new(Interaction,
        DefibrillatorConfiguration.Default with { ChargeDurationMilliseconds = 100, AutoDisarmSeconds = 1 }, 200);
    private static AedEcgEvidence Evidence(long at, EcgMonitoringConditions conditions = EcgMonitoringConditions.SuspectedVentricularFibrillation) =>
        new(at, new(WaveformMeasurementStatus.Valid, 180_000, at),
            EcgMonitoringReading.NoData with
            {
                Status = WaveformMeasurementStatus.Valid,
                Learning = false,
                ActiveConditions = conditions,
                LastBeat = new(EcgBeatLabel.Ventricular, 160, 0)
            });

    private static long Charge(AutomatedExternalDefibrillator aed, long start = 0)
    {
        aed.Enable(start, start);
        long now = start;
        while (now < start + AutomatedExternalDefibrillator.AnalysisDurationNs + 100_000_000)
        {
            now += StepNs;
            Check.That(aed.Tick(now, now, Evidence(now)) is null, "analysis and automatic charging never deliver a shock");
        }
        Check.That(aed.Phase == AedPhase.ShockAdvised, "fresh sustained VF evidence leads to charged advice");
        return now;
    }

    private static void AedRequiresAnalysisAndHeldConfirmation()
    {
        var device = Device();
        device.SetSynchronized(true, 0);
        var aed = new AutomatedExternalDefibrillator(device);
        long now = Charge(aed);
        Check.That(device.State.Mode == DefibrillationMode.ManualAsynchronous && device.EnergyJoules == 200,
            "AED clears synchronization and uses the selected device step");
        Check.That(aed.PressShock(Interaction, now, now, Evidence(now)), "charged advice accepts an explicit press");
        for (int i = 0; i < 9; i++)
        {
            now += StepNs;
            Check.That(aed.Tick(now, now, Evidence(now)) is null, "short holds never deliver");
        }
        now += StepNs;
        var shock = aed.Tick(now, now, Evidence(now));
        Check.That(shock is { EnergyJoules: 200, DeliverySequence: 1 } && aed.Phase == AedPhase.Cpr &&
            aed.Advice == AedAdvice.Shock && aed.RemainingSeconds == 120, "one authoritative discharge starts CPR");
        aed.ReleaseShock(now);
        Check.That(aed.Phase == AedPhase.Cpr && aed.Tick(now + StepNs, now + StepNs, null) is null,
            "release after discharge and the recovery artifact do not cancel CPR or duplicate delivery");
        aed.Disable(now + StepNs);
        Check.That(!aed.Enabled && device.State.Energy == EnergyState.Idle, "mode exit discards all authorization");
    }

    private static void AedInvalidatesAdviceBeforeDelivery()
    {
        foreach (int interruption in Enumerable.Range(0, 6))
        {
            var device = Device();
            var aed = new AutomatedExternalDefibrillator(device);
            long now = Charge(aed);
            aed.PressShock(Interaction, now, now, Evidence(now));
            now += StepNs;
            switch (interruption)
            {
                case 0: aed.Suspend(now); break;
                case 1: aed.Disable(now); break;
                case 2: aed.ReleaseShock(now); break;
                case 3: aed.Tick(now, now, null); break;
                case 4: aed.Tick(now, now, Evidence(now, EcgMonitoringConditions.Asystole)); break;
                case 5: aed.Tick(now, now, Evidence(now - 500_000_000)); break;
            }
            Check.That(device.State.Energy == EnergyState.Idle && aed.Phase != AedPhase.ShockAdvised,
                "cancellation, signal loss, changed rhythm and stale evidence immediately disarm");
            Check.That(!aed.PressShock(Interaction, now, now, Evidence(now)), "old advice cannot authorize another press");
            Check.That(aed.Tick(now + StepNs, now + StepNs, Evidence(now + StepNs)) is null,
                "old hold cannot leak into resumed analysis");
        }
        var expired = new AutomatedExternalDefibrillator(Device());
        long at = Charge(expired);
        // Safety clock can advance independently of simulation time.
        Check.That(expired.Tick(at + 2_000_000_000, at, Evidence(at)) is null && expired.Phase == AedPhase.Suspended,
            "ready timeout suspends AED and requires a new analysis");
        var gap = new AutomatedExternalDefibrillator(Device());
        at = Charge(gap);
        Check.That(gap.Tick(at + 500_000_000, at + 500_000_000, Evidence(at + 500_000_000)) is null &&
            gap.Phase == AedPhase.WaitingForSignal, "a fresh packet following a stream gap cannot reuse previous advice");
    }

    private static void AedNoShockAndCprCyclesReanalyze()
    {
        var device = Device();
        var aed = new AutomatedExternalDefibrillator(device);
        aed.Enable(0, 0);
        long now = 0;
        while (now < AutomatedExternalDefibrillator.AnalysisDurationNs)
        {
            now += StepNs;
            aed.Tick(now, now, Evidence(now, EcgMonitoringConditions.Asystole));
        }
        Check.That(aed.Phase == AedPhase.Cpr && aed.Advice == AedAdvice.NoShock && device.State.Energy == EnergyState.Idle,
            "asystole enters CPR without charging");
        Check.That(!aed.PressShock(Interaction, now, now, Evidence(now)), "shock is blocked during CPR even with VF evidence");
        now += AutomatedExternalDefibrillator.CprDurationNs;
        aed.Tick(now, now, null);
        Check.That(aed.Phase == AedPhase.Analyzing && aed.RemainingSeconds == 8, "CPR expiry starts a complete new analysis");
        for (int i = 0; i < 162; i++) { now += StepNs; aed.Tick(now, now, Evidence(now)); }
        Check.That(aed.Phase == AedPhase.ShockAdvised, "new VF is advised in the next cycle");
    }

    private static void AedClassifiesEcgEvidenceConservatively()
    {
        var vt = Evidence(0, EcgMonitoringConditions.VentricularTachycardia);
        Check.That(AutomatedExternalDefibrillator.Classify(vt, 0) == AedAdvice.Shock, "sustained fast wide VT is shockable");
        Check.That(AutomatedExternalDefibrillator.Classify(vt with { HeartRate = vt.HeartRate with { MilliBeatsPerMinute = 150_000 } }, 0) == AedAdvice.NoShock,
            "the monitor's lower VT threshold alone does not authorize AED");
        Check.That(AutomatedExternalDefibrillator.Classify(vt with { Monitoring = vt.Monitoring with { LastBeat = new(EcgBeatLabel.Normal, 80, 0) } }, 0) == AedAdvice.NoShock,
            "narrow QRS cannot satisfy the VT policy");
        var svt = Evidence(0, EcgMonitoringConditions.SupraventricularTachycardia);
        Check.That(AutomatedExternalDefibrillator.Classify(svt, 0) == AedAdvice.NoShock,
            "sustained conducted wide SVT evidence is not overridden by width alone");
        Check.That(AutomatedExternalDefibrillator.Classify(svt with { Monitoring = svt.Monitoring with { LastBeat = new(EcgBeatLabel.Normal, 80, 0) } }, 0) == AedAdvice.NoShock,
            "fast heart rate without ventricular evidence is not shockable");
        Check.That(AutomatedExternalDefibrillator.Classify(Evidence(0) with { HeartRate = new(WaveformMeasurementStatus.PoorSignal, null, null) }, 0) == AedAdvice.Undetermined,
            "poor signal wins over old VF conditions");
        Check.That(AutomatedExternalDefibrillator.Classify(Evidence(0, EcgMonitoringConditions.None) with
        { Monitoring = vt.Monitoring with { Learning = true, ActiveConditions = EcgMonitoringConditions.None, LastBeat = new(EcgBeatLabel.Learning, 80, 0) } }, 0) == AedAdvice.Undetermined,
            "learning is not a no-shock diagnosis");
        Check.That(AutomatedExternalDefibrillator.Classify(Evidence(1), 0) == AedAdvice.Undetermined, "future samples are rejected");
    }

    private static void AedUsesLiveWaveformsWithoutConversionMetadata()
    {
        foreach (var (configuration, expected) in new[]
        {
            (PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration), AedPhase.ShockAdvised),
            (PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent }, AedPhase.Cpr),
            (PhysiologyIllustrationConfiguration.Default, AedPhase.Cpr),
            (PhysiologyIllustrationConfiguration.VtPreset, AedPhase.ShockAdvised),
            (PhysiologyIllustrationConfiguration.SvtPreset, AedPhase.Cpr),
            (PhysiologyIllustrationConfiguration.SvtPreset with { SvtRbbb = true }, AedPhase.Cpr)
        })
        {
            var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true);
            var aed = new AutomatedExternalDefibrillator(Device());
            aed.Enable(0, 0);
            for (int i = 0; i < 600 && aed.Phase is not (AedPhase.ShockAdvised or AedPhase.Cpr); i++)
            {
                session.Advance(StepNs);
                Check.That(aed.Tick(session.SimulationTimeNs, session.SimulationTimeNs, session.LiveEcgEvidence) is null,
                    "waveform analysis has no automatic discharge");
            }
            Check.That(aed.Phase == expected && session.ElectricalTherapy is null,
                "live samples classify VF/asystole/sinus/VT without template conversion metadata: " + session.LiveEcgEvidence);
            Check.That(session.LiveEcgEvidence!.SampleTimeNs > session.FrontierNs, "AED evidence follows immediate ECG, not delayed acquisition");
        }
    }
}
