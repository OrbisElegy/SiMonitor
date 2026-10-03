// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class NoDataSweepSpecifications
{
    private const ulong Second = 1_000_000_000;

    public static Specification[] All =>
    [
        new(nameof(SweepOnsetAndProgressPreserveUnpassedTrace),
            SweepOnsetAndProgressPreserveUnpassedTrace),
        new(nameof(WrappedCoverageReplacesTraceAtExactDuration),
            WrappedCoverageReplacesTraceAtExactDuration),
        new(nameof(FrameChunkingAndLateJoinProduceTheSameProjection),
            FrameChunkingAndLateJoinProduceTheSameProjection),
        new(nameof(CheckpointRestorePreservesFutureSweepCoverage),
            CheckpointRestorePreservesFutureSweepCoverage),
        new(nameof(InvalidPlanEpisodeAndTimeFailWithoutMutation),
            InvalidPlanEpisodeAndTimeFailWithoutMutation),
    ];

    private static void SweepOnsetAndProgressPreserveUnpassedTrace()
    {
        NoDataSweepStateMachine sweep = Start(startedAtPresentationNs: 3 * (long)Second);

        NoDataSweepCoverage coverage = sweep.CaptureCoverage();

        Check.That(
            coverage.GroupId == "monitor.ecg" &&
            coverage.SweepEpoch == 7 &&
            coverage.PresentationClockRevision == 11 &&
            coverage.CycleIndex == 0 &&
            coverage.WriteHeadOffsetNs == 3 * Second &&
            coverage.WriteHeadPhasePpm == 375_000 &&
            coverage.CoveredDurationNs == 0 &&
            coverage.RemainingPatientTraceDurationNs == 8 * Second &&
            !coverage.FullyCovered &&
            coverage.CoveredIntervals.Count == 0,
            "NoData must begin at the shared phase without clearing old trace");

        coverage = sweep.Advance(5 * (long)Second);

        Check.That(
            coverage.WriteHeadOffsetNs == 5 * Second &&
            coverage.WriteHeadPhasePpm == 625_000 &&
            coverage.CoveredDurationNs == 2 * Second &&
            coverage.RemainingPatientTraceDurationNs == 6 * Second &&
            IntervalsEqual(coverage,
                new SweepCoverageInterval(3 * Second, 5 * Second)),
            "only the interval passed by the write head may become NoData");
    }

    private static void WrappedCoverageReplacesTraceAtExactDuration()
    {
        NoDataSweepStateMachine sweep = Start(startedAtPresentationNs: 7 * (long)Second);

        NoDataSweepCoverage coverage = sweep.Advance(9 * (long)Second);

        Check.That(
            coverage.CycleIndex == 1 &&
            coverage.WriteHeadOffsetNs == Second &&
            coverage.WriteHeadPhasePpm == 125_000 &&
            IntervalsEqual(
                coverage,
                new SweepCoverageInterval(0, Second),
                new SweepCoverageInterval(7 * Second, 8 * Second)),
            "wrapped coverage must be two canonical intervals, never one cross-edge line");

        NoDataSweepCoverage before = sweep.Advance(14_999_999_999);
        NoDataSweepCoverage complete = sweep.Advance(15 * (long)Second);

        Check.That(
            before.RemainingPatientTraceDurationNs == 1 &&
            !before.FullyCovered &&
            complete.CoveredDurationNs == 8 * Second &&
            complete.RemainingPatientTraceDurationNs == 0 &&
            complete.FullyCovered &&
            IntervalsEqual(
                complete,
                new SweepCoverageInterval(0, 8 * Second)),
            "exactly one visible duration must replace every old Live point");
    }

    private static void FrameChunkingAndLateJoinProduceTheSameProjection()
    {
        NoDataSweepStateMachine incremental = Start(
            startedAtPresentationNs: 7 * (long)Second);
        _ = incremental.Advance(7_100_000_000);
        _ = incremental.Advance(8_750_000_000);
        NoDataSweepCoverage chunked = incremental.Advance(10_250_000_000);

        NoDataSweepStateMachine direct = Start(
            startedAtPresentationNs: 7 * (long)Second);
        NoDataSweepCoverage lateJoin = direct.Advance(10_250_000_000);

        Check.That(
            CoverageEqual(chunked, lateJoin),
            "coverage must derive from presentation time, not rendered frame count");
    }

    private static void CheckpointRestorePreservesFutureSweepCoverage()
    {
        NoDataSweepStateMachine original = Start(
            startedAtPresentationNs: 7 * (long)Second);
        _ = original.Advance(9 * (long)Second);
        NoDataSweepState checkpoint = original.CaptureState();

        var restored = NoDataSweepStateMachine.Restore(
            NoData(noDataSince: 20),
            checkpoint);
        NoDataSweepCoverage expected = original.Advance(12_345_678_901);
        NoDataSweepCoverage actual = restored.Advance(12_345_678_901);

        Check.That(
            restored.CaptureState() == original.CaptureState() &&
            CoverageEqual(expected, actual),
            "restored sweep state must retain its phase and future coverage");
    }

    private static void InvalidPlanEpisodeAndTimeFailWithoutMutation()
    {
        NoDataSweepPlan shortHistory = Plan() with
        {
            RequiredRenderHistoryNs = 8 * Second,
        };
        Check.That(
            Reason(() => NoDataSweepStateMachine.Start(
                shortHistory,
                NoData(noDataSince: 20),
                0)) == "NoDataSweep.InvalidPlan",
            "render history must include the fixed window and erase gap");
        Check.That(
            Reason(() => NoDataSweepStateMachine.Start(
                Plan(),
                Connected(),
                0)) == "NoDataSweep.NotNoData",
            "NoDataSweep cannot start while authoritative data is available");

        NoDataSweepStateMachine sweep = Start(startedAtPresentationNs: 7);
        NoDataSweepState before = sweep.CaptureState();
        Check.That(
            Reason(() => sweep.Advance(6)) == "NoDataSweep.TimeReversed" &&
            sweep.CaptureState() == before,
            "a reversed presentation clock must reject without mutation");
        Check.That(
            Reason(() => NoDataSweepStateMachine.Restore(
                NoData(noDataSince: 21),
                before)) == "NoDataSweep.EpisodeMismatch",
            "a checkpoint from an earlier NoData episode cannot be reused");
    }

    private static NoDataSweepStateMachine Start(long startedAtPresentationNs) =>
        NoDataSweepStateMachine.Start(
            Plan(),
            NoData(noDataSince: 20),
            startedAtPresentationNs);

    private static NoDataSweepPlan Plan() => new(
        "monitor.ecg",
        SweepEpoch: 7,
        PresentationClockRevision: 11,
        CycleOriginPresentationNs: 0,
        VisibleDurationNs: 8 * Second,
        EraseGapNs: Second / 10,
        RequiredRenderHistoryNs: 9 * Second);

    private static DataContinuityState NoData(long noDataSince)
    {
        var continuity = DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);
        return continuity.Disconnect(
            hasBufferedData: false,
            authorityMonotonicNs: noDataSince);
    }

    private static DataContinuityState Connected() =>
        DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0).CaptureState();

    private static bool IntervalsEqual(
        NoDataSweepCoverage coverage,
        params SweepCoverageInterval[] expected) =>
        coverage.CoveredIntervals.SequenceEqual(expected);

    private static bool CoverageEqual(
        NoDataSweepCoverage left,
        NoDataSweepCoverage right)
    {
        IReadOnlyList<SweepCoverageInterval> ignoredIntervals =
            Array.Empty<SweepCoverageInterval>();
        return left with { CoveredIntervals = ignoredIntervals } ==
                right with { CoveredIntervals = ignoredIntervals } &&
            left.CoveredIntervals.SequenceEqual(right.CoveredIntervals);
    }

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (NoDataSweepException exception)
        {
            return exception.ReasonCode;
        }
    }
}
