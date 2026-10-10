// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class PostShockSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(EveryDeliveredShockHasAnUnusableVisibleEcgArtifact), EveryDeliveredShockHasAnUnusableVisibleEcgArtifact),
        new(nameof(PostShockPauseIsPhysiologicalOptionalAndReplaceable), PostShockPauseIsPhysiologicalOptionalAndReplaceable),
        new(nameof(PostShockSamplesAreIndependentOfAdvancePartition), PostShockSamplesAreIndependentOfAdvancePartition),
        new(nameof(ShockPresentationDoesNotWaitForAcquisition), ShockPresentationDoesNotWaitForAcquisition),
        new(nameof(SynchronizationUsesLiveSampleEvidence), SynchronizationUsesLiveSampleEvidence),
        new(nameof(PostShockBeatCuesFollowPresentedSamples), PostShockBeatCuesFollowPresentedSamples),
        new(nameof(RbbbSvtFailedShockReacquiresAcrossRecoveryPhases), RbbbSvtFailedShockReacquiresAcrossRecoveryPhases),
        new(nameof(VtachFailedShockReacquiresAndAsynchronousShockConverts), VtachFailedShockReacquiresAndAsynchronousShockConverts)
    ];

    private static readonly PhysiologyIllustrationConfiguration Vf =
        PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration);

    private static LocalMonitorPreviewSession Session(int pauseMilliseconds) => new(Vf,
        MonitorDisplayConfiguration.Default(), true, electricalTherapy: new("ecgTemplate.t021",
            new(true, 250, 150) { PostShockPauseMilliseconds = pauseMilliseconds }));

    private static DeliveredElectricalShock Shock(LocalMonitorPreviewSession session, int energy = 200) =>
        new(1, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, energy);

    private static LocalMonitorPreviewSession Sinus(LocalMonitorPreviewSession session) =>
        session.PrepareSinusAfterShock(new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default));

    private static void Advance(LocalMonitorPreviewSession session, long untilNs, long stepNs = 50_000_000)
    {
        while (session.SimulationTimeNs < untilNs) { session.Advance(Math.Min(stepNs, untilNs - session.SimulationTimeNs)); }
    }

    private static void RbbbSvtFailedShockReacquiresAcrossRecoveryPhases()
    {
        var configuration = PhysiologyIllustrationConfiguration.SvtPreset with { SvtRbbb = true };
        foreach (int offsetMilliseconds in new[] { 0, 80, 140, 160, 180, 200, 220, 280 })
        {
            var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
                electricalTherapy: new("ecgTemplate.t024", new(true, 300, 150)));
            Advance(session, 15_000_000_000 + offsetMilliseconds * 1_000_000L);
            var delivery = Shock(session, 150) with { Mode = DefibrillationMode.ManualSynchronized };
            Check.That(session.ApplyElectricalShock(delivery, Sinus(session)).Outcome == ElectricalConversionOutcome.EnergyTooLow,
                "RBBB SVT remains after the failed synchronized shock");
            while (session.SimulationTimeNs < 35_000_000_000)
            {
                session.Advance(50_000_000);
                Check.That((session.Measurements!.EcgMonitoring.ActiveConditions &
                    (EcgMonitoringConditions.SuspectedVentricularFibrillation | EcgMonitoringConditions.VentricularTachycardia)) == 0,
                    "organized RBBB SVT cannot become VF/VT after a failed shock at phase " + offsetMilliseconds);
            }
            Check.That(session.Configuration == configuration && session.Measurements!.HeartRate.MilliBeatsPerMinute == 200000 &&
                session.Measurements.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.SupraventricularTachycardia),
                "QRS counting and SVT reacquire without requiring a source reset");
        }
    }

    private static void VtachFailedShockReacquiresAndAsynchronousShockConverts()
    {
        foreach (bool pulse in new[] { true, false })
        {
            var configuration = PhysiologyIllustrationConfiguration.VtPreset with { VentricularMechanicalEnabled = pulse };
            var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
                electricalTherapy: new("ecgTemplate.t026", new(true, 250, 150) { PostShockPauseMilliseconds = 800 }));
            Advance(session, 15_000_000_000);
            Check.That(session.Measurements!.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia),
                "actual VT startup reaches the monitoring alarm");
            var failed = session.ApplyElectricalShock(Shock(session, 150), Sinus(session));
            Check.That(failed.Outcome == ElectricalConversionOutcome.EnergyTooLow && session.PendingSourceTimeNs is null,
                "a threshold-equal asynchronous shock fails without switching rhythm");
            Advance(session, 30_000_000_000);
            Check.That(session.Configuration == configuration && session.Measurements!.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia),
                "failed-shock artifact recovery reacquires VT rather than only PVCs");
            while (session.SimulationTimeNs < 300_000_000_000)
            {
                session.Advance(200_000_000);
                Check.That(session.Measurements!.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia),
                    "sustained VT cannot fall back to PVC alarms during the five-minute scenario");
            }
            var result = session.ApplyElectricalShock(Shock(session) with { DeliverySequence = 2 }, Sinus(session));
            Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled, "sufficient asynchronous energy converts enabled VT with or without ejection");
            Advance(session, 300_500_000_000);
            Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent, "VT conversion honors its configured pause");
            Advance(session, 325_000_000_000);
            Check.That(session.ElectricalTherapy!.TemplateId == EcgElectricalTherapy.SinusTemplateId &&
                session.Measurements!.HeartRate.MilliBeatsPerMinute == 75000 &&
                !session.Measurements.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia),
                "actual post-shock samples and monitoring reach sinus rhythm and clear VT");
        }
    }

    private static void EveryDeliveredShockHasAnUnusableVisibleEcgArtifact()
    {
        foreach (var kind in Enum.GetValues<DefibrillationWaveformKind>())
        {
            var artifact = new DefibrillationEcgArtifact(kind, 123_000_000, 1000);
            var plane = new WaveformPlane(PhysiologyIllustrationSource.ChannelId(0), 250, 1, 0, 1, 1, 0, 1,
                WaveformQualityEncoding.Ranges, Enumerable.Repeat((short)99, 50).ToArray(), [new(1, 2, 8)]);
            var changed = artifact.Apply(plane, 0);
            Check.That(changed.Samples[0] == 99 && changed.QualityRanges[0] == new WaveformQualityRange(1, 2, 8),
                "pre-shock samples and pre-existing quality flags are preserved");
            Check.That(changed.Samples.Any(value => value >= 3000) && changed.QualityRanges.Any(range => range.QualityFlags == 1),
                "all discharge kinds produce a visible saturated transient on an unaligned sample grid");
            Check.That(ReferenceEquals(plane, artifact.Apply(plane, 2_000_000_000)), "fully recovered blocks are untouched");
        }
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent },
            MonitorDisplayConfiguration.Default(), true, electricalTherapy: new("ecgTemplate.t072", ElectricalConversionSettings.Default));
        Advance(session, 4_123_000_000);
        var sinus = Sinus(session);
        var delivery = Shock(session);
        Check.That(session.ApplyElectricalShock(delivery, sinus).Outcome == ElectricalConversionOutcome.NotShockable &&
            session.PendingSourceTimeNs is null && session.ShockArtifacts.Count == 1,
            "an unsuccessful shock still has an artifact but cannot restore sinus activity");
        Check.That(session.ApplyElectricalShock(delivery, sinus).Outcome == ElectricalConversionOutcome.DuplicateDelivery &&
            session.ShockArtifacts.Count == 1, "replayed delivery cannot duplicate the visible artifact");
        bool poor = false;
        for (int i = 0; i < 140; i++)
        {
            session.Advance(50_000_000);
            poor |= session.Measurements!.HeartRate.Status == WaveformMeasurementStatus.PoorSignal;
            Check.That(session.DetectedBeats.Count == 0, "a shock transient must not become a detected QRS");
        }
        Check.That(poor && session.Samples(0, delivery.DeliveredAtSimTimeNs, delivery.DeliveredAtSimTimeNs + 100_000_000)
            .Any(sample => Math.Abs(sample.Value) > 3000), "wire history, visible samples and unusable measurement state agree");
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent, "no shock-dependent source change occurred");
    }

    private static void PostShockPauseIsPhysiologicalOptionalAndReplaceable()
    {
        var session = Session(2400);
        Advance(session, 4_000_000_000);
        var result = session.ApplyElectricalShock(Shock(session), Sinus(session), 500);
        Check.That(result.EffectiveSimTimeNs == 6_600_000_000 && session.Configuration == Vf, "the full recovery boundary is reported before publishing the pause");
        Advance(session, 4_300_000_000);
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent && session.PendingSourceTimeNs == result.EffectiveSimTimeNs &&
            session.Configuration.RespiratoryActivity == Vf.RespiratoryActivity, "a configured pause stops cardiac activity while breathing continues");
        Advance(session, 6_500_000_000);
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent, "sinus cannot activate early");
        Advance(session, 7_000_000_000);
        Check.That(session.ElectricalTherapy!.TemplateId == EcgElectricalTherapy.SinusTemplateId && session.PendingSourceTimeNs is null,
            "the pending sequence finishes with the sinus profile");
        Advance(session, 10_000_000_000);
        Check.That(session.Samples(0, 5_000_000_000, 6_400_000_000).All(sample => sample.Value == 0) &&
            session.Samples(1, 5_000_000_000, 6_400_000_000).Any(sample => sample.Value != 0), "the recorded pause has no cardiac wave but retains respiratory samples");
        var immediate = Session(0);
        immediate.ApplyElectricalShock(Shock(immediate), Sinus(immediate));
        Advance(immediate, 300_000_000);
        Check.That(immediate.Configuration.CardiacActivity == CardiacActivity.AtrialAndVentricular && immediate.PendingSourceTimeNs is null,
            "zero pause does not simulate obligatory cardiac arrest during frontend recovery");
        var cancelled = Session(2400);
        cancelled.ApplyElectricalShock(Shock(cancelled), Sinus(cancelled));
        Advance(cancelled, 300_000_000);
        var replacement = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent },
            MonitorDisplayConfiguration.Default(), true, electricalTherapy: new("ecgTemplate.t072", ElectricalConversionSettings.Default));
        cancelled.ScheduleSource(replacement, 0);
        Advance(cancelled, 4_000_000_000);
        Check.That(cancelled.ElectricalTherapy!.TemplateId == "ecgTemplate.t072" && cancelled.PendingSourceTimeNs is null,
            "a new template cancels the entire post-shock sequence without later resurrecting sinus");
    }

    private static void PostShockSamplesAreIndependentOfAdvancePartition()
    {
        var first = Session(1400);
        var second = Session(1400);
        foreach (var session in new[] { first, second })
        {
            session.DiscardStartup();
            Advance(session, 2_123_000_000);
            session.ApplyElectricalShock(Shock(session), Sinus(session), 700);
        }
        Advance(first, 10_000_000_000, 17_000_000);
        Advance(second, 10_000_000_000, 250_000_000);
        for (int channel = 0; channel < 7; channel++)
        {
            Check.That(first.Samples(channel, 0, 10_000_000_000).SequenceEqual(second.Samples(channel, 0, 10_000_000_000)),
                "shock sequence is independent of advance partition and startup source offset");
            Check.That(first.PresentedSamples(channel, 0, 10_000_000_000).SequenceEqual(second.PresentedSamples(channel, 0, 10_000_000_000)),
                "the immediate presentation remains independent of frame partition");
        }
        Check.That(first.Measurements == second.Measurements, "post-shock measured readings are deterministic");
    }

    private static void ShockPresentationDoesNotWaitForAcquisition()
    {
        foreach (bool discardStartup in new[] { false, true })
            foreach (long deliveredAtNs in new[] { 123_000_000L, 9_983_000_000L })
                foreach (int energy in new[] { 100, 200 })
                {
                    var session = Session(0);
                    if (discardStartup) { session.DiscardStartup(); }
                    Advance(session, deliveredAtNs);
                    var history = session.PresentedSamples(0, 0, deliveredAtNs).ToArray();
                    long frontier = session.PresentationFrontierNs(0);
                    var outcome = session.ApplyElectricalShock(Shock(session, energy), Sinus(session), 500);
                    Check.That(frontier == deliveredAtNs && session.PresentationFrontierNs(0) == frontier,
                        "delivery uses the existing live sweep position without jumping or moving the shock timestamp");
                    session.Advance(20_000_000);
                    Check.That(session.PresentedSamples(0, frontier, session.PresentationFrontierNs(0))
                        .Any(sample => Math.Abs(sample.Value) > 3000), "shock appears in the first frame, including during startup and across sweep wraps");
                    Check.That(!session.Samples(0, frontier, frontier + 20_000_000).Any(),
                        "visibility does not depend on the delayed acquisition packet arriving");
                    Check.That(history.SequenceEqual(session.PresentedSamples(0, 0, deliveredAtNs)), "delivery preserves every already displayed ECG sample");
                    Advance(session, deliveredAtNs + 4_000_000_000);
                    var presented = session.PresentedSamples(0, deliveredAtNs, deliveredAtNs + 1_500_000_000).ToArray();
                    Check.That(presented.SequenceEqual(session.Samples(0, deliveredAtNs, deliveredAtNs + 1_500_000_000)),
                        "later acquired ECG agrees exactly with the immediate trace, including recovery and successful conversion");
                    Check.That(!session.PresentedSamples(0, deliveredAtNs + 2_200_000_000, deliveredAtNs + 3_000_000_000)
                        .Any(sample => Math.Abs(sample.Value) > 3000), "packet arrival cannot replay the saturated shock transient");
                    for (int channel = 0; channel < 7; channel++)
                    {
                        Check.That(session.PresentedSamples(channel, 0, session.FrontierNs).SequenceEqual(session.Samples(channel, 0, session.FrontierNs)) &&
                            session.PresentationFrontierNs(channel) == session.SimulationTimeNs,
                            "all channels share the live frontier and retain their authored response to the same delivered shock");
                    }
                    Check.That(outcome.Outcome == (energy == 200 ? ElectricalConversionOutcome.ConversionScheduled : ElectricalConversionOutcome.EnergyTooLow),
                        "immediate presentation does not change the conversion energy rule");
                }
    }

    private static void SynchronizationUsesLiveSampleEvidence()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), true);
        session.DiscardStartup();
        List<DetectedEcgBeat> immediate = [];
        List<DetectedEcgBeat> acquired = [];
        List<DetectedPlethPulse> livePulses = [];
        List<DetectedPlethPulse> acquiredPulses = [];
        while (session.SimulationTimeNs < 6_000_000_000)
        {
            session.Advance(17_000_000);
            immediate.AddRange(session.SynchronizationBeats);
            acquired.AddRange(session.DetectedBeats);
            livePulses.AddRange(session.LiveBeatEvidence!.PlethPulses);
            acquiredPulses.AddRange(session.DetectedPulses);
            Check.That(session.SynchronizationBeats.All(beat => beat.ConfirmedAtNs < session.SimulationTimeNs &&
                session.SimulationTimeNs - beat.ConfirmedAtNs < 17_000_000), "live QRS confirmations arrive in their actual frame");
        }
        Check.That(immediate.Count >= 6 && immediate.Select(beat => beat.PeakTimeNs).Distinct().Count() == immediate.Count &&
            acquired.Count > 0 && acquired.All(immediate.Contains), "live and delayed detectors agree on exact peaks without replay");
        Check.That(livePulses.Count >= 5 && acquiredPulses.Count > 0 && acquiredPulses.All(livePulses.Contains),
            "live PLETH confirms the same acquired peaks without waiting for delayed packets");
        session.ApplyElectricalShock(Shock(session), Sinus(session), 600);
        long start = session.SimulationTimeNs;
        while (session.SimulationTimeNs < start + 600_000_000)
        {
            session.Advance(17_000_000);
            Check.That(!session.SynchronizationBeats.Any(beat => beat.PeakTimeNs >= start), "a discharge transient cannot create a synchronization marker or authorize a shock");
        }
    }

    private static void PostShockBeatCuesFollowPresentedSamples()
    {
        foreach (bool discardStartup in new[] { false, true })
            foreach (int pauseMilliseconds in new[] { 0, 2400 })
            {
                var session = Session(pauseMilliseconds);
                if (discardStartup) { session.DiscardStartup(); }
                Advance(session, 5_123_000_000);
                var delivery = Shock(session);
                session.ApplyElectricalShock(delivery, Sinus(session), 700);
                Check.That(session.LiveBeatEvidence!.EcgBeats.Count == 0 &&
                    session.LiveBeatEvidence.EcgStatus == WaveformMeasurementStatus.PoorSignal,
                    "delivery withdraws the current frame's ECG cues before the next sample arrives");
                var source = new MonitorBeatSource();
                List<long> cues = [];
                int plethCues = 0;
                bool delayedPacketsObserved = false;
                long until = session.SimulationTimeNs + 14_000_000_000;
                while (session.SimulationTimeNs < until)
                {
                    session.Advance(50_000_000);
                    var live = session.LiveBeatEvidence!;
                    Check.That(live.SampleTimeNs == session.PresentationFrontierNs(0), "beat routing uses the visible sweep clock");
                    source.Update(MonitorBeatMode.Ecg, live.EcgStatus, live.PlethStatus, live.SampleTimeNs);
                    foreach (var beat in live.EcgBeats)
                    {
                        Check.That(live.SampleTimeNs - beat.ConfirmedAtNs is >= 0 and <= 50_000_000 &&
                            source.Accept(MonitorBeatOrigin.Ecg, beat.ConfirmedAtNs), "fresh QRS cues are accepted in the same UI frame");
                        Check.That(session.ShockArtifacts.All(artifact => !artifact.Contains(beat.PeakTimeNs)),
                            "discharge and recovery artifacts cannot emit QRS tones");
                        // PeakTime is the detector's filtered deflection, which
                        // need not equal the raw trace's largest positive sample.
                        var visibleQrs = session.PresentedSamples(0, beat.PeakTimeNs - 80_000_000, beat.PeakTimeNs + 80_000_000).ToArray();
                        Check.That(visibleQrs.Length > 0 && visibleQrs.Max(sample => sample.Value) - visibleQrs.Min(sample => sample.Value) >= 200,
                            "the detected QRS complex is present on the displayed ECG");
                        cues.Add(beat.PeakTimeNs);
                    }
                    Check.That(live.PlethPulses.All(pulse => live.SampleTimeNs - pulse.ConfirmedAtNs is >= 0 and <= 50_000_000),
                        "PLETH fallback uses the same live clock, not delayed packets");
                    plethCues += live.PlethPulses.Count;
                    delayedPacketsObserved |= session.DetectedBeats.Any(beat => live.SampleTimeNs - beat.ConfirmedAtNs > 1_000_000_000);
                }
                Check.That(cues.Count >= 8 && cues.Distinct().Count() == cues.Count && plethCues >= 6 && delayedPacketsObserved,
                    "recovered QRS/PLETH cues remain live and unique while delayed measurement packets continue independently");
            }
    }
}
