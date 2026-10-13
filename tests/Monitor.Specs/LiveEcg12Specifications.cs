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
        session.Advance(16_000_000);
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            Check.That(session.EcgSamples(lead, 0, long.MaxValue).Select(sample => sample.TimeNs)
                .SequenceEqual(new long[] { 0, 4_000_000, 8_000_000, 12_000_000 }), "all leads publish before a recording packet closes");
        }
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
