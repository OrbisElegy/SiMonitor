// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class CapturedRecordMeasurementException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class CapturedRecordCursor
{
    internal CapturedRecordCursor(object owner, RecordSlotBinding slot, EcgManualCursor value)
    { Owner = owner; Slot = slot; Value = value; }
    internal object Owner { get; }
    public RecordSlotBinding Slot { get; }
    public EcgManualCursor Value { get; }
}

public sealed record CapturedRecordMeasurementCheckpoint(CapturedRecordBindingState Record,
    string SlotId, EcgManualCursor First, EcgManualCursor Second);
public sealed record RestoredRecordMeasurement(CapturedRecordMeasurement Measurement,
    CapturedRecordCursor First, CapturedRecordCursor Second);
public sealed record ProjectedRecordCursor(CapturedRecordCursor Cursor, SweepPixelPosition X, EcgVerticalPosition Y);
public sealed record RecordCursorViewport(long StartDataTimeNs, long EndExclusiveDataTimeNs, int PlotLeftPixels, int PlotWidthPixels);
public enum RecordCursorEnd { First, Second }
public sealed record CapturedRecordCursorPair(CapturedRecordCursor First, CapturedRecordCursor Second);
public sealed record RecordMeasurementDisplay(string ReasonCode, ProjectedRecordCursor? First,
    ProjectedRecordCursor? Second, EcgManualMeasurementResult? Measurement);

// Local measurement ownership, not authority identity or authentication of amplitude.
public sealed class CapturedRecordMeasurement
{
    private readonly object _owner = new();
    private readonly CapturedRecordBinding _record;
    private readonly PinnedRecordRange _range;
    private SystemViewCommandAssessmentPolicy _policy;

