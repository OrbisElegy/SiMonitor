// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static ResolvedEcgVoltageChannel UnitChannel(string unit = "MICROVOLT") => new(500, 1, new(1, 1, 0, 1, unit));

    private static CapturedRecordVoltagePageDisplay VoltagePage(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, CapturedRecordVoltageBinding binding, bool overlay = false,
        CancellationToken cancellationToken = default) => view.CaptureVoltagePageDisplay(navigation,
            binding, overlay, 0, 100, new(0, 100, 60, 20, 1), false, 100, cancellationToken);

    private static void ExpectVoltageReason(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected voltage rejection"); }
        catch (EcgRawVoltageException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }

    private static void RawVoltageConvertsExactUnitsAndSignedCalibration()
    {
        EcgRawVoltageCalibration uv = new(-3, 7, 5, 11, "MICROVOLT");
        Check.That(uv.Convert(2) == new EcgSampleVoltage(-31, 77) &&
            (uv with { UnitCode = "MILLIVOLT" }).Convert(2) == new EcgSampleVoltage(-31000, 77) &&
            new EcgRawVoltageCalibration(0, uint.MaxValue, 0, uint.MaxValue, "MILLIVOLT").Convert(short.MinValue) == new EcgSampleVoltage(0, 1) &&
            new EcgRawVoltageCalibration(int.MinValue, 1, int.MaxValue, 1, "MILLIVOLT").Convert(short.MinValue) ==
                new EcgSampleVoltage(70_370_891_661_311_000, 1),
            "raw calibration must apply signed scale plus offset, convert declared units and reduce exactly at integer extremes");
    }

    private static void RawVoltageRejectsInvalidAndUnrepresentableValues()
    {
        EcgRawVoltageCalibration valid = new(1, 1, 0, 1, "MICROVOLT");
        foreach (string unit in new[] { "MMHG", "UNITLESS", "microvolt", "" })
        { ExpectVoltageReason(() => (valid with { UnitCode = unit }).Convert(1), "EcgRawVoltage.UnsupportedUnit"); }
        ExpectVoltageReason(() => (valid with { ScaleDenominator = 0 }).Convert(1), "EcgRawVoltage.InvalidCalibration");
        ExpectVoltageReason(() => (valid with { OffsetDenominator = 0 }).Convert(1), "EcgRawVoltage.InvalidCalibration");
        EcgRawVoltageCalibration wide = new(1, uint.MaxValue, 1, uint.MaxValue - 1, "MICROVOLT");
        Check.That(wide.Convert(0) == new EcgSampleVoltage(1, uint.MaxValue - 1), "reduce before checking output width");
        ExpectVoltageReason(() => wide.Convert(1), "EcgRawVoltage.UnrepresentableVoltage");
    }

    private static void VoltagePageCombinesRawEvidenceAndUnclampedGeometry()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordVoltageBinding binding = new(record, "ecg.slot0", UnitChannel());
        CapturedRecordVoltagePageDisplay page = VoltagePage(view, navigation, binding);
        Check.That(page.Blocks.Single().Voltages[0] == new EcgSampleVoltage(111, 1) &&
            page.Blocks.Single().SampleY[0] == new EcgVerticalPosition(2889, 50, VerticalPlotRelation.WithinPlot) &&
            page.Blocks.Single().Source.Source.Plane.Samples[0] == 111 &&
            page.Blocks.Single().Source.SampleX[^1] == new ExactPlotCoordinate(99, 1),
            "explicit slot mapping must produce matching raw, X, voltage and Y evidence despite independent command locks");
        CapturedRecordVoltagePageDisplay mv = VoltagePage(view, navigation, new(record, "ecg.slot0", UnitChannel("MILLIVOLT")));
        Check.That(mv.Blocks.Single().Voltages[0] == new EcgSampleVoltage(111000, 1) &&
            mv.Blocks.Single().SampleY[0].Relation == VerticalPlotRelation.AbovePlot,
            "declared millivolts must convert explicitly and retain out-of-plot amplitudes without clamping");
    }

    private static void VoltageBindingRejectsMismatchedAndForeignSources()
    {
        CapturedRecordBinding record = MeasurementRecord();
        ResolvedEcgVoltageChannel channel = UnitChannel();
        foreach (ResolvedEcgVoltageChannel mismatch in new[]
        {
            channel with { SampleRateNumerator = 250 }, channel with { SampleRateDenominator = 2 },
            channel with { Calibration = channel.Calibration with { ScaleNumerator = 2 } },
            channel with { Calibration = channel.Calibration with { ScaleDenominator = 2 } },
            channel with { Calibration = channel.Calibration with { OffsetNumerator = 1 } },
            channel with { Calibration = channel.Calibration with { OffsetDenominator = 2 } },
        })
        { ExpectVoltageReason(() => _ = new CapturedRecordVoltageBinding(record, "ecg.slot0", mismatch), "EcgRawVoltage.ProfileMismatch"); }
        ExpectVoltageReason(() => _ = new CapturedRecordVoltageBinding(record, "missing", channel), "EcgRawVoltage.UnknownSlot");
        ExpectVoltageReason(() => _ = new CapturedRecordVoltageBinding(record, "ecg.slot0", channel with { SampleRateNumerator = 1_000_001 }), "EcgRawVoltage.InvalidChannel");
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordVoltageBinding binding = new(record, "ecg.slot0", channel);
        ExpectVoltageReason(() => VoltagePage(view, navigation, new(MeasurementRecord(), "ecg.slot0", channel)), "EcgRawVoltage.ForeignBinding");
        view.SelectMeasurementSlot("ecg.slot1");
        ExpectVoltageReason(() => VoltagePage(view, navigation, binding), "EcgRawVoltage.ForeignBinding");
    }

    private static void VoltagePageFailureAndSafetyLossPreserveEvidence()
    {
        EcgRawVoltageCalibration wide = new(1, uint.MaxValue, 1, uint.MaxValue - 1, "MICROVOLT");
        WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(Wire(100, 0));
        short[] values = new short[100];
        values[^1] = 1;
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(envelope with
        {
            Planes = envelope.Planes.Select(plane => plane with
            {
                ScaleDenominator = wide.ScaleDenominator,
                OffsetNumerator = 1,
                OffsetDenominator = wide.OffsetDenominator,
                Samples = values,
                QualityEncoding = WaveformQualityEncoding.Ranges,
                QualityRanges = new WaveformQualityRange[] { new(0, 100, 0x80000000) },
            }).ToArray(),
        });
        var record = CapturedRecordBinding.Create(BindingPresentation().CaptureState(),
            WaveformRecordArchive.Create(Plan(endExclusiveSimTimeNs: 200_000_000), [wire]), BindingSlots());
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(198_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        CapturedRecordVoltageBinding binding = new(record, "ecg.slot0", new(500, 1, wide));
        CapturedRecordVoltagePageDisplay before = VoltagePage(view, navigation, binding, true);
        CapturedRecordNavigation whole = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectVoltageReason(() => VoltagePage(view, whole, binding, true), "EcgRawVoltage.UnrepresentableVoltage");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { VoltagePage(view, navigation, binding, true, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        CapturedRecordVoltagePageDisplay denied = VoltagePage(view, navigation, null!);
        Check.That(cancelled && ReferenceEquals(pair, view.Measurement.CurrentPair) && before.Blocks.Single().Voltages.Count == 99 &&
            before.Blocks.Single().Source.Source.Plane.QualityRanges.Single().QualityFlags == 0x80000000 &&
            denied.Blocks.Count == 0 && denied.VoltageChannel is null && denied.VerticalScale is null &&
            VoltagePage(view, navigation, binding, true).Blocks.Single().Voltages.SequenceEqual(before.Blocks.Single().Voltages),
            "late conversion failure, cancellation and safety loss must preserve evidence; opaque quality flags never imply drawability");
    }

    private static void VoltagePageRestoreRequiresFreshResolution()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordVoltageBinding old = new(record, "ecg.slot0", UnitChannel());
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(view.CaptureSession(navigation),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectVoltageReason(() => VoltagePage(restored.View, restored.Navigation, old), "EcgRawVoltage.ForeignBinding");
        CapturedRecordBinding fresh = ReadWaveformPage(restored.View, restored.Navigation).Content.Study.Record!;
        CapturedRecordVoltagePageDisplay after = VoltagePage(restored.View, restored.Navigation, new(fresh, "ecg.slot0", UnitChannel()));
        bool immutable = false;
        try { ((IList<EcgSampleVoltage>)after.Blocks.Single().Voltages)[0] = new(0, 1); }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && after.Blocks.Single().Voltages.SequenceEqual(VoltagePage(view, navigation, old).Blocks.Single().Voltages),
            "restored records require fresh explicit voltage resolution and own immutable output");
    }
}
