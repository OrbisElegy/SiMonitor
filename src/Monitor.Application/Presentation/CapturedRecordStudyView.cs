// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordStudyDisplay(Ecg12ViewAdmissionDecision Admission,
    CapturedRecordBinding? Record, RecordMeasurementDisplay? Measurement, RecordSlotBinding? MeasurementSlot);

public sealed record CapturedRecordPageDisplay(CapturedRecordStudyDisplay Study,
    CapturedRecordNavigationDisplay? Navigation, RecordCursorViewport? Viewport)
{
    public CapturedRecordPage? Page => Navigation?.Page;
}

// Serialized composition. The shell must replace the old display with this result.
public sealed class CapturedRecordStudyView
{
    private readonly CapturedRecordBinding _record;
    private readonly Ecg12RecordContext _context;

    public CapturedRecordStudyView(CapturedRecordBinding record, Ecg12RecordContext context,
        string slotId, SystemViewCommandAssessmentPolicy measurementPolicy)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = Ecg12ViewAdmission.Evaluate(context, TemporalViewMode.CapturedRecord, true);
        _record = record;
        _context = context;
        Measurement = new(record, slotId, measurementPolicy);
    }

    public CapturedRecordMeasurement Measurement { get; private set; }

    public CapturedRecordPage ResolvePage(long pageDurationNs, ulong pageIndex) =>
        CapturedRecordPagination.Resolve(_record, pageDurationNs, pageIndex);

    public CapturedRecordNavigation CreateNavigation(long pageDurationNs, ulong initialPageIndex,
        SystemViewCommandAssessmentPolicy paginationPolicy) => new(_record, pageDurationNs, initialPageIndex, paginationPolicy);

    public void SelectMeasurementSlot(string slotId)
    {
        if (string.Equals(Measurement.Slot.SlotId, slotId, StringComparison.Ordinal)) { return; }
        CapturedRecordMeasurement replacement = new(_record, slotId, Measurement.CurrentPolicy);
        Measurement = replacement;
    }

    public CapturedRecordCursorPair PlacePairOnCurrentPage(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        ExactPlotCoordinate firstX, ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        return Measurement.ReplacePairFromPoints(firstX, firstY, secondX, secondY, viewport, scale);
    }

    public CapturedRecordCursorPair MoveCursorOnCurrentPage(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        RecordCursorEnd end, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        return Measurement.MoveCursor(end, x, y, viewport, scale);
    }

    public CapturedRecordStudyDrag BeginCursorDrag(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordCursorEnd end) =>
        new(this, navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, end);

    internal RecordCursorViewport RequireCurrentViewport(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale)
    {
        CapturedRecordPageDisplay display = CapturePageDisplay(navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale, false);
        if (!display.Study.Admission.MayEnter)
        { throw new Ecg12ViewAdmissionException(display.Study.Admission.ReasonCode, nameof(canPreserveGlobalSafetyOverlay)); }
        return display.Viewport!;
    }

    public CapturedRecordPageDisplay CapturePageDisplay(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, bool allowAuxiliaryRate)
    {
        if (navigation is null || !navigation.IsBoundTo(_record))
        { throw new CapturedRecordPaginationException("RecordPagination.ForeignNavigation", nameof(navigation)); }
        Ecg12ViewAdmissionDecision admission = Ecg12ViewAdmission.Evaluate(_context,
            TemporalViewMode.CapturedRecord, canPreserveGlobalSafetyOverlay);
        if (!admission.MayEnter) { return new(new(admission, null, null, null), null, null); }
        CapturedRecordNavigationDisplay commands = navigation.CaptureDisplay();
        CapturedRecordPage page = commands.Page;
        _ = SweepPlotGeometry.MapSampleOffset(0, (ulong)(page.EndExclusiveDataTimeNs - page.StartDataTimeNs), plotLeftPixels, plotWidthPixels);
        _ = EcgVerticalGeometry.MapMicrovolts(scale, 0, 1);
        RecordCursorViewport viewport = new(page.StartDataTimeNs, page.EndExclusiveDataTimeNs, plotLeftPixels, plotWidthPixels);
        return new(CaptureDisplay(canPreserveGlobalSafetyOverlay, viewport, scale, allowAuxiliaryRate), commands, viewport);
    }

    public CapturedRecordStudyDisplay CaptureDisplay(bool canPreserveGlobalSafetyOverlay,
        RecordCursorViewport viewport, EcgVerticalScale scale, bool allowAuxiliaryRate)
    {
        Ecg12ViewAdmissionDecision admission = Ecg12ViewAdmission.Evaluate(_context,
            TemporalViewMode.CapturedRecord, canPreserveGlobalSafetyOverlay);
        if (!admission.MayEnter) { return new(admission, null, null, null); }
        RecordMeasurementDisplay measurement = Measurement.CaptureDisplay(viewport, scale, allowAuxiliaryRate);
        return new(admission, _record, measurement, Measurement.Slot);
    }
}
