// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AuthoredQrsMeasurementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QMeasurementUsesInitialDeflectionAndInclusiveBoundaries), QMeasurementUsesInitialDeflectionAndInclusiveBoundaries),
        new(nameof(QMeasurementRejectsIncompleteAndUnsafeWindows), QMeasurementRejectsIncompleteAndUnsafeWindows),
        new(nameof(ProjectedQMeasurementFollowsShapesTimingAndWiring), ProjectedQMeasurementFollowsShapesTimingAndWiring),
    ];
    private static AuthoredQrsMeasurement Measure(long[] microvolts, long sampleIntervalNs = 10_000_000) =>
        AuthoredQrsMeasurements.Measure(microvolts.Select(value => value * FixedPointMath.Q32One).ToArray(), sampleIntervalNs);

    private static void QMeasurementUsesInitialDeflectionAndInclusiveBoundaries()
    {
        var edge = Measure([0, -100, -100, 0, 400, -900, 0]);
        Check.That(edge.Kind == AuthoredQrsKind.QThenR && edge.QDurationNs == 30_000_000 &&
            edge.QDepthQ32 == 100 * FixedPointMath.Q32One && edge.MeetsQIllustrationCriteria,
            "inclusive 30ms and quarter-R criteria use Q rather than deeper terminal S");
        Check.That(!Measure([0, -100, -100, 0, 401, 0]).MeetsQIllustrationCriteria &&
            !Measure([0, -100, -100, 0, 400, 0], 9_000_000).MeetsQIllustrationCriteria, "both criteria are required");
        var crossing = Measure([0, 0, -100, -100, 100, 400, 0]);
        Check.That(crossing.QDurationNs == 25_000_000, "interpolate zero crossing and exclude leading baseline");
        var qs = Measure([0, -100, -500, 0]);
        Check.That(qs.Kind == AuthoredQrsKind.QS && qs.RPeakQ32 == 0 && !qs.MeetsQIllustrationCriteria,
            "QS is separate from the Q/R rule");
        Check.That(Measure([0, 200, -500, 0]).Kind == AuthoredQrsKind.RFirst &&
            Measure([0, 0, 0]).Kind == AuthoredQrsKind.Flat, "do not label terminal S or flat windows as Q");
    }

    private static void QMeasurementRejectsIncompleteAndUnsafeWindows()
    {
        foreach (var (samplesQ32, sampleIntervalNs) in new (long[], long)[]
        {
            (null!, 1), ([], 1), ([0, 0], 1), ([1, -1, 0], 1), ([0, -1, 1], 1),
            ([0, long.MinValue, 0], 1), ([0, 0, 0], 0), ([0, 0, 0], long.MaxValue), (new long[4098], 1),
        })
        {
            bool rejected = false;
            try { _ = AuthoredQrsMeasurements.Measure(samplesQ32, sampleIntervalNs); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgQrs.InvalidMeasurementWindow"; }
            Check.That(rejected, "invalid measurement windows reject without overflow or out-of-range access");
        }
    }

    private static void ProjectedQMeasurementFollowsShapesTimingAndWiring()
    {
        IReadOnlyList<ElectrodeWaveformPlan> Source(NecrosisIllustrationShape shape, int weightPermille = 1000, long qrsDurationNs = 80_000_000) =>
            TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = qrsDurationNs },
                zones: new(new(31), new(7), new(4), new(shape, -500, 200, 100, 50, weightPermille), 80_000_000));
        var full = AuthoredQrsMeasurements.Project(Source(NecrosisIllustrationShape.QWithReducedR));
        var qs = AuthoredQrsMeasurements.Project(Source(NecrosisIllustrationShape.QS));
        var shortQrs = AuthoredQrsMeasurements.Project(Source(NecrosisIllustrationShape.QWithReducedR, qrsDurationNs: 40_000_000));
        Check.That(full[8].MeetsQIllustrationCriteria && full[8].QDurationNs == 40_000_000 &&
            shortQrs[8].QDurationNs == 20_000_000 && !shortQrs[8].MeetsQIllustrationCriteria && qs[8].Kind == AuthoredQrsKind.QS,
            "measure generated regional Q, QS and shortened Q, not the template name");
        var restored = AuthoredQrsMeasurements.Project(Source(NecrosisIllustrationShape.QWithReducedR, 0));
        var reference = AuthoredQrsMeasurements.Project(TextbookElectrodeReference.CreateElectrodes());
        Check.That(restored.SequenceEqual(reference), "ST/T/delay are excluded and zero blend restores reference QRS metrics");
        bool tooShort = false;
        try { _ = AuthoredQrsMeasurements.Project(Source(NecrosisIllustrationShape.QS, qrsDurationNs: 1_000_000)); }
        catch (EventWaveformException e) { tooShort = e.ReasonCode == "EcgQrs.InsufficientResolution"; }
        Check.That(tooShort, "an unresolved sub-grid QRS must not be reported as flat");
        foreach (var placement in Enum.GetValues<EcgLimbPlacement>())
        {
            var source = Source(NecrosisIllustrationShape.QWithReducedR);
            var copy = ElectrodeWaveformComposition.Restore(new(source, [], placement)).CaptureState();
            var first = AuthoredQrsMeasurements.Project(source, placement);
            var second = AuthoredQrsMeasurements.Project(copy.Electrodes, placement);
            Check.That(first.Count == 12 && first.SequenceEqual(second), "all wired leads and restored source give deterministic metrics");
        }
    }
}
