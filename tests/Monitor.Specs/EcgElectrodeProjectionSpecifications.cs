// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgElectrodeProjectionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(ElectrodesProduceTwelveStandardLeads), ElectrodesProduceTwelveStandardLeads),
        new(nameof(ProjectionRejectsCommonModeAndIgnoresDriveElectrode), ProjectionRejectsCommonModeAndIgnoresDriveElectrode),
        new(nameof(ProjectionPreservesFractionsBeforeExplicitQuantization), ProjectionPreservesFractionsBeforeExplicitQuantization),
        new(nameof(SharedElectrodeEventsRestoreAtEverySample), SharedElectrodeEventsRestoreAtEverySample),
        new(nameof(ElectrodeStateRejectsInvalidTopologyAndCancellation), ElectrodeStateRejectsInvalidTopologyAndCancellation),
    ];

    private static void Identities(EcgLeadProjection result)
    {
        Int128 i = result[EcgLead.I].Numerator, ii = result[EcgLead.II].Numerator;
        Check.That(result[EcgLead.III].Numerator == ii - i &&
            2 * result[EcgLead.AVR].Numerator == -i - ii &&
            2 * result[EcgLead.AVL].Numerator == 2 * i - ii &&
            2 * result[EcgLead.AVF].Numerator == 2 * ii - i,
            "Einthoven and Goldberger must hold exactly before acquisition quantization");
    }

    private static void ElectrodesProduceTwelveStandardLeads()
    {
        var result = EcgLeadProjection.Project(new(10 * Q, 40 * Q, 123 * Q, 100 * Q,
            51 * Q, 52 * Q, 53 * Q, 54 * Q, 55 * Q, 56 * Q));
        long[] expected = [30, 90, 60, -60, -15, 75, 1, 2, 3, 4, 5, 6];
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        { Check.That(result[lead].Numerator == expected[(int)lead] * Q * 6, "electrode topology must produce the known lead values"); }
        Check.That(result.WilsonCentralTerminal.ToQ32() == 50 * Q, "Wilson terminal uses three measuring limb electrodes");
        Identities(result);
    }

    private static void ProjectionRejectsCommonModeAndIgnoresDriveElectrode()
    {
        var original = EcgLeadProjection.Project(new(1, -7, 19, 13, 5, 6, 7, 8, 9, 10));
        var shifted = EcgLeadProjection.Project(new(101, 93, long.MinValue, 113, 105, 106, 107, 108, 109, 110));
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        { Check.That(original[lead] == shifted[lead], "common mode and RL changes must not change measured leads"); }
        var chestChanged = EcgLeadProjection.Project(new(1, -7, 19, 13, 5, 6, 700, 8, 9, 10));
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        { Check.That((original[lead] == chestChanged[lead]) == (lead != EcgLead.V3), "one chest electrode affects only its own chest lead"); }
    }

    private static void ProjectionPreservesFractionsBeforeExplicitQuantization()
    {
        var result = EcgLeadProjection.Project(new(0, 1, 0, 0, 0, 0, 0, 0, 0, 0));
        Check.That(result[EcgLead.AVR].Numerator == -3 && result[EcgLead.V1].Numerator == -2,
            "half and third Q32 units must survive projection without intermediate rounding");
        Identities(result);
        Check.That(new ExactEcgPotential(3).ToQ32() == 0 && new ExactEcgPotential(-3).ToQ32() == 0 &&
            new ExactEcgPotential(9).ToQ32() == 2 && new ExactEcgPotential(-9).ToQ32() == -2,
            "explicit Q32 conversion uses signed nearest ties to even");
        var extreme = EcgLeadProjection.Project(new(long.MinValue, long.MaxValue, 0, long.MaxValue, 0, 0, 0, 0, 0, 0));
        Identities(extreme);
        bool rejected = false;
        try { _ = extreme[EcgLead.I].ToQ32(); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EcgProjection.AmplitudeOverflow"; }
        Check.That(rejected, "unrepresentable acquisition value must reject rather than wrap or saturate");
    }

    private static ElectrodeWaveformState State()
    {
        var timeline = RegularPhysiologyTimeline.Start(new(0, 800_000_000, 160_000_000,
            80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000));
        return new(Enum.GetValues<EcgElectrode>().Select((electrode, index) =>
            new ElectrodeWaveformPlan(electrode, new EventWaveformBand[]
            {
                new(PhysiologyCycleEventKind.VentricularElectrical, index * 1_000_000, 80_000_000,
                    new long[] { 0, (index + 1) * Q, -index * Q, 0 }),
            })).ToArray(), timeline.AdvanceBefore(1_600_000_000, 100));
    }

    private static void SharedElectrodeEventsRestoreAtEverySample()
    {
        var state = State();
        var source = ElectrodeWaveformComposition.Restore(state);
        var restored = ElectrodeWaveformComposition.Restore(source.CaptureState());
        ((long[])state.Electrodes[0].Bands[0].TableQ32)[1] = 999 * Q;
        for (long time = 0; time < 1_600_000_000; time += 4_000_000)
        {
            var sample = source.EvaluateAt(time);
            var replay = restored.EvaluateAt(time);
            Check.That(sample.SimTimeNs == time, "all leads publish together at the caller's one sample time");
            Identities(sample.Leads);
            foreach (EcgLead lead in Enum.GetValues<EcgLead>())
            { Check.That(sample.Leads[lead] == replay.Leads[lead], "owned electrode bands must survive caller mutation and restore"); }
        }
    }

    private static void ElectrodeStateRejectsInvalidTopologyAndCancellation()
    {
        var state = State();
        foreach (var electrodes in new[] { state.Electrodes.Take(9).ToArray(), state.Electrodes.Reverse().ToArray(),
            Enumerable.Repeat(state.Electrodes[0], 10).ToArray() })
        {
            bool rejected = false;
            try { _ = ElectrodeWaveformComposition.Restore(state with { Electrodes = electrodes }); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EcgProjection.InvalidElectrodes"; }
            Check.That(rejected, "missing, duplicate or noncanonical electrode topology must reject");
        }
        var source = ElectrodeWaveformComposition.Restore(state);
        var before = source.EvaluateAt(180_000_000);
        bool cancelled = false;
        try { _ = source.EvaluateAt(180_000_000, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && source.EvaluateAt(180_000_000).Leads[EcgLead.II] == before.Leads[EcgLead.II],
            "cancelled evaluation must not publish a partial set or change future evaluation");
    }
}
