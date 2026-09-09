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
    private sealed record Scenario(string Name, TemporalViewMode View, SessionRunState Run,
        bool NoData, bool Return = false);

    private static readonly Scenario[] Scenarios =
    [
        new("live", TemporalViewMode.LiveSweep, SessionRunState.Running, false),
        new("nodata", TemporalViewMode.LiveSweep, SessionRunState.Running, true),
        new("frozen", TemporalViewMode.FrozenSnapshot, SessionRunState.Running, false),
        new("frozen-nodata", TemporalViewMode.FrozenSnapshot, SessionRunState.Running, true),
        new("review", TemporalViewMode.HistoricalReview, SessionRunState.Running, false),
        new("review-nodata", TemporalViewMode.HistoricalReview, SessionRunState.Running, true),
        new("paused", TemporalViewMode.LiveSweep, SessionRunState.Paused, false),
        new("paused-nodata", TemporalViewMode.LiveSweep, SessionRunState.Paused, true),
        new("stopped", TemporalViewMode.LiveSweep, SessionRunState.Stopped, false),
        new("stopped-nodata", TemporalViewMode.LiveSweep, SessionRunState.Stopped, true),
        new("frozen-return", TemporalViewMode.FrozenSnapshot, SessionRunState.Running, true, true),
        new("review-return", TemporalViewMode.HistoricalReview, SessionRunState.Running, true, true),
    ];

    public static int Run(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (args.Length == 1 && args[0] == "--list-svg-fixtures")
        {
            string listing = string.Join(Environment.NewLine, Scenarios.Select(item =>
                $"{item.Name}\t{item.Run} / {item.View} / {(item.NoData ? "NoData" : "Authoritative")}" +
                    (item.Return ? " -> LiveSweep at 10500000000 ns presentation time" : "")));
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine(listing);
            return 0;
        }
        Scenario? scenario = args.Length == 2 && args[0] == "--svg-fixture"
            ? Array.Find(Scenarios, item => item.Name == args[1]) : null;
        if (scenario is null)
        {
            error.WriteLine("Usage: Monitor.Specs --list-svg-fixtures | --svg-fixture " +
                string.Join("|", Scenarios.Select(item => item.Name)));
            return 2;
        }
        EcgStripCheckpoint input = Create(scenario.NoData, scenario.View, scenario.Run, cancellationToken);
        if (scenario.Return) { input = ReturnToLive(input, 10_500_000_000); }
        string rendered = EcgStripSvgPreview.Render(input, 5000, 5000, cancellationToken);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement root = XElement.Parse(rendered);
        root.Element(svg + "title")!.Value = "Synthetic triangle geometry fixture - " + args[1];
        root.Element(svg + "desc")!.Value += " Synthetic triangles only; not a physiological ECG model.";
        string completed = root.ToString(SaveOptions.DisableFormatting);
        cancellationToken.ThrowIfCancellationRequested();
        // Cancellation fences entry to the write; it cannot roll back stream I/O.
        output.WriteLine(completed);
        return 0;
    }

    internal static EcgStripCheckpoint ReturnToLive(EcgStripCheckpoint input, long presentationNs)
    {
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        long frontier = input.Source.Frame.Presentation.LivePlayheadDataSimTimeNs;
        if (input.Source.Frame.Presentation.TemporalViewMode == TemporalViewMode.FrozenSnapshot)
        { machine.ExitFrozen(presentationNs, frontier); }
        else { machine.ExitReview(presentationNs, frontier); }
        return input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
    }

    internal static EcgStripCheckpoint Create(bool noData, TemporalViewMode view, SessionRunState run = SessionRunState.Running,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view is not (TemporalViewMode.LiveSweep or TemporalViewMode.FrozenSnapshot or TemporalViewMode.HistoricalReview))
        { throw new ArgumentOutOfRangeException(nameof(view)); }
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0);
        machine.ChangeRunState(run, 0, 0);
        if (view == TemporalViewMode.FrozenSnapshot) { machine.EnterFrozen(0, 0); }
        if (view == TemporalViewMode.HistoricalReview) { machine.EnterReview("synthetic-triangle-record", 0, 0, 0); }
        if (noData)
        {
            machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState)
                .Disconnect(false, 1), 0, 0);
            machine.Advance(10_000_000_000, 0);
        }
        else if (run != SessionRunState.Running) { machine.Advance(10_000_000_000, 0); }
        SweepFramePathState frame = new(machine.CaptureState(), 30, 500, 0, 100, null, new(0, 100, 60, 20, 1));
        SweepSampleSource source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("33333333-3333-4333-8333-333333333333"),
            1, 2, 3, 4, 5, 500, 1);
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(frame);
        List<SweepPathSample> samples = new(5000);
        for (ulong index = 0; index < 5000; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long phase = (long)(index % 500);
            long voltage = (phase <= 250 ? phase : 500 - phase) * 8 - 1000;
            builder.AppendVoltageAtOffset(source, index, 0, true, index * 2_000_000, new(voltage, 1), cancellationToken);
            samples.Add(builder.CaptureState().Previous!);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(frame, samples), 0, 5);
    }
}
