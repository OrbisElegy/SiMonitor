// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public sealed record EcgInfarctionRegion(int ChestMask = 0, InfarctionTerritory Territory = InfarctionTerritory.CustomChest);

// Components use the same reference baseline, so overlapping regions add
// independent changes rather than sequentially replacing one another.
public sealed record EcgInfarctionZones(EcgInfarctionRegion Ischemia, EcgInfarctionRegion Injury,
    EcgInfarctionRegion Necrosis, EcgInfarctionComponents Components, long RepolarizationDelayNs = 0)
{
    internal IReadOnlyList<ElectrodeWaveformPlan> Apply(IReadOnlyList<ElectrodeWaveformPlan> current, EcgCycleTiming timing, EcgUWavePlan? u)
    {
        if (Ischemia is null || Injury is null || Necrosis is null || Components is null || RepolarizationDelayNs < 0)
        { throw Invalid(); }
        Components.Validate();
        var baseline = TextbookElectrodeReference.CreateElectrodes(timing: timing);
        List<EventWaveformBand>[] additions = Enumerable.Range(0, 10).Select(_ => new List<EventWaveformBand>()).ToArray();
        var parts = new[]
        {
            (Ischemia, new EcgInfarctionComponents(TPeakMicrovolts: Components.TPeakMicrovolts), RepolarizationDelayNs),
            (Injury, new EcgInfarctionComponents(JMicrovolts: Components.JMicrovolts, StEndMicrovolts: Components.StEndMicrovolts, StArchMicrovolts: Components.StArchMicrovolts), 0L),
            (Necrosis, new EcgInfarctionComponents(Components.Necrosis, QrsTemplatePermille: Components.QrsTemplatePermille, ContributionLoss: Components.ContributionLoss), 0L),
        };
        for (int part = 0; part < parts.Length; part++)
        {
            var (region, component, delay) = parts[part];
            var plan = new EcgChestInfarctionPlan(region.ChestMask, InfarctionIllustrationStage.None, region.Territory, delay, component);
            var target = plan.Apply(baseline, timing); // Also validates inactive regions.
            if (part == 2 && component.ContributionLoss is { } loss)
            {
                // Append only the loss field. Re-projecting and subtracting a
                // reference QRS would leave tiny rounding residues outside it.
                loss.AddRegionalBands(additions, timing, plan);
                continue;
            }
            if (part == 0 && plan.HasActiveRegion && u is not null && u.ElectrodeAmplitudesMicrovolts.Any(v => v != 0) && delay > u.DelayAfterTNs)
            { throw new EventWaveformException("EcgInfarction.UOverlap", "zones"); }
            if ((part == 0 && component.TPeakMicrovolts is null && delay == 0) ||
                (part == 1 && component.JMicrovolts == 0 && component.StEndMicrovolts == 0 && component.StArchMicrovolts == 0) ||
                (part == 2 && component.ContributionLoss?.IsActive != true &&
                    (component.Necrosis == NecrosisIllustrationShape.Reference || component.QrsTemplatePermille == 0))) { continue; }
            bool Select(EventWaveformBand band) => band.Trigger == PhysiologyCycleEventKind.VentricularElectrical && (part switch
            {
                0 => band.DelayNs == timing.TOffsetFromQrsNs && (band.DurationNs == timing.TDurationNs || band.DurationNs == timing.TDurationNs + delay),
                1 => (band.DelayNs == timing.QrsDurationNs - timing.QrsDurationNs / 4 && band.DurationNs == timing.QtIntervalNs - band.DelayNs) ||
                    (band.DelayNs == timing.QrsDurationNs && band.DurationNs == timing.StDurationNs),
                _ => band.DelayNs == 0,
            });
            for (int i = 0; i < 10; i++)
            {
                additions[i].AddRange(target[i].Bands.Where(Select));
                additions[i].AddRange(baseline[i].Bands.Where(Select).Select(b => b with
                { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => -v).ToArray()) }));
            }
        }
        return Array.AsReadOnly(current.Select((electrode, i) => electrode with
        { Bands = Array.AsReadOnly(electrode.Bands.Concat(additions[i]).ToArray()) }).ToArray());
    }

    private static EventWaveformException Invalid() => new("EcgInfarction.InvalidZones", "zones");
}
