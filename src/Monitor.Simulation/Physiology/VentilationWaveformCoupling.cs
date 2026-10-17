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
                    TableQ32 = Scale(band.TableQ32),
                    RespiratoryCycleShapes = band.RespiratoryCycleShapes is { } shapes ? Array.AsReadOnly(shapes.Select(shape => shape with
                    { TableQ32 = Scale(shape.TableQ32) }).ToArray()) : null
                } : band).ToArray())
        };

        IReadOnlyList<long> Scale(IReadOnlyList<long> table) => Array.AsReadOnly(table.Select(value =>
            checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * ventilation.TidalVolumeMicrolitersBtps, 450000))).ToArray());
    }

    internal static PhysiologyWaveformChannelPlan Capnogram(PhysiologyWaveformChannelPlan channel,
        VentilationTransportPlan ventilation, CapnogramPlan sourcePlan)
    {
        RealtimeOxygenationSource.ValidateVentilation(ventilation);
        if (channel.Physiology.RespiratoryPattern == RespiratoryPattern.SpontaneousIllustration)
        {
            return channel with
            {
                Bands = sourcePlan.CreateSpontaneousChannel(channel.Physiology,
                    channel.Plane.ChannelId, channel.QualityFlags, ventilation).Bands
            };
        }
        var pattern = channel.Physiology.RespiratoryPattern;
        int length = RespiratoryPatternDepth.Length(pattern);
        int[] depths = Enumerable.Range(0, length).Select(slot => RespiratoryPatternDepth.At(pattern, (ulong)slot)).ToArray();
        List<EventWaveformBand> bands = [];
        foreach (int depth in depths.Distinct())
        {
            long tidalNanoliters = (long)ventilation.TidalVolumeMicrolitersBtps * depth;
            long deadSpaceNanoliters = (long)ventilation.DeadSpaceMicrolitersBtps * 1000;
            if (!ventilation.AirwayOpen || tidalNanoliters <= deadSpaceNanoliters) { continue; }
            long expirationNs = channel.Physiology.BreathPeriodNs - channel.Physiology.InspirationDurationNs;
            // Scale the authored dead-space segment by VD/VT relative to
            // 150/450, retaining a positive rise and plateau at all limits.
            long deadSpaceNs = Math.Clamp((long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)sourcePlan.DeadSpaceNs * deadSpaceNanoliters * 3, tidalNanoliters), 1, expirationNs - 2);
            long riseNs = Math.Min(sourcePlan.RiseNs, expirationNs - deadSpaceNs - 1);
            // The Hermite slopes belong to physical time. Regenerate all paths
            // after retiming; merely changing the phase map would reintroduce
            // a corner where the expiratory rise meets the plateau.
            var depthPlan = sourcePlan with { DeadSpaceNs = deadSpaceNs, RiseNs = riseNs };
            var depthChannel = depthPlan.CreateChannel(channel.Physiology, channel.Plane.ChannelId, channel.QualityFlags);
            foreach (var band in depthChannel.Bands)
            {
                var gains = band.ExpirationCycleGainsPermille;
                if (length > 1)
                {
                    // A morphology can have a different breath-cycle length
                    // (for example four IMV slots against eleven Cheyne slots).
                    // Keep the complete joint period when applying depth gates.
                    int[] depthGains = Enumerable.Range(0, length).Select(slot => depths[slot] == depth ? 1000 : 0).ToArray();
                    gains = CapnogramMorphology.CombineGains(gains, depthGains);
                }
                bands.Add(band with { ExpirationCycleGainsPermille = gains });
            }
        }
        // Preserve the inspired baseline and sensor timing even with no gas.
        if (bands.Count == 0)
        {
            bands.Add(channel.Bands[0] with
            {
                TableQ32 = Array.AsReadOnly(new long[channel.Bands[0].TableQ32.Count]),
                ExpirationCycleGainsPermille = null,
                ExpirationActivationGainsPermille = null,
                ExpirationCycleOrigin = 0
            });
        }
        return channel with { Bands = bands.AsReadOnly() };
    }
}
