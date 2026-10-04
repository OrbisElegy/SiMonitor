// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored sensor coupling, not a CO2 mass-balance or lung-mechanics solver.
// The existing signal settings describe the reference VT/VD of 450/150 mL.
internal static class VentilationWaveformCoupling
{
    internal static PhysiologyWaveformChannelPlan Respiration(PhysiologyWaveformChannelPlan channel,
        VentilationTransportPlan ventilation)
    {
        RealtimeOxygenationSource.ValidateVentilation(ventilation);
        return channel with
        {
            Bands = Array.AsReadOnly(channel.Bands.Select(band => band.Trigger == PhysiologyCycleEventKind.InspirationStart
                ? band with
                {
                    TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)value * ventilation.TidalVolumeMicrolitersBtps, 450000))).ToArray())
                } : band).ToArray())
        };
    }

    internal static PhysiologyWaveformChannelPlan Capnogram(PhysiologyWaveformChannelPlan channel,
        VentilationTransportPlan ventilation)
    {
        RealtimeOxygenationSource.ValidateVentilation(ventilation);
        var pattern = channel.Physiology.RespiratoryPattern;
        int length = RespiratoryPatternDepth.Length(pattern);
        int[] depths = Enumerable.Range(0, length).Select(slot => RespiratoryPatternDepth.At(pattern, (ulong)slot)).ToArray();
        List<EventWaveformBand> bands = [];
        foreach (var band in channel.Bands)
        {
            foreach (int depth in depths.Distinct())
            {
                long tidalNanoliters = (long)ventilation.TidalVolumeMicrolitersBtps * depth;
                long deadSpaceNanoliters = (long)ventilation.DeadSpaceMicrolitersBtps * 1000;
                if (!ventilation.AirwayOpen || tidalNanoliters <= deadSpaceNanoliters) { continue; }
                var phases = band.PhasePoints!.ToArray();
                long expirationNs = phases[^2].OffsetNs;
                // Scale the authored dead-space segment by VD/VT relative to
                // 150/450, retaining a positive rise and plateau at all limits.
                long deadSpaceNs = Math.Clamp((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)phases[1].OffsetNs * deadSpaceNanoliters * 3, tidalNanoliters), 1, expirationNs - 2);
                long riseNs = Math.Min(phases[2].OffsetNs - phases[1].OffsetNs, expirationNs - deadSpaceNs - 1);
                phases[1] = phases[1] with { OffsetNs = deadSpaceNs };
                phases[2] = phases[2] with { OffsetNs = deadSpaceNs + riseNs };
                var gains = band.ExpirationCycleGainsPermille;
                if (length > 1)
                {
                    gains = Array.AsReadOnly(Enumerable.Range(0, length).Select(slot => depths[slot] == depth
                        ? gains?[slot] ?? 1000 : 0).ToArray());
                }
                bands.Add(band with { PhasePoints = Array.AsReadOnly(phases), ExpirationCycleGainsPermille = gains });
            }
        }
        // Preserve the inspired baseline and sensor timing even with no gas.
        if (bands.Count == 0)
        {
            bands.Add(channel.Bands[0] with
            {
                TableQ32 = Array.AsReadOnly(new long[channel.Bands[0].TableQ32.Count]),
                ExpirationCycleGainsPermille = null
            });
        }
        return channel with { Bands = bands.AsReadOnly() };
    }
}
