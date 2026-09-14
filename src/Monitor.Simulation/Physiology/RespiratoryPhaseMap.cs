// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// InspirationDurationNs includes the optional end-inspiratory pause. Holding
// table phase keeps the respiratory excursion level until expiration starts.
internal static class RespiratoryPhaseMap
{
    internal static IReadOnlyList<EventWaveformPhasePoint> Create(RegularPhysiologyPlan plan, int tableLength)
    {
        List<EventWaveformPhasePoint> points = [new(0, 0)];
        if (plan.InspiratoryPauseNs > 0)
        { points.Add(new(plan.InspirationDurationNs - plan.InspiratoryPauseNs, tableLength / 2)); }
        points.Add(new(plan.InspirationDurationNs, tableLength / 2));
        points.Add(new(plan.BreathPeriodNs, tableLength));
        return points.AsReadOnly();
    }
}
