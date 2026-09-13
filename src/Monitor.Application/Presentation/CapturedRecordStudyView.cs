// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordStudyDisplay(Ecg12ViewAdmissionDecision Admission,
    CapturedRecordBinding? Record, RecordMeasurementDisplay? Measurement, RecordSlotBinding? MeasurementSlot);

public sealed record CapturedRecordPageDisplay(CapturedRecordStudyDisplay Study,
    CapturedRecordNavigationDisplay? Navigation, RecordCursorViewport? Viewport)
{
    public CapturedRecordPage? Page => Navigation?.Page;
}

public sealed record ThemedCapturedRecordPageDisplay(CapturedRecordPageDisplay Content, Ecg12ThemeDisplay? Theme);
public sealed record CapturedRecordWaveformPageDisplay(CapturedRecordPageDisplay Content,
    ArchivedWaveformChannelRead? Waveform);
public sealed record RecordScreenZoomLayout(int PageWidth, int PageHeight, int AvailableWidth, int AvailableHeight);
public sealed record ZoomedCapturedRecordPageDisplay(ThemedCapturedRecordPageDisplay Content,
    Ecg12ZoomDisplay? Zoom, Ecg12ScreenTransform? Transform);
public sealed record GridCapturedRecordPageDisplay(ThemedCapturedRecordPageDisplay Content,
    EcgPaperGridPlan? GridPlan, IReadOnlyList<EcgPaperGridLine> GridLines);

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

    public CapturedRecordVoltagePageDisplay CaptureVoltagePageDisplay(CapturedRecordNavigation navigation,
        CapturedRecordVoltageBinding voltageBinding, bool canPreserveGlobalSafetyOverlay,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate,
        int maximumSamples, CancellationToken cancellationToken = default)
    {
        CapturedRecordWaveformHorizontalPageDisplay content = CaptureWaveformHorizontalPageDisplay(navigation,
            canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale,
            allowAuxiliaryRate, maximumSamples, cancellationToken);
        if (!content.Content.Content.Study.Admission.MayEnter)
        { return new(content, null, null, Array.Empty<CapturedRecordVoltageBlock>()); }
        ArgumentNullException.ThrowIfNull(voltageBinding);
        voltageBinding.RequireBinding(_record, content.Content.Content.Study.MeasurementSlot!);
        return CapturedRecordVoltageProjection.Build(content, voltageBinding, scale, cancellationToken);
    }

    public CapturedRecordWaveformHorizontalPageDisplay CaptureWaveformHorizontalPageDisplay(
        CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        bool allowAuxiliaryRate, int maximumSamples, CancellationToken cancellationToken = default)
    {
        CapturedRecordWaveformPageDisplay content = CaptureWaveformPageDisplay(navigation,
            canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale,
            allowAuxiliaryRate, maximumSamples, cancellationToken);
        return CapturedRecordWaveformHorizontalProjection.Build(content, cancellationToken);
    }

    // Serialized capture from current navigation and slot selection, not a saved
    // display supplied by the caller. Raw samples still need trusted unit/quality resolution.
    public CapturedRecordWaveformPageDisplay CaptureWaveformPageDisplay(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CapturedRecordPageDisplay content = CapturePageDisplay(navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale, allowAuxiliaryRate);
        if (!content.Study.Admission.MayEnter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(content, null);
        }
        CapturedRecordPage page = content.Page!;
        ArchivedWaveformChannelRead waveform = _record.ReadChannel(content.Study.MeasurementSlot!.ChannelId,
            page.StartDataTimeNs, page.EndExclusiveDataTimeNs, maximumSamples, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, waveform);
    }

    // Coordinates in Content remain unscaled. Apply Transform to the whole page once.
    public ZoomedCapturedRecordPageDisplay CaptureZoomedPageDisplay(CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout,
        bool allowAuxiliaryRate)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        ThemedCapturedRecordPageDisplay content = CaptureThemedPageDisplay(navigation, theme,
            canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, allowAuxiliaryRate);
        if (!content.Content.Study.Admission.MayEnter) { return new(content, null, null); }
        ArgumentNullException.ThrowIfNull(layout);
        Ecg12ZoomDisplay selection = zoom.CaptureDisplay();
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(selection.Selection,
            layout.PageWidth, layout.PageHeight, layout.AvailableWidth, layout.AvailableHeight);
        return new(content, selection, transform);
    }

    public GridCapturedRecordPageDisplay CaptureGridPageDisplay(CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, EcgPaperScale paperScale,
        int gridOriginXPixels, int gridOriginYPixels, int maximumGridLines, bool allowAuxiliaryRate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThemedCapturedRecordPageDisplay content = CaptureThemedPageDisplay(navigation, theme,
            canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, allowAuxiliaryRate);
        if (content.Theme?.Theme != Ecg12Theme.PaperGridBlack)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(content, null, Array.Empty<EcgPaperGridLine>());
        }
        RecordCursorViewport viewport = content.Content.Viewport!;
        EcgPaperGridPlan plan = EcgPaperGridCalibration.Resolve(viewport.PlotLeftPixels, viewport.PlotWidthPixels,
            (ulong)(viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs), scale, paperScale, gridOriginXPixels, gridOriginYPixels);
        IReadOnlyList<EcgPaperGridLine> lines = EcgPaperGridGeometry.Build(plan, maximumGridLines, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, plan, lines);
    }

    public ThemedCapturedRecordPageDisplay CaptureThemedPageDisplay(CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate)
    {
        ArgumentNullException.ThrowIfNull(theme);
        CapturedRecordPageDisplay content = CapturePageDisplay(navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale, allowAuxiliaryRate);
        return new(content, content.Study.Admission.MayEnter ? theme.CaptureDisplay() : null);
    }

    // Trusted serialized policy delivery; validate the full update before changing any command group.
    public void UpdateThemedCommandPolicies(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme,
        SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy,
        SystemViewCommandAssessmentPolicy themePolicy, bool allowLocalThemeSelection)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!Enum.IsDefined(themePolicy))
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidPolicy", nameof(themePolicy)); }
        UpdateCommandPolicies(navigation, paginationPolicy, measurementPolicy);
        theme.UpdatePolicy(themePolicy, allowLocalThemeSelection);
    }

    // Trusted serialized delivery. No command group changes until every input validates.
    public void UpdateZoomedCommandPolicies(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme,
        Ecg12ZoomSelection zoom, SystemViewCommandAssessmentPolicy paginationPolicy,
        SystemViewCommandAssessmentPolicy measurementPolicy, SystemViewCommandAssessmentPolicy themePolicy,
        bool allowLocalThemeSelection, SystemViewCommandAssessmentPolicy zoomPolicy)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        if (!Enum.IsDefined(zoomPolicy))
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidPolicy", nameof(zoomPolicy)); }
        UpdateThemedCommandPolicies(navigation, theme, paginationPolicy, measurementPolicy, themePolicy, allowLocalThemeSelection);
        zoom.UpdatePolicy(zoomPolicy);
    }

    public void UpdateCommandPolicies(CapturedRecordNavigation navigation,
        SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy)
    {
        if (navigation is null || !navigation.IsBoundTo(_record))
        { throw new CapturedRecordPaginationException("RecordPagination.ForeignNavigation", nameof(navigation)); }
        if (!Enum.IsDefined(paginationPolicy))
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidPolicy", nameof(paginationPolicy)); }
        if (!Enum.IsDefined(measurementPolicy))
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidPolicy", nameof(measurementPolicy)); }
        navigation.UpdatePolicy(paginationPolicy);
        Measurement.UpdatePolicy(measurementPolicy);
    }

    public CapturedRecordPage ResolvePage(long pageDurationNs, ulong pageIndex) =>
        CapturedRecordPagination.Resolve(_record, pageDurationNs, pageIndex);

    public CapturedRecordNavigation CreateNavigation(long pageDurationNs, ulong initialPageIndex,
        SystemViewCommandAssessmentPolicy paginationPolicy) => new(_record, pageDurationNs, initialPageIndex, paginationPolicy);

    public CapturedRecordStudySessionState CaptureSession(CapturedRecordNavigation navigation)
    {
        if (navigation is null || !navigation.IsBoundTo(_record))
        { throw new CapturedRecordPaginationException("RecordPagination.ForeignNavigation", nameof(navigation)); }
        CapturedRecordCursorPair? pair = Measurement.CurrentPair;
        if (pair is not null) { _ = Measurement.Calculate(pair.First, pair.Second, false); }
        return new(navigation.CaptureState(), Measurement.Slot.SlotId, pair?.First.Value, pair?.Second.Value);
    }

    public ThemedCapturedRecordStudySessionState CaptureThemedSession(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new(CaptureSession(navigation), theme.CaptureState());
    }

    public ZoomedCapturedRecordStudySessionState CaptureZoomedSession(CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        return new(CaptureThemedSession(navigation, theme), zoom.CaptureState());
    }

    public void SelectMeasurementSlot(string slotId)
    {
        if (string.Equals(Measurement.Slot.SlotId, slotId, StringComparison.Ordinal)) { return; }
        CapturedRecordMeasurement replacement = new(_record, slotId, Measurement.CurrentPolicy);
        Measurement = replacement;
    }

    public void ClearPairOnCurrentPage(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale)
    {
        _ = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        Measurement.ClearPair();
    }

    public CapturedRecordCursorPair PlacePairOnCurrentPage(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        ExactPlotCoordinate firstX, ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        return Measurement.ReplacePairFromPoints(firstX, firstY, secondX, secondY, viewport, scale);
    }

    // Inputs are scaled page-local coordinates; shell origin/scroll translation precedes this call.
    public CapturedRecordCursorPair PlacePairOnZoomedPage(CapturedRecordNavigation navigation,
        Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate firstX,
        ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        ArgumentNullException.ThrowIfNull(zoom);
        ArgumentNullException.ThrowIfNull(layout);
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(zoom.Selection,
            layout.PageWidth, layout.PageHeight, layout.AvailableWidth, layout.AvailableHeight);
        return Measurement.ReplacePairFromPoints(transform.Inverse(firstX), transform.Inverse(firstY),
            transform.Inverse(secondX), transform.Inverse(secondY), viewport, scale);
    }

    public RecordCursorHits HitTestOnZoomedPage(CapturedRecordNavigation navigation, Ecg12ZoomSelection zoom,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        ArgumentNullException.ThrowIfNull(zoom);
        ArgumentNullException.ThrowIfNull(layout);
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(zoom.Selection,
            layout.PageWidth, layout.PageHeight, layout.AvailableWidth, layout.AvailableHeight);
        if (radius is null || radius.Numerator <= 0 || radius.Denominator <= 0)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidHitRadius", nameof(radius)); }
        return Measurement.HitTestCursors(transform.Inverse(x), transform.Inverse(y), transform.Inverse(radius), viewport, scale);
    }

    public CapturedRecordZoomedDrag BeginCursorDragOnZoomedPage(CapturedRecordNavigation navigation,
        Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y,
        ExactPlotCoordinate radius) => new(this, navigation, zoom, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale, layout, x, y, radius);

    public CapturedRecordCursorPair MoveCursorOnZoomedPage(CapturedRecordNavigation navigation,
        Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordScreenZoomLayout layout, RecordCursorEnd end,
        ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        ArgumentNullException.ThrowIfNull(zoom);
        ArgumentNullException.ThrowIfNull(layout);
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(zoom.Selection,
            layout.PageWidth, layout.PageHeight, layout.AvailableWidth, layout.AvailableHeight);
        return Measurement.MoveCursor(end, transform.Inverse(x), transform.Inverse(y), viewport, scale);
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

    public RecordCursorHits HitTestOnCurrentPage(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius)
    {
        RecordCursorViewport viewport = RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        return Measurement.HitTestCursors(x, y, radius, viewport, scale);
    }

    public CapturedRecordStudyDrag BeginCursorDragAtPoint(CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale,
        ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius)
    {
        RecordCursorHits hits = HitTestOnCurrentPage(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, x, y, radius);
        RecordCursorEnd end = hits switch
        {
            RecordCursorHits.First => RecordCursorEnd.First,
            RecordCursorHits.Second => RecordCursorEnd.Second,
            RecordCursorHits.None => throw new CapturedRecordMeasurementException("RecordMeasurement.NoCursorHit", nameof(x)),
            _ => throw new CapturedRecordMeasurementException("RecordMeasurement.AmbiguousCursorHit", nameof(x)),
        };
        return new(this, navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, end, x, y);
    }

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
