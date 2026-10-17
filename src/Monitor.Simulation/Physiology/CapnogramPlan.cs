// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Full-cycle signal illustration with optional transport lag and a bounded
// three-path dispersion kernel. Not a measured EtCO2/RR or validated device model.
public sealed record CapnogramPlan(long DeadSpaceNs, long RiseNs, long InspiratoryFallNs,
    int BaselineMmHg, int EndExpiratoryMmHg, int? PlateauStartCentiMmHg = null, long TransportDelayNs = 0, long DispersionStepNs = 0)
{
    public SeededExpirationPressure? SeededPressure { get; init; }
    public CapnogramIllustration Illustration { get; init; }
    public const string EvidenceId = "AuthoredCapnogram@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        if (physiology.RespiratoryPattern == RespiratoryPattern.SpontaneousIllustration)
        { return CreateSpontaneousChannel(physiology, channelId, qualityFlags); }
        if (PlateauStartCentiMmHg is not null && Illustration is not (CapnogramIllustration.Reference or
            CapnogramIllustration.Rebreathing or CapnogramIllustration.ExhaustedAbsorber or CapnogramIllustration.CardiogenicOscillations))
        { throw new EventWaveformException("Capnogram.CustomPlateauUnsupported", "plan"); }
        if (SeededPressure is { } seeded && (seeded.TargetMmHg != EndExpiratoryMmHg || BaselineMmHg != 0 ||
            physiology.RespiratoryPattern != RespiratoryPattern.Regular || physiology.RespiratoryActivity != RespiratoryActivity.Breathing ||
            physiology.ActivityAfterBreaths is not null))
        { throw new EventWaveformException("Capnogram.SeededPressureRequiresRegularBreathing", "plan"); }
        long expiration = physiology.BreathPeriodNs - physiology.InspirationDurationNs;
        if (!Enum.IsDefined(Illustration) || DeadSpaceNs <= 0 || RiseNs <= 0 || DeadSpaceNs >= expiration || RiseNs >= expiration - DeadSpaceNs ||
            InspiratoryFallNs <= 0 || InspiratoryFallNs > physiology.InspirationDurationNs ||
            BaselineMmHg < 0 || EndExpiratoryMmHg < BaselineMmHg || EndExpiratoryMmHg > 327 ||
            (PlateauStartCentiMmHg is { } plateau && (plateau < BaselineMmHg * 100 || plateau > EndExpiratoryMmHg * 100)))
        { throw new EventWaveformException("Capnogram.InvalidPlan", "plan"); }
        long duration = expiration + InspiratoryFallNs;
        if (TransportDelayNs < 0 || TransportDelayNs > long.MaxValue - duration || DispersionStepNs < 0 ||
            DispersionStepNs > (long.MaxValue - duration - TransportDelayNs) / 2)
        { throw new EventWaveformException("Capnogram.InvalidPlan", "plan"); }
        EventWaveformPhasePoint[] phases = [new(0, 0), new(DeadSpaceNs, 32), new(DeadSpaceNs + RiseNs, 96),
            new(expiration, 480), new(duration, 512)];
        long peakCounts = (EndExpiratoryMmHg - BaselineMmHg) * 100L;
        long[] values = CapnogramMorphology.Create(Illustration, peakCounts, phases,
            PlateauStartCentiMmHg is { } plateauStart ? plateauStart - BaselineMmHg * 100L : null);
        long morphologyPeakQ32 = values.Max();
        // Chest-effort events do not imply expired gas. Keep the affine baseline
        // and validated response settings, but generate no gas excursion.
        if (physiology.RespiratoryActivity != RespiratoryActivity.Breathing && physiology.ActivityAfterBreaths is null)
        { Array.Clear(values); }
        IReadOnlyList<EventWaveformBand> bands;
        if (DispersionStepNs == 0)
        {
            bands = Array.AsReadOnly(new EventWaveformBand[]
            {
                new(PhysiologyCycleEventKind.ExpirationStart, TransportDelayNs, duration,
                    Array.AsReadOnly(values), Array.AsReadOnly(phases)),
            });
        }
        else
        {
            // Illustrative 1:2:1 paths at delays0/step/2*step. Preserve the
            // original table sum exactly by assigning division residue to the
            // middle path. All three remain nonnegative; constant input keeps
            // its level. The added mean lag is one step, not clock compensation.
            long[] edge = values.Select(value => (long)FixedPointMath.RoundDivideTiesToEven(value, 4)).ToArray();
            long[] middle = values.Select((value, index) => value - 2 * edge[index]).ToArray();
            bands = Array.AsReadOnly(new EventWaveformBand[]
            {
                new(PhysiologyCycleEventKind.ExpirationStart, TransportDelayNs, duration,
                    Array.AsReadOnly(edge), Array.AsReadOnly(phases)),
                new(PhysiologyCycleEventKind.ExpirationStart, TransportDelayNs + DispersionStepNs, duration,
                    Array.AsReadOnly(middle), Array.AsReadOnly(phases)),
                new(PhysiologyCycleEventKind.ExpirationStart, TransportDelayNs + 2 * DispersionStepNs, duration,
                    Array.AsReadOnly(edge), Array.AsReadOnly(phases)),
            });
        }
        if (physiology.RespiratoryPattern != RespiratoryPattern.Regular)
        {
            var gains = RespiratoryCo2Response.Create(physiology.BreathPeriodNs, physiology.RespiratoryPattern);
            if ((Int128)BaselineMmHg * 100 * 1000 + (Int128)peakCounts * gains.Max() > 32_700_000)
            { throw new EventWaveformException("Capnogram.Co2ResponseOutOfRange", "EndExpiratoryMmHg"); }
            bands = Array.AsReadOnly(bands.Select(band => band with { ExpirationCycleGainsPermille = gains }).ToArray());
        }
        if (SeededPressure is { AmplitudeCentiMmHg: > 0 } variation)
        { bands = Array.AsReadOnly(bands.Select(band => band with { ExpirationCycleGainsPermille = variation.Gains }).ToArray()); }
        if (physiology.ActivityAfterBreaths is { } limit)
        {
            ulong? resume = physiology.ActivityDurationBreaths is { } durationBreaths ? limit + durationBreaths : null;
            bands = Array.AsReadOnly(bands.Select(band => band with { TriggerCycleLimit = limit, TriggerCycleResume = resume }).ToArray());
        }
        bands = CapnogramMorphology.ApplyBands(bands, Illustration);
        if (Illustration != CapnogramIllustration.Reference)
        {
            int maximumGain = bands.Max(band => band.ExpirationCycleGainsPermille?.Max() ?? 1000);
            if ((Int128)BaselineMmHg * 100 * FixedPointMath.Q32One * 1000 +
                (Int128)morphologyPeakQ32 * maximumGain > (Int128)32_700 * FixedPointMath.Q32One * 1000)
            { throw new EventWaveformException("Capnogram.Co2ResponseOutOfRange", "EndExpiratoryMmHg"); }
            foreach (var band in bands)
            {
                int gain = band.ExpirationCycleGainsPermille?.Max() ?? 1000;
                if (band.TableQ32.Any(value => (Int128)BaselineMmHg * 100 * FixedPointMath.Q32One * 1000 +
                    (Int128)value * gain > (Int128)32_700 * FixedPointMath.Q32One * 1000))
                { throw new EventWaveformException("Capnogram.Co2ResponseOutOfRange", "EndExpiratoryMmHg"); }
            }
            _ = EventWaveformComposition.Restore(new(bands, []));
        }
        return new(physiology, new(channelId, "AcqCO2_100@1", 1, 100, BaselineMmHg, 1), bands, qualityFlags);
    }

    internal PhysiologyWaveformChannelPlan CreateSpontaneousChannel(RegularPhysiologyPlan physiology, Guid channelId,
        uint qualityFlags, VentilationTransportPlan? ventilation = null)
    {
        // Validate the authored reference before preparing any derived slot.
        var reference = CreateChannel(physiology with { RespiratoryPattern = RespiratoryPattern.Regular }, channelId, qualityFlags);
        if (ventilation is not null) { RealtimeOxygenationSource.ValidateVentilation(ventilation); }
        var channels = new PhysiologyWaveformChannelPlan[SpontaneousRespirationSchedule.Length];
        long referenceExpiration = physiology.BreathPeriodNs - physiology.InspirationDurationNs;
        for (ulong slot = 0; slot < (ulong)channels.Length; slot++)
        {
            var cycle = SpontaneousRespirationSchedule.CyclePlan(physiology, slot);
            long expiration = cycle.BreathPeriodNs - cycle.InspirationDurationNs;
            long nextInspiration = SpontaneousRespirationSchedule.InspirationDurationNs(physiology, slot + 1);
            if (expiration < 3) { throw new EventWaveformException("Capnogram.InvalidPlan", "plan"); }
            long deadSpace = Math.Clamp(SpontaneousRespirationSchedule.Scale(DeadSpaceNs, expiration, referenceExpiration), 1, expiration - 2);
            long rise = Math.Clamp(SpontaneousRespirationSchedule.Scale(RiseNs, expiration, referenceExpiration), 1, expiration - deadSpace - 1);
            long fall = Math.Clamp(SpontaneousRespirationSchedule.Scale(InspiratoryFallNs, nextInspiration, physiology.InspirationDurationNs), 1, nextInspiration);
            bool gas = true;
            if (ventilation is not null)
            {
                long tidalNanoliters = (long)ventilation.TidalVolumeMicrolitersBtps * SpontaneousRespirationSchedule.DepthPermille(slot);
                long deadSpaceNanoliters = (long)ventilation.DeadSpaceMicrolitersBtps * 1000;
                gas = ventilation.AirwayOpen && tidalNanoliters > deadSpaceNanoliters;
                if (gas)
                {
                    deadSpace = Math.Clamp((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)deadSpace * deadSpaceNanoliters * 3, tidalNanoliters), 1, expiration - 2);
                    rise = Math.Min(rise, expiration - deadSpace - 1);
                }
            }
            // This local plan carries only waveform phase constraints. Events
            // still come from the shared irregular timeline; the falling limb
            // occupies the next breath's inspiration, not this breath's one.
            var phasePlan = cycle with
            {
                BreathPeriodNs = checked(expiration + nextInspiration),
                InspirationDurationNs = nextInspiration,
                InspiratoryPauseNs = 0,
                ExpiratoryPauseNs = 0
            };
            var shapePlan = this with { DeadSpaceNs = deadSpace, RiseNs = rise, InspiratoryFallNs = fall };
            var channel = shapePlan.CreateChannel(phasePlan, channelId, qualityFlags);
            if (!gas)
            {
                channel = channel with
                {
                    Bands = Array.AsReadOnly(channel.Bands.Select(band => band with
                    { TableQ32 = Array.AsReadOnly(new long[band.TableQ32.Count]) }).ToArray())
                };
            }
            channels[slot] = channel;
        }
        var bands = reference.Bands.Select((_, index) =>
            SpontaneousRespirationSchedule.WithCycleShapes(channels.Select(channel => channel.Bands[index]).ToArray())).ToArray();
        return reference with { Physiology = physiology, Bands = Array.AsReadOnly(bands) };
    }
}
