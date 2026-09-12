// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record CapturedRecordPage(ulong PageIndex, ulong PageCount,
    long StartDataTimeNs, long EndExclusiveDataTimeNs);

public sealed class CapturedRecordPaginationException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// A query over immutable record evidence, not a navigation command or print layout.
public static class CapturedRecordPagination
{
    public static CapturedRecordPage Resolve(CapturedRecordBinding record, long pageDurationNs, ulong pageIndex)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (pageDurationNs <= 0)
        { throw new CapturedRecordPaginationException("RecordPagination.InvalidPageDuration", nameof(pageDurationNs)); }
        var range = record.CapturePinnedRecordRange();
        long duration = range.EndExclusiveDataSimTimeNs - range.StartDataSimTimeNs;
        ulong count = (ulong)(duration / pageDurationNs) + (duration % pageDurationNs == 0 ? 0UL : 1UL);
        if (pageIndex >= count)
        { throw new CapturedRecordPaginationException("RecordPagination.PageOutsideRecord", nameof(pageIndex)); }
        // The validated index bounds the product by the record duration.
        long start = checked(range.StartDataSimTimeNs + (long)pageIndex * pageDurationNs);
        long length = Math.Min(pageDurationNs, range.EndExclusiveDataSimTimeNs - start);
        return new(pageIndex, count, start, start + length);
    }
}
