// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Loading a named example resets incompatible rhythm/shape fields atomically.
internal static class SecondDegreeBlockPreset
{
    internal static int FromSelection(int selection) => selection switch
    { 6 => 0, 16 => 1, 17 => 2, 18 => 3, 19 => 4, 1 => 5, 20 => 6, 21 => 7, _ => -1 };
    internal static bool RequiresReload(Monitor.Simulation.Physiology.AvConductionPattern pattern) => pattern is
        Monitor.Simulation.Physiology.AvConductionPattern.PrematureVentricularIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularBigeminyIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularTrigeminyIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PolymorphicPvcIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.MultifocalPvcIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.InterpolatedPvcIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularCoupletIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PolymorphicVentricularCoupletIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalAfterQrsIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalOverlappingIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.AberrantPrematureAtrialIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.BlockedPrematureAtrialIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.PrematureAtrialIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.CompleteAvBlockJunctionalIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.CompleteAvBlockVentricularIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.AtrialFlutterIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.AtrialFibrillationCoarseIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.AtrialFibrillationFineIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularFlutterIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularFibrillationCoarseIllustration or
        Monitor.Simulation.Physiology.AvConductionPattern.VentricularFibrillationFineIllustration;
    internal static int Selection(int index) => index switch
    {
        0 => 6,
        1 => 16,
        2 => 17,
        3 => 18,
        4 => 19,
        5 => 1,
        6 => 20,
        7 => 21,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
    internal static ProjectedEcgDemoConfiguration Ecg(int index)
    {
        int selection = Selection(index);
        var (atrial, conducted) = ConductionSelection.Resolve(selection);
        return ProjectedEcgDemoConfiguration.Default with
        { QrsDurationMilliseconds = index == 7 ? 160 : index == 6 ? 140 : 80, VentricularConductionRatio = atrial, ConductedBeatsPerGroup = conducted, ConductionPattern = ConductionSelection.Pattern(selection) };
    }
    internal static PhysiologyDemoConfiguration Physiology(int index)
    {
        var ecg = Ecg(index);
        return PhysiologyDemoConfiguration.Default with
        { VentricularConductionRatio = ecg.VentricularConductionRatio, ConductedBeatsPerGroup = ecg.ConductedBeatsPerGroup, ConductionPattern = ecg.ConductionPattern };
    }
}
