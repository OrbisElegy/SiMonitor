// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum VascularPressureMorphologyKind { Arterial, PulmonaryArtery }

// Empirical pressure-shape modulation using independent authored ABP/PA
// contours. This is not a second flow input or a qualified full vascular model.
// Pulse height is in centi-mmHg at the nominal periodic reservoir state.
public sealed record VascularPressureMorphologyPlan(VascularPressureMorphologyKind Kind,
    long DurationNs, int PulseHeightCentiMmHg, string ModelId = "VascularPressureMorphologyRatio@4", int MaximumPulseOverlap = 1)
{
    public const string EvidenceId = "VascularPressureMorphologyRatio@4";
    public const string PreviousEvidenceId = "VascularPressureMorphologyRatio@3";
    public const string LegacyEvidenceId = "VascularPressureMorphologyRatio@2";
}
