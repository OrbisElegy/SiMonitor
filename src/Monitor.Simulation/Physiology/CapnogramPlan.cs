// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Full-cycle signal illustration with optional transport lag and a bounded
// three-path dispersion kernel. Not a measured EtCO2/RR or validated device model.
public sealed record CapnogramPlan(long DeadSpaceNs, long RiseNs, long InspiratoryFallNs,
    int BaselineMmHg, int EndExpiratoryMmHg, int? PlateauStartCentiMmHg = null, long TransportDelayNs = 0, long DispersionStepNs = 0)
{
    public const string EvidenceId = "InfirmaryCapnogramDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        long expiration = physiology.BreathPeriodNs - physiology.InspirationDurationNs;
        if (DeadSpaceNs <= 0 || RiseNs <= 0 || DeadSpaceNs >= expiration || RiseNs >= expiration - DeadSpaceNs ||
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
        long[] values = CapnogramTables.Cycle.Select(value => checked(value * peakCounts)).ToArray();
        if (PlateauStartCentiMmHg is { } plateauStart)
        {
            // Remap amplitudes around the existing C landmark without moving
            // phase times or changing the next-inspiration fall. Null retains
            // the original seed scaling bit-for-bit.
            long startCounts = plateauStart - BaselineMmHg * 100L;
            long seedC = CapnogramTables.Cycle[96];
            for (int index = 0; index <= 480; index++)
            {
                long seed = CapnogramTables.Cycle[index];
                values[index] = index <= 96
                    ? (long)FixedPointMath.RoundDivideTiesToEven((Int128)seed * startCounts * FixedPointMath.Q32One, seedC)
                    : checked(startCounts * FixedPointMath.Q32One + (long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)(seed - seedC) * (peakCounts - startCounts) * FixedPointMath.Q32One,
                        FixedPointMath.Q32One - seedC));
            }
        }
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
        if (physiology.ActivityAfterBreaths is { } limit)
        { bands = Array.AsReadOnly(bands.Select(band => band with { TriggerCycleLimit = limit }).ToArray()); }
        return new(physiology, new(channelId, "AcqCO2_100@1", 1, 100, BaselineMmHg, 1), bands, 200, qualityFlags);
    }
}
