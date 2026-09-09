// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Specs;

// Developer fixture only: these triangles are not a physiological ECG model.
internal static class SvgFixtureCommand
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || args[0] != "--svg-fixture" ||
            args[1] is not ("live" or "nodata" or "frozen" or "frozen-nodata" or "review" or "review-nodata"))
        {
            error.WriteLine("Usage: Monitor.Specs --svg-fixture live|nodata|frozen|frozen-nodata|review|review-nodata");
            return 2;
        }
        TemporalViewMode view = args[1] switch
        {
            "frozen" or "frozen-nodata" => TemporalViewMode.FrozenSnapshot,
            "review" or "review-nodata" => TemporalViewMode.HistoricalReview,
            _ => TemporalViewMode.LiveSweep,
        };
        string rendered = EcgStripSvgPreview.Render(
            Create(args[1] is "nodata" or "frozen-nodata" or "review-nodata", view), 5000, 5000);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement root = XElement.Parse(rendered);
        root.Element(svg + "title")!.Value = "Synthetic triangle geometry fixture - " + args[1];
        root.Element(svg + "desc")!.Value += " Synthetic triangles only; not a physiological ECG model.";
        output.WriteLine(root.ToString(SaveOptions.DisableFormatting));
        return 0;
    }

    internal static EcgStripCheckpoint Create(bool noData, TemporalViewMode view)
    {
        if (view is not (TemporalViewMode.LiveSweep or TemporalViewMode.FrozenSnapshot or TemporalViewMode.HistoricalReview))
        { throw new ArgumentOutOfRangeException(nameof(view)); }
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0);
        if (view == TemporalViewMode.FrozenSnapshot) { machine.EnterFrozen(0, 0); }
        if (view == TemporalViewMode.HistoricalReview) { machine.EnterReview("synthetic-triangle-record", 0, 0, 0); }
        if (noData)
        {
            machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState)
                .Disconnect(false, 1), 0, 0);
            machine.Advance(10_000_000_000, 0);
        }
        SweepFramePathState frame = new(machine.CaptureState(), 30, 500, 0, 100, null, new(0, 100, 60, 20, 1));
        SweepSampleSource source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("33333333-3333-4333-8333-333333333333"),
            1, 2, 3, 4, 5, 500, 1);
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(frame);
        List<SweepPathSample> samples = new(5000);
        for (ulong index = 0; index < 5000; index++)
        {
            long phase = (long)(index % 500);
            long voltage = (phase <= 250 ? phase : 500 - phase) * 8 - 1000;
            builder.AppendVoltageAtOffset(source, index, 0, true, index * 2_000_000, new(voltage, 1));
            samples.Add(builder.CaptureState().Previous!);
        }
        return new(new(frame, samples), 0, 5);
    }
}