    public CapturedRecordMeasurement(CapturedRecordBinding record, string slotId, SystemViewCommandAssessmentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(record);
        _record = record;
        _range = record.CapturePinnedRecordRange();
        Slot = record.Slots.FirstOrDefault(slot => string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
            ?? throw new CapturedRecordMeasurementException("RecordMeasurement.UnknownSlot", nameof(slotId));
        UpdatePolicy(policy);
    }

    public RecordSlotBinding Slot { get; }
    public CapturedRecordCursorPair? CurrentPair { get; private set; }

    // Serialized UI composition; callers must replace their prior display with this result.
    public RecordMeasurementDisplay CaptureDisplay(RecordCursorViewport viewport, EcgVerticalScale verticalScale,
        bool allowAuxiliaryRate)
    {
        if (_policy != SystemViewCommandAssessmentPolicy.Enabled)
        {
            return new(_policy == SystemViewCommandAssessmentPolicy.CourseLocked
                ? "RecordMeasurement.CourseLocked" : "RecordMeasurement.Disabled", null, null, null);
        }
        CapturedRecordCursorPair? pair = CurrentPair;
        if (pair is null) { return new("RecordMeasurement.NoCursorPair", null, null, null); }
        ProjectedRecordCursor? first = ProjectCursor(pair.First, viewport, verticalScale);
        ProjectedRecordCursor? second = ProjectCursor(pair.Second, viewport, verticalScale);
        EcgManualMeasurementResult result = Calculate(pair.First, pair.Second, allowAuxiliaryRate);
        return new("RecordMeasurement.Ready", first, second, result);
    }

    public void ClearPair()
    {
        EnsureEnabled();
        CurrentPair = null;
    }

    public CapturedRecordCursorPair ReplacePair(EcgManualCursor first, EcgManualCursor second)
    {
        CapturedRecordCursor a = CreateCursor(first), b = CreateCursor(second);
        _ = Calculate(a, b, false);
        return CurrentPair = new(a, b);
    }

    // Both manually placed points share one resolved page and calibration.
    public CapturedRecordCursorPair ReplacePairFromPoints(ExactPlotCoordinate firstX, ExactPlotCoordinate firstY,
        ExactPlotCoordinate secondX, ExactPlotCoordinate secondY, RecordCursorViewport viewport,
        EcgVerticalScale verticalScale)
    {
        CapturedRecordCursor first = CreateCursorFromPoint(firstX, firstY, viewport, verticalScale);
        CapturedRecordCursor second = CreateCursorFromPoint(secondX, secondY, viewport, verticalScale);
        _ = Calculate(first, second, false);
        return CurrentPair = new(first, second);
    }

    public CapturedRecordCursorPair MoveCursor(RecordCursorEnd end, ExactPlotCoordinate x, ExactPlotCoordinate y,
        RecordCursorViewport viewport, EcgVerticalScale verticalScale)
    {
        EnsureEnabled();
        if (!Enum.IsDefined(end))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidCursorEnd", nameof(end)); }
        CapturedRecordCursorPair pair = RequirePair();
        CapturedRecordCursor moved = CreateCursorFromPoint(x, y, viewport, verticalScale);
        CapturedRecordCursorPair candidate = end == RecordCursorEnd.First ? pair with { First = moved } : pair with { Second = moved };
        _ = Calculate(candidate.First, candidate.Second, false);
        return CurrentPair = candidate;
    }

    public CapturedRecordMeasurementCheckpoint CaptureCheckpoint()
    {
        EnsureEnabled();
        CapturedRecordCursorPair pair = RequirePair();
        return CaptureCheckpoint(pair.First, pair.Second);
    }

    private CapturedRecordCursorPair RequirePair() => CurrentPair ??
        throw new CapturedRecordMeasurementException("RecordMeasurement.NoCursorPair", nameof(CurrentPair));

    public CapturedRecordMeasurementCheckpoint CaptureCheckpoint(CapturedRecordCursor first, CapturedRecordCursor second)
    {
        _ = Calculate(first, second, false);
        return new(_record.CaptureState(), Slot.SlotId, first.Value, second.Value);
    }

    public static RestoredRecordMeasurement Restore(CapturedRecordMeasurementCheckpoint checkpoint,
        SystemViewCommandAssessmentPolicy currentPolicy)
    {
        if (!Enum.IsDefined(currentPolicy))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPolicy", nameof(currentPolicy)); }
        try
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            CapturedRecordMeasurement measurement = new(CapturedRecordBinding.Restore(checkpoint.Record), checkpoint.SlotId, currentPolicy);
            CapturedRecordCursorPair pair = measurement.ReplacePair(checkpoint.First, checkpoint.Second);
            return new(measurement, pair.First, pair.Second);
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.Disabled" or "RecordMeasurement.CourseLocked")
        { throw; }
        catch (ArgumentException)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidCheckpoint", nameof(checkpoint)); }
    }

    // Serialized caller supplies the currently resolved course policy.
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPolicy", nameof(policy)); }
        _policy = policy;
    }

    public CapturedRecordCursor CreateCursor(EcgManualCursor value)
    {
        EnsureEnabled();
        _ = EcgManualMeasurement.Calculate(value, value, false);
        if (value.DataTimeNs < _range.StartDataSimTimeNs || value.DataTimeNs >= _range.EndExclusiveDataSimTimeNs)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.CursorOutsideRecord", nameof(value)); }
        return new(_owner, Slot, value);
    }

