// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepDisplaySnapshot(
    SweepStateProjectionSnapshot Presentation,
    SweepPlotGeometrySnapshot CurrentRegions,
    NoDataSafetyProjection LiveSafety,
    ConnectivityCriticalBannerProjection Connectivity,
    SweepFrameDisplaySelection SourceFrame);

// Current safety and trace-region policy never come from a cached worker frame.
// LiveSafety describes the live patient, including while history is pinned;
// Presentation.TransientReplayPolicy independently controls historical replay.
public static class SweepDisplayComposition
{
    public static SweepDisplaySnapshot Compose(SweepStateProjectionState state,
        long authorityMonotonicNs, IReadOnlyList<NumericNoDataPolicy> numericPolicies,
        int leftPixels, int widthPixels, int topPixels, int heightPixels,
        PublishedSweepFrame? published, EcgVerticalScale? verticalScale = null)
    {
        var presentation = SweepStateProjectionStateMachine.Restore(state);
        SweepStateProjectionState current = presentation.CaptureState();
        var safety = NoDataPresentationStateMachine.Start(
            numericPolicies, current.ContinuityState);
        NoDataSafetyProjection liveSafety = safety.Advance(authorityMonotonicNs);
        SweepFrameDisplaySelection frame = SweepFrameDisplayGate.Select(
            current, leftPixels, widthPixels, topPixels, heightPixels, published, verticalScale);

        return new(presentation.CaptureProjection(),
            SweepPlotGeometry.Compose(current, leftPixels, widthPixels),
            liveSafety, DataContinuityStateMachine.Restore(current.ContinuityState).CaptureBanner(), frame);
    }
}
