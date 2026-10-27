// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Absolute source-time targets. The source prepares endpoint reservoirs once;
// acquisition samples interpolate the same mechanical-event reconstruction.
public sealed record VascularPressureTargetCurves(VitalValueCurve SystolicCentiMmHg,
    VitalValueCurve DiastolicCentiMmHg);
