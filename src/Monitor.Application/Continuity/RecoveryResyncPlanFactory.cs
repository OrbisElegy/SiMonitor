// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Continuity;

public sealed class RecoveryResyncPlanFactoryException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record RecoveryHostPlanContext(
    Guid PlanId,
    ulong HostCheckpointSequence,
    ulong HostDurableCommitSequence,
    string HostStateSha256,
    long CurrentSimTimeNs,
    bool RequireFullSnapshot,
    string? ResumeSnapshotSha256,
    string ReasonCode);

public static class RecoveryResyncPlanFactory
{
    public static RecoveryResyncPlan Create(
        RecoveryPlanningInput planningInput,
        WaveformBlockRing hostWaveformRing,
        WaveformRecoveryRequest waveformRequest,
        RecoveryHostPlanContext context)
    {
        ArgumentNullException.ThrowIfNull(planningInput);
        ArgumentNullException.ThrowIfNull(hostWaveformRing);
        ArgumentNullException.ThrowIfNull(waveformRequest);
        ArgumentNullException.ThrowIfNull(context);
        ValidatePlanningInput(planningInput);
        ValidateContext(planningInput, context);
        ValidateWaveformContext(
            planningInput,
            hostWaveformRing,
            waveformRequest);
        WaveformRecoveryPlan waveformPlan = WaveformRecoveryPlanner.Plan(
            hostWaveformRing,
            waveformRequest);
        WaveformJoinBoundary boundary = ValidateWaveformPlan(
            waveformPlan,
            context.CurrentSimTimeNs);

        LocalPredictionDecision decision = context.RequireFullSnapshot
            ? LocalPredictionDecision.FullSnapshotRequired
            : planningInput.LocalPredictionVerified
                ? LocalPredictionDecision.CommitVerified
                : LocalPredictionDecision.Discard;

        return new RecoveryResyncPlan(
            context.PlanId,
            planningInput.HostAuthorityEpoch,
            context.HostCheckpointSequence,
            context.HostStateSha256,
            boundary.SimTimeNs,
            hostWaveformRing.StreamEpoch,
            boundary.PrerollFromSimTimeNs,
            boundary.SimTimeNs,
            decision,
            context.ReasonCode,
            planningInput.AcceptedThroughCommitSequence,
            context.ResumeSnapshotSha256);
    }

    private static void ValidatePlanningInput(RecoveryPlanningInput input)
    {
        LocalContinuationReportMessage? report = input.LocalContinuationReport;
        if (input.ClientId == Guid.Empty ||
            input.SessionId == Guid.Empty ||
            input.InstanceId == Guid.Empty ||
            input.HostAuthorityEpoch <= input.ObservedAuthorityEpoch ||
            input.AcceptedThroughCommitSequence < input.LastAppliedCommitSequence ||
            input.LocalPredictionVerified != (report is not null) ||
            (report is null && input.AcceptedThroughCommitSequence !=
                input.LastAppliedCommitSequence) ||
            (report is not null &&
                (report.ClientId != input.ClientId ||
                    report.SessionId != input.SessionId ||
                    report.InstanceId != input.InstanceId)))
        {
            throw Error(
                "RecoveryPlan.InvalidPlanningInput",
                nameof(input));
        }
    }

    private static void ValidateContext(
        RecoveryPlanningInput input,
        RecoveryHostPlanContext context)
    {
        if (context.PlanId == Guid.Empty ||
            context.CurrentSimTimeNs < 0 ||
            context.HostDurableCommitSequence <
                input.AcceptedThroughCommitSequence ||
            !IsSha256(context.HostStateSha256) ||
            !IsReasonCode(context.ReasonCode) ||
            (context.ResumeSnapshotSha256 is not null &&
                !IsSha256(context.ResumeSnapshotSha256)) ||
            (context.RequireFullSnapshot &&
                context.ResumeSnapshotSha256 is null))
        {
            throw Error("RecoveryPlan.InvalidHostContext", nameof(context));
        }
    }

    private static void ValidateWaveformContext(
        RecoveryPlanningInput input,
        WaveformBlockRing hostRing,
        WaveformRecoveryRequest request)
    {
        if (hostRing.SessionId != input.SessionId ||
            hostRing.InstanceId != input.InstanceId ||
            request.SessionId != input.SessionId ||
            request.InstanceId != input.InstanceId ||
            hostRing.StreamEpoch <= request.StreamEpoch)
        {
            throw Error(
                "RecoveryPlan.InvalidWaveformContext",
                nameof(hostRing));
        }
    }

    private static WaveformJoinBoundary ValidateWaveformPlan(
        WaveformRecoveryPlan plan,
        long currentSimTimeNs)
    {
        WaveformJoinBoundary? boundary = plan.JoinBoundary;
        if (plan.Mode != WaveformRecoveryMode.SnapshotThenJoin ||
            !Enum.IsDefined(plan.Reason) ||
            plan.Reason == WaveformRecoveryReason.None ||
            plan.ReplayBlocks is null ||
            plan.ReplayBlocks.Count != 0 ||
            boundary is null ||
            boundary.SimTimeNs < 0 ||
            boundary.PrerollFromSimTimeNs < 0 ||
            boundary.PrerollFromSimTimeNs > boundary.SimTimeNs ||
            boundary.RequiredRenderHistoryNs >
                boundary.AvailableRenderHistoryNs ||
            boundary.AvailableRenderHistoryNs >
                WaveformRecoveryPlanner.MaximumRequiredRenderHistoryNs ||
            boundary.RequiredRenderHistoryNs %
                WaveformBlockAssembler.BlockDurationNs != 0 ||
            boundary.AvailableRenderHistoryNs %
                WaveformBlockAssembler.BlockDurationNs != 0 ||
            (Int128)boundary.PrerollFromSimTimeNs +
                boundary.RequiredRenderHistoryNs != boundary.SimTimeNs ||
            !IsAligned(boundary.SimTimeNs) ||
            !IsAligned(boundary.PrerollFromSimTimeNs))
        {
            throw Error(
                "RecoveryPlan.InvalidWaveformPlan",
                nameof(plan));
        }

        Int128 minimumBoundary = (Int128)currentSimTimeNs +
            WaveformRecoveryPlanner.MinimumJoinLeadNs;
        if (boundary.SimTimeNs < minimumBoundary)
        {
            throw Error(
                "RecoveryPlan.InvalidWaveformPlan",
                nameof(plan));
        }

        return boundary;
    }

    private static bool IsAligned(long simTimeNs) =>
        simTimeNs % WaveformBlockAssembler.BlockDurationNs == 0;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsReasonCode(string? value)
    {
        if (value is not { Length: >= 2 and <= 64 } ||
            value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'A' and <= 'Z') &&
                character is not (>= '0' and <= '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static RecoveryResyncPlanFactoryException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
