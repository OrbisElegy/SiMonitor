// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Simulation.Authoring;

internal static class PhysiologyScalarCurves
{
    internal static void Apply(PhysiologyWaveformChannelPlan[] channels,
        PhysiologyIllustrationConfiguration configuration, RegularPhysiologyPlan physiology)
    {
        ApplyPressure(3, configuration.AbpSystolicCentiMmHgCurve, configuration.AbpDiastolicCentiMmHgCurve, true);
        ApplyPressure(5, configuration.PaSystolicCentiMmHgCurve, configuration.PaDiastolicCentiMmHgCurve, false);
        if (configuration.CvpBaselineCentiMmHgCurve is { } cvp)
        {
            var amplitude = new WaveformAmplitudeCurve(cvp, configuration.CvpBaselineCentiMmHg, true);
            amplitude.Validate(channels[6].Plane.ProfileId);
            channels[6] = channels[6] with { AmplitudeCurve = amplitude };
        }
        if (configuration.Co2EndExpiratoryCentiMmHgCurve is { } co2)
        {
            var amplitude = new WaveformAmplitudeCurve(co2,
                checked((configuration.ResolveCapnogram().EndExpiratoryMmHg - configuration.Co2BaselineMmHg) * 100), false,
                configuration.Co2BaselineMmHg * 100);
            amplitude.Validate(channels[4].Plane.ProfileId);
            channels[4] = channels[4] with { AmplitudeCurve = amplitude };
        }

        void ApplyPressure(int row, VitalValueCurve? systolic, VitalValueCurve? diastolic, bool arterial)
        {
            if (systolic is null && diastolic is null) { return; }
            if (systolic is null || diastolic is null) { throw new ArgumentException("Physiology.PressureCurvesRequirePair"); }
            var systolicRange = arterial ? VascularPressureTarget.ArterialSystolicRange : VascularPressureTarget.PulmonarySystolicRange;
            var diastolicRange = arterial ? VascularPressureTarget.ArterialDiastolicRange : VascularPressureTarget.PulmonaryDiastolicRange;
            if (systolic.MinimumValue < systolicRange.MinimumCentiMmHg || systolic.MaximumValue > systolicRange.MaximumCentiMmHg ||
                diastolic.MinimumValue < diastolicRange.MinimumCentiMmHg || diastolic.MaximumValue > diastolicRange.MaximumCentiMmHg)
            { throw new ArgumentException("Physiology.PressureTargetOutOfRange"); }
            var changes = systolic.Changes.Concat(diastolic.Changes).ToArray();
            var times = changes.SelectMany(change => new[] { change.StartSimTimeNs, checked(change.StartSimTimeNs + change.DurationNs) })
                .Append(0).Distinct();
            foreach (long time in times)
            { new VascularPressureTarget(systolic.At(time), diastolic.At(time)).Validate(arterial); }
            // As with fixed pressure targets, a stored target cannot create an
            // ejection or lift a stopped circulation's original runoff.
            if (physiology.CardiacActivity is CardiacActivity.Absent or CardiacActivity.AtrialOnly ||
                !physiology.VentricularMechanicalEnabled && physiology.MechanicalAfterCycles is null ||
                physiology.Pacing is PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure ||
                physiology.Pacing is not null && physiology.Pacing != PacingIllustration.VentricularUndersensing && physiology.PacingOutput?.CurrentMilliamps == 0)
            { return; }
            var pressure = channels[row].VascularPressure ?? throw new ArgumentException("Physiology.PressureTargetRequiresReservoirMorphology");
            channels[row] = (pressure with { TargetCurves = new(systolic, diastolic) })
                .CreateChannel(physiology, channels[row].Plane.ChannelId, channels[row].QualityFlags);
        }
    }
}
