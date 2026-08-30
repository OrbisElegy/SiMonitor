// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Common;

public sealed record DomainRejection(
    string ReasonCode,
    string ObjectRef,
    string FieldPath,
    ulong CurrentRevision,
    bool Retryable,
    string LocalizationKey,
    Guid CorrelationId);

public readonly record struct DomainResult<T>(T? Value, DomainRejection? Rejection)
{
    public bool IsAccepted => Rejection is null;
}

public static class DomainResult
{
    public static DomainResult<T> Accept<T>(T value) => new(value, null);

    public static DomainResult<T> Reject<T>(DomainRejection rejection) => new(default, rejection);
}