    // Explicit rational pointer coordinates. No snapping or implicit quantization.
    public CapturedRecordCursor CreateCursorFromPoint(ExactPlotCoordinate x, ExactPlotCoordinate y,
        RecordCursorViewport viewport, EcgVerticalScale verticalScale)
    {
        EnsureEnabled();
        ValidateViewport(viewport);
        _ = EcgVerticalGeometry.MapMicrovolts(verticalScale, 0, 1);
        if (x is null || y is null || x.Denominator <= 0 || y.Denominator <= 0 ||
            x.Numerator < (BigInteger)viewport.PlotLeftPixels * x.Denominator ||
            x.Numerator >= ((BigInteger)viewport.PlotLeftPixels + viewport.PlotWidthPixels) * x.Denominator ||
            y.Numerator < (BigInteger)verticalScale.PlotTopPixels * y.Denominator ||
            y.Numerator > ((BigInteger)verticalScale.PlotTopPixels + verticalScale.PlotHeightPixels) * y.Denominator)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPoint", nameof(x)); }
        BigInteger elapsed = BigInteger.DivRem(
            (x.Numerator - (BigInteger)viewport.PlotLeftPixels * x.Denominator) *
                (viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs),
            x.Denominator * viewport.PlotWidthPixels, out BigInteger remainder);
        if (!remainder.IsZero)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.UnrepresentableTime", nameof(x)); }
        BigInteger numerator = ((BigInteger)verticalScale.ZeroBaselinePixels * y.Denominator - y.Numerator) *
            1000 * verticalScale.PixelsPerMillivoltDenominator;
        BigInteger denominator = y.Denominator * verticalScale.PixelsPerMillivoltNumerator;
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;
        if (numerator < long.MinValue || numerator > long.MaxValue || denominator > uint.MaxValue)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.UnrepresentableAmplitude", nameof(y)); }
        return CreateCursor(new((long)(viewport.StartDataTimeNs + elapsed), (long)numerator, (uint)denominator));
    }

    // Pixel layout is never written back to cursor evidence.
    public ProjectedRecordCursor ProjectCursor(CapturedRecordCursor cursor, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale verticalScale) => ProjectCursor(cursor,
            new RecordCursorViewport(_range.StartDataSimTimeNs, _range.EndExclusiveDataSimTimeNs, plotLeftPixels, plotWidthPixels), verticalScale)!;

    // Null means the valid cursor lies outside this half-open page, not missing data.
    public ProjectedRecordCursor? ProjectCursor(CapturedRecordCursor cursor, RecordCursorViewport viewport,
        EcgVerticalScale verticalScale)
    {
        EnsureEnabled();
        ValidateOwner(cursor, nameof(cursor));
        ValidateViewport(viewport);
        ulong duration = (ulong)(viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs);
        EcgVerticalPosition y = EcgVerticalGeometry.MapMicrovolts(verticalScale,
            cursor.Value.MicrovoltsNumerator, cursor.Value.MicrovoltsDenominator);
        if (cursor.Value.DataTimeNs < viewport.StartDataTimeNs || cursor.Value.DataTimeNs >= viewport.EndExclusiveDataTimeNs) { return null; }
        ulong offset = (ulong)(cursor.Value.DataTimeNs - viewport.StartDataTimeNs);
        SweepPixelPosition x = SweepPlotGeometry.MapSampleOffset(offset, duration, viewport.PlotLeftPixels, viewport.PlotWidthPixels);
        return new(cursor, x, y);
    }

    public EcgManualMeasurementResult Calculate(CapturedRecordCursor first, CapturedRecordCursor second,
        bool allowAuxiliaryRate)
    {
        EnsureEnabled();
        ValidateOwner(first, nameof(first));
        ValidateOwner(second, nameof(second));
        return EcgManualMeasurement.Calculate(first.Value, second.Value, allowAuxiliaryRate);
    }

    private void ValidateViewport(RecordCursorViewport viewport)
    {
        if (viewport is null || viewport.StartDataTimeNs < _range.StartDataSimTimeNs ||
            viewport.EndExclusiveDataTimeNs > _range.EndExclusiveDataSimTimeNs || viewport.StartDataTimeNs >= viewport.EndExclusiveDataTimeNs)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidViewport", nameof(viewport)); }
        _ = SweepPlotGeometry.MapSampleOffset(0, (ulong)(viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs),
            viewport.PlotLeftPixels, viewport.PlotWidthPixels);
    }

    private void EnsureEnabled()
    {
        if (_policy != SystemViewCommandAssessmentPolicy.Enabled)
        {
            throw new CapturedRecordMeasurementException(_policy == SystemViewCommandAssessmentPolicy.CourseLocked
                ? "RecordMeasurement.CourseLocked" : "RecordMeasurement.Disabled", "policy");
        }
    }

    private void ValidateOwner(CapturedRecordCursor cursor, string parameterName)
    {
        if (cursor is null || !ReferenceEquals(cursor.Owner, _owner))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.ForeignCursor", parameterName); }
    }
}
