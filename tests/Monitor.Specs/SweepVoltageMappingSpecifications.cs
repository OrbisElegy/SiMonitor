// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepVoltageMappingSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VoltageAppendUsesDeclaredScaleAndPreservesFractions), VoltageAppendUsesDeclaredScaleAndPreservesFractions),
        new(nameof(InvalidVoltageEvidencePreservesSharedFrontier), InvalidVoltageEvidencePreservesSharedFrontier),
        new(nameof(ReconstructionRevalidatesVoltageEvidenceOnRestore), ReconstructionRevalidatesVoltageEvidenceOnRestore),
        new(nameof(VoltageCancellationAndGenericPathsRemainIndependent), VoltageCancellationAndGenericPathsRemainIndependent),
    ];

    private static SweepFramePathState Frame()
    {
        SweepStateProjectionState state = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10, 2, 12), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState();
        return new(state, 0, 10, 0, 100, null, new(0, 100, 60, 20, 1));
    }

    private static SweepSampleSource Source => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1);

    private static SweepPathSample Sample(ulong index, int x, EcgSampleVoltage voltage) => new(
        Source, index, 0, true, new(new(x, 0, 1), EcgVerticalGeometry.MapMicrovolts(
            Frame().VerticalScale!, voltage.NumeratorMicrovolts, voltage.Denominator)), voltage);

    private static void VoltageAppendUsesDeclaredScaleAndPreservesFractions()
    {
        var builder = SweepFramePathBuilder.Restore(Frame());
        builder.AppendVoltage(Source, 0, 0, true, new(2, 0, 1), new(1000, 1));
        Check.That(builder.CaptureState().Previous!.Point.Y == new EcgVerticalPosition(40, 1, VerticalPlotRelation.WithinPlot),
            "one mV uses exactly the declared calibration gain");
        builder.AppendVoltage(Source, 1, 0, true, new(3, 0, 1), new(1, 3));
        Check.That(builder.CaptureState().Previous!.Point.Y == new EcgVerticalPosition(8999, 150, VerticalPlotRelation.WithinPlot),
            "fractional microvolts survive mapping and checkpoint capture");
        builder.AppendVoltage(Source, 2, 0, true, new(4, 0, 1), new(4000, 1));
        Check.That(builder.CaptureState().Previous!.Point.Y == new EcgVerticalPosition(-20, 1, VerticalPlotRelation.AbovePlot),
            "out-of-plot voltage stays unclamped for exact segment clipping");
    }

    private static void InvalidVoltageEvidencePreservesSharedFrontier()
    {
        var builder = SweepFramePathBuilder.Restore(Frame());
        builder.Append(Sample(0, 2, new(1000, 1)));
        SweepFramePathState before = builder.CaptureState();
        SweepPathSample valid = Sample(1, 3, new(1000, 1));
        Check.That(Reason(() => builder.Append(valid with { Voltage = null })) == "SweepFrame.VoltageEvidenceRequired" &&
            Reason(() => builder.Append(valid with { Voltage = new(1, 0) })) == "SweepFrame.InvalidVoltageAmplitude" &&
            Reason(() => builder.Append(valid with { Point = valid.Point with { Y = new(41, 1, VerticalPlotRelation.WithinPlot) } })) ==
                "SweepFrame.VoltageMappingMismatch" &&
            Reason(() => builder.Append(valid with { Drawable = false, Voltage = new(2000, 1) })) == "SweepFrame.VoltageMappingMismatch" &&
            Reason(() => builder.AppendVoltage(Source, 1, 0, true, new(3, 0, 1), new(1, 0))) == "EcgGeometry.InvalidAmplitude",
            "missing, forged, suppressed and invalid voltage evidence is rejected before frontier mutation");
        Check.That(builder.CaptureState() == before, "all rejected appends preserve the shared predecessor");
        Check.That(Reason(() => SweepFramePathBuilder.Restore(before with
        {
            Previous = before.Previous! with { Voltage = new(2000, 1) },
        })) == "SweepFrame.InvalidCheckpoint", "predecessor restore rechecks its voltage mapping");
    }

    private static void ReconstructionRevalidatesVoltageEvidenceOnRestore()
    {
        SweepFrameReconstructionInput input = new(Frame(), new[] { Sample(0, 2, new(0, 1)), Sample(1, 3, new(1000, 1)) });
        SweepFrameReconstructor reconstructor = new(2, 2);
        ReconstructedSweepFrame first = reconstructor.Replace(input);
        SweepFrameReconstructionInput checkpoint = reconstructor.CaptureCheckpoint()!;
        var restored = SweepFrameReconstructor.Restore(2, 2, checkpoint);
        Check.That(first.Segments.Count == 1 && first.Segments.SequenceEqual(restored.Current!.Segments) &&
            checkpoint.Samples[1].Voltage == new EcgSampleVoltage(1000, 1),
            "completed frame restore regenerates paths from owned voltage evidence");
        SweepFrameReconstructionInput corrupt = checkpoint with
        {
            Samples = new[] { checkpoint.Samples[0], checkpoint.Samples[1] with { Voltage = new(2000, 1) } },
        };
        Check.That(Reason(() => reconstructor.Replace(corrupt)) == "SweepFrame.VoltageMappingMismatch" &&
            ReferenceEquals(first, reconstructor.Current) && ReferenceEquals(checkpoint, reconstructor.CaptureCheckpoint()) &&
            Reason(() => SweepFrameReconstructor.Restore(2, 2, corrupt)) == "FrameReconstruction.InvalidCheckpoint",
            "late mapping failure leaves completed geometry and checkpoint intact and cannot survive restore");
    }

    private static void VoltageCancellationAndGenericPathsRemainIndependent()
    {
        var builder = SweepFramePathBuilder.Restore(Frame());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            builder.AppendVoltage(Source, 0, 0, true, new(2, 0, 1), new(1000, 1), cancellation.Token);
            throw new InvalidOperationException("cancelled append accepted");
        }
        catch (OperationCanceledException exception)
        {
            Check.That(exception.CancellationToken == cancellation.Token && builder.CaptureState().Previous is null,
                "cancelled voltage mapping retains the original token and empty frontier");
        }
        var generic = SweepFramePathBuilder.Restore(Frame() with { VerticalScale = null });
        Check.That(Reason(() => generic.AppendVoltage(Source, 0, 0, true, new(2, 0, 1), new(1, 1))) == "SweepFrame.VoltageScaleRequired",
            "voltage append cannot invent an absent scale");
        generic.Append(Sample(0, 2, new(0, 1)) with { Voltage = null });
        Check.That(generic.CaptureState().Previous is not null, "generic unscaled pixel paths remain supported");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepFramePathException exception) { return exception.ReasonCode; }
        catch (EcgVerticalGeometryException exception) { return exception.ReasonCode; }
        catch (SweepFrameReconstructionException exception) { return exception.ReasonCode; }
    }
}
