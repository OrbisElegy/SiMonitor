// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Full-cycle source illustration, not a measured EtCO2/RR or gas transport model.
public sealed record CapnogramPlan(long DeadSpaceNs, long RiseNs, long InspiratoryFallNs,
    int BaselineMmHg, int EndExpiratoryMmHg, int? PlateauStartCentiMmHg = null)
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
        var table = Array.AsReadOnly(values);
        var bands = Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.ExpirationStart, 0, duration, table, Array.AsReadOnly(phases)),
        });
        return new(physiology, new(channelId, "AcqCO2_100@1", 1, 100, BaselineMmHg, 1), bands, 200, qualityFlags);
    }
}
