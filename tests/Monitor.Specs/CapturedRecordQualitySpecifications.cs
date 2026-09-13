// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static CapturedRecordBinding QualityRecord()
    {
        WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(Wire(100, 0));
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(envelope with
        {
            Planes = envelope.Planes.Select(plane => plane with
            {
                QualityEncoding = WaveformQualityEncoding.Ranges,
                QualityRanges = new WaveformQualityRange[] { new(2, 2, 1), new(4, 1, 3), new(99, 1, 0x80000000) },
            }).ToArray(),
        });
        return CapturedRecordBinding.Create(BindingPresentation().CaptureState(),
            WaveformRecordArchive.Create(Plan(endExclusiveSimTimeNs: 200_000_000), [wire]), BindingSlots());
    }

    private static RecordQualityRule[] QualityRules() => [new(0, true), new(1, false), new(3, true), new(0x80000000, false)];

    private static CapturedRecordQualityPageDisplay QualityPage(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, CapturedRecordVoltageBinding voltage,
        CapturedRecordQualityBinding quality, bool overlay = false, CancellationToken token = default) =>
        view.CaptureQualityPageDisplay(navigation, voltage, quality, overlay, 0, 100,
            new(0, 100, 60, 20, 1), false, 100, token);

    private static void ExpectQualityReason(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected quality rejection"); }
        catch (CapturedRecordQualityException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }

    private static void QualityPageResolvesExactFlagsAndSparseGaps()
    {
        CapturedRecordBinding record = QualityRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(6_000_000, 1, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordVoltageBinding voltage = new(record, "ecg.slot0", UnitChannel());
        CapturedRecordQualityBinding quality = new(record, "ecg.slot0", QualityRules(), 4);
        CapturedRecordQualityPageDisplay page = QualityPage(view, navigation, voltage, quality);
        Check.That(page.Blocks.Single().Samples.SequenceEqual(new RecordSampleQuality[]
            { new(1, false), new(3, true), new(0, true) }) &&
            page.Blocks.Single().Source.Source.Source.Plane.FirstSampleIndex == 3 &&
            page.Blocks.Single().Source.Voltages.Count == 3,
            "rebased quality ranges must retain exact whole-word matches and explicit zero-gap decisions without dropping voltage evidence");
        CapturedRecordQualityBinding zeroDenied = new(record, "ecg.slot0", [new(0, false), new(1, true), new(3, false)], 3);
        IReadOnlyList<RecordSampleQuality> inverted = QualityPage(view, navigation, voltage, zeroDenied).Blocks.Single().Samples;
        Check.That(inverted[0].Drawable && !inverted[1].Drawable && !inverted[2].Drawable,
            "zero flags and combined flags have no implicit meaning outside the caller's current explicit declaration");
    }

    private static void QualityRulesRejectAmbiguityAndOwnCallerData()
    {
        CapturedRecordBinding record = QualityRecord();
        foreach (RecordQualityRule[] invalid in new RecordQualityRule[][]
        { [], [new(1, true), new(1, false)], [new(3, true), new(1, true)], [null!] })
        { ExpectQualityReason(() => _ = new CapturedRecordQualityBinding(record, "ecg.slot0", invalid, 4), "RecordQuality.InvalidRules"); }
        ExpectQualityReason(() => _ = new CapturedRecordQualityBinding(record, "ecg.slot0", QualityRules(), 3), "RecordQuality.InvalidRules");
        ExpectQualityReason(() => _ = new CapturedRecordQualityBinding(record, "missing", QualityRules(), 4), "RecordQuality.UnknownSlot");
        RecordQualityRule[] rules = QualityRules();
        CapturedRecordQualityBinding binding = new(record, "ecg.slot0", rules, 4);
        rules[0] = new(0, false);
        bool immutable = false;
        try { ((IList<RecordQualityRule>)binding.Rules)[0] = rules[0]; }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && binding.Rules[0].Drawable, "accepted canonical rules must own caller arrays");
    }

    private static void QualityPageFailureAndDenialPreserveEvidence()
    {
        CapturedRecordBinding record = QualityRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 10, 1));
        CapturedRecordVoltageBinding voltage = new(record, "ecg.slot0", UnitChannel());
        CapturedRecordQualityBinding quality = new(record, "ecg.slot0", QualityRules(), 4);
        CapturedRecordQualityPageDisplay before = QualityPage(view, navigation, voltage, quality, true);
        CapturedRecordQualityBinding missingLast = new(record, "ecg.slot0", QualityRules()[..3], 3);
        ExpectQualityReason(() => QualityPage(view, navigation, voltage, missingLast, true), "RecordQuality.UnknownFlags");
        CapturedRecordQualityBinding missingCombination = new(record, "ecg.slot0", [new(0, true), new(1, true), new(2, true), new(0x80000000, true)], 4);
        ExpectQualityReason(() => QualityPage(view, navigation, voltage, missingCombination, true), "RecordQuality.UnknownFlags");
        CapturedRecordQualityBinding missingZero = new(record, "ecg.slot0", [new(1, true)], 1);
        ExpectQualityReason(() => QualityPage(view, navigation, voltage, missingZero, true), "RecordQuality.UnknownFlags");
        CapturedRecordQualityPageDisplay denied = QualityPage(view, navigation, null!, null!);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { QualityPage(view, navigation, voltage, quality, true, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && ReferenceEquals(pair, view.Measurement.CurrentPair) && denied.Blocks.Count == 0 && denied.Rules.Count == 0 &&
            before.Blocks.Single().Samples[^1] == new RecordSampleQuality(0x80000000, false) &&
            QualityPage(view, navigation, voltage, quality, true).Blocks.Single().Samples.SequenceEqual(before.Blocks.Single().Samples),
            "unknown late/combined flags, cancellation and safety denial must publish no partial quality result or change cursor/source evidence");
    }

    private static void QualityPageRestoreRequiresCurrentBoundRules()
    {
        CapturedRecordBinding record = QualityRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordQualityBinding old = new(record, "ecg.slot0", QualityRules(), 4);
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(view.CaptureSession(navigation),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordBinding fresh = ReadWaveformPage(restored.View, restored.Navigation).Content.Study.Record!;
        CapturedRecordVoltageBinding voltage = new(fresh, "ecg.slot0", UnitChannel());
        ExpectQualityReason(() => QualityPage(restored.View, restored.Navigation, voltage, old), "RecordQuality.ForeignBinding");
        CapturedRecordQualityBinding current = new(fresh, "ecg.slot0", QualityRules().Select(rule => rule with { Drawable = false }).ToArray(), 4);
        CapturedRecordQualityPageDisplay page = QualityPage(restored.View, restored.Navigation, voltage, current);
        Check.That(page.Blocks.Single().Samples.All(sample => !sample.Drawable), "restore uses current explicit quality resolution, not saved decisions");
        bool immutable = false;
        try { ((IList<RecordSampleQuality>)page.Blocks.Single().Samples)[0] = new(0, true); }
        catch (NotSupportedException) { immutable = true; }
        restored.View.SelectMeasurementSlot("ecg.slot1");
        ExpectQualityReason(() => QualityPage(restored.View, restored.Navigation,
            new(fresh, "ecg.slot1", UnitChannel()), current), "RecordQuality.ForeignBinding");
        Check.That(immutable, "quality results must own immutable sample decisions");
    }
}
