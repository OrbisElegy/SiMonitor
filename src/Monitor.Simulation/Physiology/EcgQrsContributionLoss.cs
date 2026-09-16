// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Explicit decomposition: reference = residual + authored contribution.
// The remaining signal is residual + (1-loss)*contribution. The contribution
// is not identifiable from the reference ECG and must be supplied by the author.
public sealed record EcgQrsContributionLoss(int AmplitudeMicrovolts = 1200,
    int DurationPermille = 750, int LossPermille = 1000)
{
    public bool IsActive => AmplitudeMicrovolts != 0 && LossPermille != 0;

    internal void Validate()
    {
        if (AmplitudeMicrovolts is < 0 or > 4000 || DurationPermille is < 100 or > 1000 || LossPermille is < 0 or > 1000)
        { throw new EventWaveformException("EcgQrs.InvalidContributionLoss", "contributionLoss"); }
    }

    internal EventWaveformBand? CreateBand(EcgCycleTiming timing)
    {
        Validate();
        if (!IsActive) { return null; }
        long duration = checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)timing.QrsDurationNs * DurationPermille, 1000));
        if (duration <= 0) { throw new EventWaveformException("EcgQrs.InvalidContributionLoss", "duration"); }
        // Reuse the offline unit bell 16*x^2*(1-x)^2. Separate finite support
        // keeps the reference QRS intact outside this contribution.
        return new(PhysiologyCycleEventKind.VentricularElectrical, 0, duration,
            Array.AsReadOnly(StSegmentTables.Arch.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                -(Int128)value * AmplitudeMicrovolts * LossPermille, 1000))).ToArray()));
    }

    internal void AddRegionalBands(List<EventWaveformBand>[] additions, EcgCycleTiming timing, EcgChestInfarctionPlan region)
    {
        if (CreateBand(timing) is not { } band) { return; }
        for (int chest = 0; chest < 6; chest++)
        { if ((region.ResolvedChestMask & (1 << chest)) != 0) { additions[chest + 4].Add(band); } }
        if (region.Territory is not (InfarctionTerritory.Inferior or InfarctionTerritory.Lateral)) { return; }
        int positive = region.Territory == InfarctionTerritory.Inferior ? 3 : 1;
        var third = band with
        {
            TableQ32 = Array.AsReadOnly(band.TableQ32.Select(v =>
            checked((long)FixedPointMath.RoundDivideTiesToEven(v, 3))).ToArray())
        };
        var opposite = third with { TableQ32 = Array.AsReadOnly(third.TableQ32.Select(v => -v).ToArray()) };
        foreach (int limb in new[] { 0, 1, 3 })
        {
            if (limb == positive) { additions[limb].Add(third); additions[limb].Add(third); }
            else { additions[limb].Add(opposite); }
        }
    }
}
