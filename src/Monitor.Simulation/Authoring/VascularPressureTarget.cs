// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Authoring;

// Teaching pressure targets for a reservoir channel with pulse morphology. The source
// solves R*Q so that the steady state at the nominal ventricular period starts each
// beat at the diastolic target, and uses the systolic-diastolic difference as the
// pulse height. Measured values still come from the generated samples.
public sealed record VascularPressureTarget(int SystolicCentiMmHg, int DiastolicCentiMmHg)
{
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) ArterialSystolicRange => (5000, 25000);
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) ArterialDiastolicRange => (2000, 15000);
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PulmonarySystolicRange => (1000, 10000);
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PulmonaryDiastolicRange => (600, 6000);
    public const int MinimumPulseCentiMmHg = 500;

    internal void Validate(bool arterial)
    {
        var (systolicMinimum, systolicMaximum) = arterial ? ArterialSystolicRange : PulmonarySystolicRange;
        var (diastolicMinimum, diastolicMaximum) = arterial ? ArterialDiastolicRange : PulmonaryDiastolicRange;
        if (SystolicCentiMmHg < systolicMinimum || SystolicCentiMmHg > systolicMaximum ||
            DiastolicCentiMmHg < diastolicMinimum || DiastolicCentiMmHg > diastolicMaximum)
        { throw new ArgumentException("Physiology.PressureTargetOutOfRange"); }
        if (SystolicCentiMmHg - DiastolicCentiMmHg < MinimumPulseCentiMmHg)
        { throw new ArgumentException("Physiology.PressureTargetPulseTooSmall"); }
    }
}
