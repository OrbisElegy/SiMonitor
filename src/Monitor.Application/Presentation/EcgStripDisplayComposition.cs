// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record EcgStripDisplaySnapshot(
    SweepStateProjectionSnapshot Presentation,
    SweepPlotGeometrySnapshot CurrentRegions,
    EcgCalibrationGeometrySnapshot CurrentCalibration,
    NoDataSafetyProjection LiveSafety,
    ConnectivityCriticalBannerProjection Connectivity,
    EcgStripDisplaySelection SourceStrip);

// Current persistent scale and safety are independent of background geometry.
// A missing source strip is not an instruction to clear retained history.
public static class EcgStripDisplayComposition
{
    public static EcgStripDisplaySnapshot Compose(SweepStateProjectionState state,
        long authorityMonotonicNs, IReadOnlyList<NumericNoDataPolicy> numericPolicies,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale,
        int gutterLeftPixels, int pulseLeftPixels, PublishedEcgStrip? published,
        bool requireColumnReduction = false)
    {
        ArgumentNullException.ThrowIfNull(verticalScale);
        SweepStateProjectionState current = SweepStateProjectionStateMachine.Restore(state).CaptureState();
        SweepDisplaySnapshot safety = SweepDisplayComposition.Compose(current, authorityMonotonicNs, numericPolicies,
            plotLeftPixels, plotWidthPixels, verticalScale.PlotTopPixels, verticalScale.PlotHeightPixels, null, verticalScale);
        EcgCalibrationGeometrySnapshot calibration = EcgCalibrationGeometry.Compose(current,
            plotLeftPixels, plotWidthPixels, verticalScale, gutterLeftPixels, pulseLeftPixels);
        EcgStripDisplaySelection strip = EcgStripDisplayGate.Select(current,
            plotLeftPixels, plotWidthPixels, verticalScale, gutterLeftPixels, pulseLeftPixels, published?.Strip, requireColumnReduction);
        return new(safety.Presentation, safety.CurrentRegions, calibration,
            safety.LiveSafety, safety.Connectivity, strip);
    }
}
