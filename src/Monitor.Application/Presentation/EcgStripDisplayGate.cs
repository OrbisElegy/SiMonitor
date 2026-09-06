// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record EcgStripDisplaySelection(string ReasonCode, ReconstructedEcgStrip? Strip);

// Trusted in-process reconstruction results only. Selects the whole strip;
// source freshness, history completeness and global safety remain external.
public static class EcgStripDisplayGate
{
    public static EcgStripDisplaySelection Select(SweepStateProjectionState current,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale,
        int gutterLeftPixels, int pulseLeftPixels, ReconstructedEcgStrip? strip)
    {
        ArgumentNullException.ThrowIfNull(verticalScale);
        EcgCalibrationGeometrySnapshot calibration = EcgCalibrationGeometry.Compose(current,
            plotLeftPixels, plotWidthPixels, verticalScale, gutterLeftPixels, pulseLeftPixels);
        SweepFrameDisplaySelection patient = SweepFrameDisplayGate.SelectFrame(current,
            plotLeftPixels, plotWidthPixels, verticalScale.PlotTopPixels, verticalScale.PlotHeightPixels,
            strip?.PatientFrame, strip?.Checkpoint.Source, verticalScale);
        if (patient.Frame is null) { return new(patient.ReasonCode, null); }

        EcgCalibrationGeometrySnapshot previous = strip!.Calibration;
        if (strip.Checkpoint.GutterLeftPixels != gutterLeftPixels || strip.Checkpoint.PulseLeftPixels != pulseLeftPixels ||
            previous.GroupId != calibration.GroupId || previous.SweepEpoch != calibration.SweepEpoch ||
            previous.PresentationClockRevision != calibration.PresentationClockRevision ||
            previous.GutterLeftPixels != calibration.GutterLeftPixels || previous.GutterRightPixels != calibration.GutterRightPixels ||
            !previous.Points.SequenceEqual(calibration.Points))
        {
            return new("EcgStripDisplay.CalibrationMismatch", null);
        }
        return new("EcgStripDisplay.Matched", strip);
    }
}
