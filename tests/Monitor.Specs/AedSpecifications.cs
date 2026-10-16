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
        new(nameof(AedUsesLiveWaveformsWithoutConversionMetadata), AedUsesLiveWaveformsWithoutConversionMetadata),
        new(nameof(AedToleratesOnlyIsolatedCaptureInEstablishedWideRhythm), AedToleratesOnlyIsolatedCaptureInEstablishedWideRhythm),
        new(nameof(AedCaptureHistoryCannotSurviveInvalidEvidence), AedCaptureHistoryCannotSurviveInvalidEvidence),
        new(nameof(AedCompletesLiveVtCaptureWorkflow), AedCompletesLiveVtCaptureWorkflow),
        new(nameof(AedEnergySequenceAdvancesOnlyAfterDelivery), AedEnergySequenceAdvancesOnlyAfterDelivery),
        new(nameof(AedEnergySequenceValidatesBeforeChangingDevice), AedEnergySequenceValidatesBeforeChangingDevice),
        new(nameof(AedCprControlsRemoveChargeAndRequireNewAnalysis), AedCprControlsRemoveChargeAndRequireNewAnalysis)
    ];

    private static readonly Guid Interaction = Guid.Parse("1424f029-6cd5-4ce8-b563-565020ba2001");
    private static readonly IReadOnlyList<int> ProtocolEnergySteps = Array.AsReadOnly(new[] { 100, 200, 300, 360 });
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

    private static void AedEnergySequenceAdvancesOnlyAfterDelivery()
    {
        var device = new ManualDefibrillator(Interaction, DefibrillatorConfiguration.Default with
        {
            EnergyStepsJoules = ProtocolEnergySteps,
            ChargeDurationMilliseconds = 100,
            AutoDisarmSeconds = 1
        }, 100);
        int[] sequence = [200, 300, 360];
        var aed = new AutomatedExternalDefibrillator(device, sequence);
        sequence[0] = 100;
        long now = Charge(aed);
        Check.That(device.EnergyJoules == 200, "AED owns an immutable protocol snapshot and selects its first step");
        aed.PressShock(Interaction, now, now, Evidence(now));
        aed.ReleaseShock(now);
        now = Charge(aed, now);
        Check.That(device.EnergyJoules == 200, "canceling a shock and restarting analysis cannot escalate energy");
        foreach (int expected in new[] { 200, 300, 360, 360 })
        {
            Check.That(aed.PressShock(Interaction, now, now, Evidence(now)), "protocol step accepts held confirmation");
            DeliveredElectricalShock? delivered = null;
            for (int i = 0; i < 10; i++)
            {
                now += StepNs;
                delivered = aed.Tick(now, now, Evidence(now));
            }
            Check.That(delivered is { Automated: true } && delivered.EnergyJoules == expected && device.EnergyJoules == expected,
                "each delivered shock retains its actual energy, including repeated final protocol steps");
            aed.ReleaseShock(now);
            aed.ResumeAnalyzing(now, now);
            for (int i = 0; i < 162; i++)
            {
                now += StepNs;
                Check.That(aed.Tick(now, now, Evidence(now)) is null, "protocol never delivers automatically");
            }
            Check.That(aed.Phase == AedPhase.ShockAdvised && device.EnergyJoules == (expected == 200 ? 300 : 360),
                "only the next analyzed charge uses the next protocol energy");
        }
        aed.Disable(now);
        aed.Enable(now, now);
        Check.That(device.EnergyJoules == 200 && device.State.Energy == EnergyState.Idle,
            "a new AED session resets the energy protocol and discards charged authorization");
    }

    private static void AedEnergySequenceValidatesBeforeChangingDevice()
    {
        var device = Device();
        device.BeginCharge(0);
        foreach (int[] sequence in new int[][] { [], [200, 360], [300, 200], [0], [1001], new int[65] })
        {
            try { _ = new AutomatedExternalDefibrillator(device, sequence); }
            catch (ArgumentException error) when (error.ParamName == "energySequenceJoules")
            {
                Check.That(device.EnergyJoules == 200 && device.State.Energy == EnergyState.Charging,
                    "invalid protocols do not modify the device");
                continue;
            }
            throw new InvalidOperationException("Invalid AED energy sequence accepted");
        }
        device.Disarm(0);
        device.SelectEnergy(150, 0);
        var generic = new AutomatedExternalDefibrillator(device);
        generic.Enable(0, 0);
        Check.That(device.EnergyJoules == 150, "omitting a protocol preserves generic selected-energy behavior");
    }

    private static void AedCprControlsRemoveChargeAndRequireNewAnalysis()
    {
        var device = Device();
        var aed = new AutomatedExternalDefibrillator(device);
        aed.PauseForCpr(0, 0);
        aed.ResumeAnalyzing(0, 0);
        Check.That(!aed.Enabled, "CPR soft keys cannot enable an off AED");
        long now = Charge(aed);
        aed.PressShock(Interaction, now, now, Evidence(now));
        aed.PauseForCpr(now, now);
        Check.That(aed.Phase == AedPhase.Cpr && aed.RemainingSeconds == 120 && device.State.Energy == EnergyState.Idle,
            "CPR pause removes charge and cancels a held shock");
        now += StepNs;
        Check.That(aed.Tick(now, now, Evidence(now)) is null, "CPR pause cannot leak a pending delivery");
        aed.ResumeAnalyzing(now, now);
        Check.That(aed.Phase == AedPhase.Analyzing && aed.RemainingSeconds == 8 &&
            !aed.PressShock(Interaction, now, now, Evidence(now)), "resume requires complete analysis and new charging");
        for (int i = 0; i < 162; i++) { now += StepNs; aed.Tick(now, now, Evidence(now)); }
        Check.That(aed.Phase == AedPhase.ShockAdvised, "fresh complete analysis restores advice after CPR pause");
        aed.Tick(now + 2_000_000_000, now, Evidence(now));
        aed.ResumeAnalyzing(now + 2_000_000_000, now);
        Check.That(aed.Phase == AedPhase.Analyzing && device.State.Energy == EnergyState.Idle,
            "resume after automatic disarm never restores previous charged authorization");
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
                Check.That(aed.Tick(session.SimulationTimeNs, session.SimulationTimeNs, session.EcgEvidence) is null,
                    "waveform analysis has no automatic discharge");
            }
            Check.That(aed.Phase == expected && session.ElectricalTherapy is null,
                "live samples classify VF/asystole/sinus/VT without template conversion metadata: " + session.EcgEvidence);
            Check.That(session.SimulationTimeNs - session.EcgEvidence!.SampleTimeNs is > 0 and <= 4_000_000 &&
                session.EcgEvidence.HeartRate == session.Measurements!.HeartRate, "AED and monitor read the same current ECG measurement");
            session.Advance(1);
            long sampleTime = session.EcgEvidence!.SampleTimeNs;
            session.Advance(1);
            Check.That(session.EcgEvidence.SampleTimeNs == sampleTime && session.DetectedBeats.Count == 0,
                "a sub-sample frame neither manufactures fresh AED evidence nor replays beats");
        }
    }

    private static AedEcgEvidence WideRhythmEvidence(long at, bool narrow = false)
    {
        long peak = at / 350_000_000 * 350_000_000;
        var evidence = Evidence(at, EcgMonitoringConditions.None);
        return evidence with
        {
            HeartRate = evidence.HeartRate with { LastBeatTimeNs = peak },
            Monitoring = evidence.Monitoring with
            {
                LastBeatPeakTimeNs = peak,
                LastBeat = new(EcgBeatLabel.Normal, narrow ? 80 : 120, 0)
            }
        };
    }

    private static long ChargeWideRhythm(AutomatedExternalDefibrillator aed)
    {
        aed.Enable(0, 0);
        long now = 0;
        while (now < 8_400_000_000 - StepNs)
        {
            now += StepNs;
            aed.Tick(now, now, WideRhythmEvidence(now));
        }
        Check.That(aed.Phase == AedPhase.ShockAdvised, "six-beat wide history supports the normal analysis and charge");
        return now;
    }

    private static void AedToleratesOnlyIsolatedCaptureInEstablishedWideRhythm()
    {
        var device = Device();
        var aed = new AutomatedExternalDefibrillator(device);
        long now = ChargeWideRhythm(aed) + StepNs;
        var capture = WideRhythmEvidence(now, narrow: true);
        Check.That(AutomatedExternalDefibrillator.Classify(capture, now) == AedAdvice.NoShock,
            "a narrow snapshot alone never authorizes shock");
        Check.That(aed.PressShock(Interaction, now, now, capture),
            "an isolated capture retains established wide-rhythm advice without a VT alarm label");
        for (; now < 8_750_000_000; now += StepNs)
        {
            Check.That(aed.Tick(now, now, WideRhythmEvidence(now, narrow: true)) is null && aed.Phase == AedPhase.ShockAdvised,
                "repeated packets of one capture do not become multiple narrow beats or deliver early");
        }
        Check.That(aed.Tick(now, now, WideRhythmEvidence(now, narrow: true)) is null &&
            aed.Phase == AedPhase.Analyzing && device.State.Energy == EnergyState.Idle,
            "a second distinct narrow beat cancels the held shock before delivery");
        Check.That(!aed.PressShock(Interaction, now, now, WideRhythmEvidence(now, narrow: true)),
            "old wide history cannot authorize a new press after recovery");
    }

    private static void AedCaptureHistoryCannotSurviveInvalidEvidence()
    {
        foreach (int interruption in Enumerable.Range(0, 12))
        {
            var device = Device();
            var aed = new AutomatedExternalDefibrillator(device);
            long now = ChargeWideRhythm(aed) + StepNs;
            if (interruption is 9 or 10) { now += 250_000_000; }
            var capture = WideRhythmEvidence(now, narrow: true);
            AedEcgEvidence? invalid = interruption switch
            {
                0 => null,
                1 => capture with { SampleTimeNs = now - 250_000_000 },
                2 => capture with { HeartRate = capture.HeartRate with { Status = WaveformMeasurementStatus.PoorSignal } },
                3 => capture with { HeartRate = capture.HeartRate with { MilliBeatsPerMinute = 150_000 } },
                4 => capture with { Monitoring = capture.Monitoring with { ActiveConditions = EcgMonitoringConditions.Asystole } },
                5 => capture with { Monitoring = capture.Monitoring with { ActiveConditions = EcgMonitoringConditions.SupraventricularTachycardia } },
                6 => capture with { Monitoring = capture.Monitoring with { LastBeat = new(EcgBeatLabel.Paced, 80, 0) } },
                7 => capture with { Monitoring = capture.Monitoring with { LastBeatPeakTimeNs = now + StepNs } },
                8 => capture with { Monitoring = capture.Monitoring with { LastBeatPeakTimeNs = null } },
                11 => capture with { Monitoring = capture.Monitoring with { LastBeatPeakTimeNs = now - 800_000_000 } },
                _ => capture
            };
            if (interruption == 10)
            { Check.That(!aed.PressShock(Interaction, now, now, invalid), "a press following a stream gap cannot reuse capture history"); }
            else { aed.Tick(now, now, invalid); }
            Check.That(device.State.Energy == EnergyState.Idle && aed.Phase != AedPhase.ShockAdvised,
                "capture tolerance never overrides invalid, slow, paced or explicitly nonshockable evidence");
            now += StepNs;
            aed.Tick(now, now, WideRhythmEvidence(now, narrow: true));
            Check.That(aed.Advice == AedAdvice.Undetermined && device.State.Energy == EnergyState.Idle,
                "resumed narrow evidence cannot recover discarded authorization");
        }
    }

    private static void AedCompletesLiveVtCaptureWorkflow()
    {
        foreach (long startNs in new long[] { 0, 8_000_000_000, 14_000_000_000 })
        {
            var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.VtPreset with { VtCapture = true },
                MonitorDisplayConfiguration.Default(), true);
            while (session.SimulationTimeNs < startNs) { session.Advance(StepNs); }
            var device = new ManualDefibrillator(Interaction, DefibrillatorConfiguration.Default, 200);
            var aed = new AutomatedExternalDefibrillator(device);
            aed.Enable(startNs, startNs);
            bool ready = false;
            long? pressedAtNs = null;
            DeliveredElectricalShock? delivered = null;
            while (session.SimulationTimeNs < startNs + 40_000_000_000 && delivered is null)
            {
                session.Advance(StepNs);
                long now = session.SimulationTimeNs;
                var evidence = session.EcgEvidence!;
                delivered = aed.Tick(now, now, evidence);
                if (aed.Phase == AedPhase.ShockAdvised) { ready = true; }
                if (now >= startNs + 12_000_000_000)
                { Check.That(ready, "capture during analysis or default charging must not restart the workflow"); }
                if (ready && delivered is null)
                { Check.That(aed.Phase == AedPhase.ShockAdvised, "later isolated captures retain the charged advice"); }
                if (pressedAtNs is null && now >= startNs + 25_000_000_000 &&
                    evidence.Monitoring.LastBeat is { QrsWidthMilliseconds: < 100 })
                {
                    Check.That(aed.PressShock(Interaction, now, now, evidence), "live capture accepts explicit confirmation of sustained VT advice");
                    pressedAtNs = now;
                }
                if (pressedAtNs is null || now - pressedAtNs < 500_000_000)
                { Check.That(delivered is null, "capture handling never bypasses the full explicit hold"); }
            }
            Check.That(delivered is { Automated: true, DeliverySequence: 1 } && aed.Phase == AedPhase.Cpr &&
                session.ElectricalTherapy is null, "live VT capture completes one confirmed discharge without template metadata");
        }
    }
}
