// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class LiveEcg12Specifications
{
    internal static readonly Specification[] All =
    [
        new(nameof(TwelveLeadsPublishOnTheLiveSampleClock), TwelveLeadsPublishOnTheLiveSampleClock),
        new(nameof(DisplayLeadSelectionPreservesAcquisitionAndOtherRows), DisplayLeadSelectionPreservesAcquisitionAndOtherRows),
        new(nameof(TwelveLeadsRetainHistoryAcrossSourceChanges), TwelveLeadsRetainHistoryAcrossSourceChanges),
        new(nameof(TwelveLeadsFollowPacingAndShockRecovery), TwelveLeadsFollowPacingAndShockRecovery),
        new(nameof(TwelveLeadProjectionPreservesExactElectrodeValues), TwelveLeadProjectionPreservesExactElectrodeValues)
    ];

    private static LocalMonitorPreviewSession Session(PhysiologyIllustrationConfiguration? configuration = null,
        EcgElectricalTherapyProfile? therapy = null) => new(configuration ?? PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), electricalTherapy: therapy, enableTwelveLead: true);

    private static void Advance(LocalMonitorPreviewSession session, long toNs, long stepNs = 50_000_000)
    {
        while (session.SimulationTimeNs < toNs) { session.Advance(Math.Min(stepNs, toNs - session.SimulationTimeNs)); }
    }

    private static void TwelveLeadsPublishOnTheLiveSampleClock()
    {
        var session = Session();
        session.DiscardStartup();
        foreach (var lead in Enum.GetValues<EcgLead>())
        { Check.That(!session.EcgSamples(lead, 0, long.MaxValue).Any(), "startup publishes no lead history"); }
        AssertProjectedHistoryRanges(session);
        session.Advance(16_000_000);
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            Check.That(session.EcgSamples(lead, 0, long.MaxValue).Select(sample => sample.TimeNs)
                .SequenceEqual(new long[] { 0, 4_000_000, 8_000_000, 12_000_000 }), "all leads publish before a recording packet closes");
        }
        AssertProjectedHistoryRanges(session);
        Advance(session, 900_000_000);
        Check.That(session.EcgSamples(EcgLead.II, 0, long.MaxValue).SequenceEqual(session.Samples(0, 0, long.MaxValue)),
            "short and long II use the monitor samples exactly");
        var first = session.EcgSamples(EcgLead.I, 0, long.MaxValue).ToArray();
        var second = session.EcgSamples(EcgLead.II, 0, long.MaxValue).ToArray();
        var third = session.EcgSamples(EcgLead.III, 0, long.MaxValue).ToArray();
        Check.That(first.Zip(second).Zip(third).All(sample =>
            Math.Abs(sample.First.First.Value + sample.Second.Value - sample.First.Second.Value) <= 1),
            "canonical monitor II satisfies the projected limb identity after quantization");
        foreach (var block in session.Blocks)
        {
            var plane = block.Planes.Single(item => item.ChannelId == PhysiologyIllustrationSource.ChannelId(0));
            Check.That(plane.Samples.Select(value => (double)value).SequenceEqual(session.Samples(0, block.StartSimTimeNs,
                block.StartSimTimeNs + block.DurationNs).Select(sample => sample.Value)), "recording II equals the immediate stream");
        }
        foreach (var sample in session.EcgSamples(EcgLead.V2, 0, 900_000_000))
        { Check.That(sample.TimeNs < session.SimulationTimeNs, "no future samples are published"); }
    }

    private static void DisplayLeadSelectionPreservesAcquisitionAndOtherRows()
    {
        var display = MonitorDisplayConfiguration.Default();
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, display,
            enableMeasurements: true, enableTwelveLead: true);
        var reference = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, display,
            enableMeasurements: true, enableTwelveLead: true);
        Advance(session, 12_000_000_000);
        Advance(reference, 12_000_000_000);
        var records = session.Blocks;
        var measurement = session.Measurements;
        var beats = session.DetectedBeats;
        var ranges = Enumerable.Range(1, display.Slots.Count - 1)
            .Select(row => (session.Ranges.Range(row), session.Ranges.PreviousRange(row), session.Ranges.RowCycle(row))).ToArray();
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            session.SelectDisplayEcgLead(lead);
            Check.That(session.DisplaySamples(0, 0, long.MaxValue).SequenceEqual(session.EcgSamples(lead, 0, long.MaxValue)),
                "every selectable lead displays its own retained waveform");
            Check.That(session.Samples(0, 0, long.MaxValue).SequenceEqual(reference.Samples(0, 0, long.MaxValue)) &&
                records.SequenceEqual(session.Blocks) && measurement == session.Measurements && ReferenceEquals(beats, session.DetectedBeats),
                "lead selection does not rewrite acquisition, recordings or detection evidence");
            Check.That(ranges.SequenceEqual(Enumerable.Range(1, display.Slots.Count - 1)
                .Select(row => (session.Ranges.Range(row), session.Ranges.PreviousRange(row), session.Ranges.RowCycle(row)))),
                "other waveforms retain current and previous auto ranges");
            Check.That(session.EcgSamples(lead, 0, 10_000_000_000).All(sample =>
                sample.Value >= session.Ranges.Range(0).Minimum && sample.Value <= session.Ranges.Range(0).Maximum),
                "ECG auto range follows the selected lead's completed sweep");
        }
        session.SelectDisplayEcgLead(EcgLead.V1);
        Advance(session, 13_000_000_000);
        Advance(reference, 13_000_000_000);
        Check.That(session.Measurements == reference.Measurements, "continued measurement still uses the same II stream");
        var manual = display.Slots.Select(slot => slot.Channel == 0 ? slot with { Automatic = false, Range = new(-3000, 3000) } : slot).ToArray();
        session.UpdateDisplay(new(display.Skin, manual));
        session.SelectDisplayEcgLead(EcgLead.AVR);
        Check.That(session.Ranges.Range(0) == new MonitorAmplitudeRange(-3000, 3000), "manual ECG range survives lead switching");
        try { session.SelectDisplayEcgLead((EcgLead)99); throw new InvalidOperationException("invalid lead accepted"); }
        catch (ArgumentOutOfRangeException) { }
        Check.That(session.DisplayEcgLead == EcgLead.AVR, "invalid selection leaves current lead intact");
        session.ScheduleSource(new(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent },
            session.Display, enableMeasurements: true), 0);
        Advance(session, 15_000_000_000);
        Check.That(session.DisplayEcgLead == EcgLead.AVR && session.DisplaySamples(0, 14_000_000_000, 15_000_000_000).All(sample => sample.Value == 0),
            "source transitions preserve the display lead and publish its new activity");
    }

    private static void TwelveLeadsRetainHistoryAcrossSourceChanges()
    {
        var session = Session();
        var partitioned = Session();
        Advance(session, 1_000_000_000);
        Advance(partitioned, 1_000_000_000, 16_000_000);
        var before = session.EcgSamples(EcgLead.V3, 0, long.MaxValue).ToArray();
        var changed = PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent };
        long boundary = session.ScheduleSource(new(changed, session.Display), 200_000_000);
        partitioned.ScheduleSource(Session(changed), 200_000_000);
        Advance(session, 3_000_000_000);
        Advance(partitioned, 3_000_000_000, 16_000_000);
        AssertProjectedHistoryRanges(session);
        Check.That(session.EcgSamples(EcgLead.V3, 0, 1_000_000_000).SequenceEqual(before), "source changes preserve old strips");
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            var samples = session.EcgSamples(lead, 0, long.MaxValue).ToArray();
            Check.That(samples.SequenceEqual(partitioned.EcgSamples(lead, 0, long.MaxValue)), "source transitions are independent of frame partition");
            Check.That(samples.Length == 750 && samples[^1].TimeNs == 2_996_000_000, "no missing or duplicated transition samples");
            Check.That(session.EcgSamples(lead, boundary + 1_000_000_000, 3_000_000_000).All(sample => sample.Value == 0),
                "absent activity reaches every lead after triggered tails finish");
        }
        Advance(session, 42_000_000_000);
        Check.That(session.EcgSamples(EcgLead.V3, 0, long.MaxValue).First().TimeNs >=
            session.SimulationTimeNs - LocalMonitorPreviewSession.RetainedBlockCount * 200_000_000L, "lead history is bounded");
        AssertProjectedHistoryRanges(session);
    }

    private static void AssertProjectedHistoryRanges(LocalMonitorPreviewSession session)
    {
        foreach (var lead in new[] { EcgLead.I, EcgLead.AVR, EcgLead.V3 })
        {
            var history = session.EcgSamples(lead, long.MinValue, long.MaxValue).ToArray();
            Check.That(history.Zip(history.Skip(1)).All(pair => pair.First.TimeNs < pair.Second.TimeNs),
                "projected sample times stay ordered across source transitions and retention");
            long first = history.Length == 0 ? 0 : history[0].TimeNs;
            long last = history.Length == 0 ? 0 : history[^1].TimeNs;
            foreach (var (fromNs, toNs) in new (long, long)[]
            {
                (long.MinValue, first), (first, first), (first, first + 1), (first + 1, last),
                (last, last + 1), (last + 1, long.MaxValue), (last + 1, first),
                (first + 1, last - 1), (long.MinValue, long.MaxValue)
            })
            {
                Check.That(session.EcgSamples(lead, fromNs, toNs).SequenceEqual(
                    history.Where(sample => sample.TimeNs >= fromNs && sample.TimeNs < toNs)),
                    "projected queries preserve inclusive start and exclusive end, including empty and unaligned ranges");
            }
        }
    }

    private static void TwelveLeadsFollowPacingAndShockRecovery()
    {
        var therapy = new EcgElectricalTherapyProfile(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default)
        { PacingAllowed = true };
        var session = Session(therapy: therapy);
        Advance(session, 800_000_000);
        session.SchedulePacing(PacingIllustration.RightVentricularVvi, new(90, 100));
        Advance(session, 2_000_000_000);
        Check.That(session.ActivePacing is not null && session.EcgSamples(EcgLead.V1, 1_000_000_000, 2_000_000_000)
            .Any(sample => sample.Value != 0), "paced activity reaches projected leads");
        session.StopPacing();
        Advance(session, 3_000_000_000);
        var shock = new DeliveredElectricalShock(1, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 200);
        session.ApplyElectricalShock(shock, session.PrepareSinusAfterShock(therapy));
        session.Advance(16_000_000);
        var reference = session.EcgSamples(EcgLead.II, 3_000_000_000, 3_016_000_000).ToArray();
        foreach (var lead in Enum.GetValues<EcgLead>())
        { Check.That(session.EcgSamples(lead, 3_000_000_000, 3_016_000_000).SequenceEqual(reference), "shock artifact appears on every lead immediately"); }
        var secondShock = shock with
        {
            DeliverySequence = 2,
            DeliveredAtSimTimeNs = session.SimulationTimeNs,
            Waveform = DefibrillationWaveformKind.MonophasicTruncatedExponential
        };
        session.ApplyElectricalShock(secondShock, session.PrepareSinusAfterShock(therapy));
        session.Advance(16_000_000);
        var latest = session.ShockArtifacts[^1];
        foreach (var lead in new[] { EcgLead.I, EcgLead.V4 })
        {
            Check.That(session.EcgSamples(lead, 3_000_000_000, 3_016_000_000).SequenceEqual(reference),
                "a later overlapping shock preserves samples before its onset");
            Check.That(session.EcgSamples(lead, 3_016_000_000, 3_032_000_000).All(sample =>
                sample.Value == latest.EvaluateMicrovolts(sample.TimeNs)),
                "the latest containing shock overrides earlier recovery on projected leads");
        }
        Advance(session, 5_000_000_000);
        Check.That(session.ActivePacing is null && session.EcgSamples(EcgLead.V4, 4_000_000_000, 5_000_000_000)
            .Any(sample => sample.Value != 0), "live leads recover after therapy");
    }

    private static void TwelveLeadProjectionPreservesExactElectrodeValues()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        var electrodes = TextbookElectrodeReference.CreateElectrodes();
        var live = new LiveEcgProjection(plan, electrodes);
        var reference = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        for (long time = 50_000_000; time <= 1_000_000_000; time += 50_000_000)
        {
            var actual = live.GenerateBefore(time, 50, 100);
            var expected = reference.GenerateBefore(time, 50, 100);
            Check.That(actual.Zip(expected).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "projection rounds once after electrode subtraction");
        }
    }
}
