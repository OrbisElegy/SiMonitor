// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordNavigationState(CapturedRecordBindingState Record,
    long PageDurationNs, ulong PageIndex);

// Serialized commands; constructor/restore receive the trusted initial page.
public sealed class CapturedRecordNavigation
{
    private readonly CapturedRecordBinding _record;
    private readonly long _pageDurationNs;
    private SystemViewCommandAssessmentPolicy _policy;

    public CapturedRecordNavigation(CapturedRecordBinding record, long pageDurationNs,
        ulong initialPageIndex, SystemViewCommandAssessmentPolicy policy)
    {
        UpdatePolicy(policy);
        CurrentPage = CapturedRecordPagination.Resolve(record, pageDurationNs, initialPageIndex);
        _record = record;
        _pageDurationNs = pageDurationNs;
    }

    public CapturedRecordPage CurrentPage { get; private set; }
    public SystemViewCommandAssessmentPolicy CurrentPolicy => _policy;

    internal bool IsBoundTo(CapturedRecordBinding record) => ReferenceEquals(_record, record);

    public CapturedRecordStudyView CreateStudyView(Ecg12RecordContext context, string slotId,
        SystemViewCommandAssessmentPolicy measurementPolicy) => new(_record, context, slotId, measurementPolicy);

    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        { throw new CapturedRecordPaginationException("RecordPagination.InvalidPolicy", nameof(policy)); }
        _policy = policy;
    }

    public CapturedRecordPage SelectPage(ulong pageIndex)
    {
        EnsureEnabled();
        CapturedRecordPage candidate = CapturedRecordPagination.Resolve(_record, _pageDurationNs, pageIndex);
        return CurrentPage = candidate;
    }

    public CapturedRecordPage NextPage()
    {
        EnsureEnabled();
        if (CurrentPage.PageIndex == CurrentPage.PageCount - 1)
        { throw new CapturedRecordPaginationException("RecordPagination.NoNextPage", nameof(CurrentPage)); }
        return SelectPage(CurrentPage.PageIndex + 1);
    }

    public CapturedRecordPage PreviousPage()
    {
        EnsureEnabled();
        if (CurrentPage.PageIndex == 0)
        { throw new CapturedRecordPaginationException("RecordPagination.NoPreviousPage", nameof(CurrentPage)); }
        return SelectPage(CurrentPage.PageIndex - 1);
    }

    public CapturedRecordNavigationState CaptureState() => new(_record.CaptureState(), _pageDurationNs, CurrentPage.PageIndex);

    public static CapturedRecordNavigation Restore(CapturedRecordNavigationState state,
        SystemViewCommandAssessmentPolicy currentPolicy)
    {
        if (!Enum.IsDefined(currentPolicy))
        { throw new CapturedRecordPaginationException("RecordPagination.InvalidPolicy", nameof(currentPolicy)); }
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(CapturedRecordBinding.Restore(state.Record), state.PageDurationNs, state.PageIndex, currentPolicy);
        }
        catch (ArgumentException)
        { throw new CapturedRecordPaginationException("RecordPagination.InvalidCheckpoint", nameof(state)); }
    }

    private void EnsureEnabled()
    {
        if (_policy != SystemViewCommandAssessmentPolicy.Enabled)
        {
            throw new CapturedRecordPaginationException(_policy == SystemViewCommandAssessmentPolicy.CourseLocked
                ? "RecordPagination.CourseLocked" : "RecordPagination.Disabled", "policy");
        }
    }
}
