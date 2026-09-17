// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Established escape rhythm, not onset after an observed pause. No atrial
// impulse conducts or resets the ventricular oscillator in this illustration.
public static class CompleteAvBlockJunctionalReference
{
    public const string EvidenceId = "CompleteAvBlockJunctionalIllustrationDraft@1";
    public static RegularPhysiologyPlan CreatePlan(long atrialPeriodNs = 800_000_000,
        long escapePeriodNs = 1_200_000_000, long firstQrsNs = 400_000_000)
    {
        if (firstQrsNs < 0 || firstQrsNs > long.MaxValue - 80_000_000)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidState", nameof(firstQrsNs)); }
        RegularPhysiologyPlan plan = new(0, atrialPeriodNs, firstQrsNs, 80_000_000,
            firstQrsNs + 80_000_000, 3_750_000_000, 1_875_000_000,
            IndependentVentricularPeriodNs: escapePeriodNs,
            ConductionPattern: AvConductionPattern.CompleteAvBlockJunctionalIllustration);
        _ = RegularPhysiologyTimeline.Start(plan);
        return plan;
    }

    // Normal conducted-activation shape is reused for junctional escape;
    // timing independence, rather than a wide ectopic QRS, defines this case.
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        TextbookElectrodeReference.CreateElectrodes();
}
