// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentilationWaveformSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PhysicalVentilationControlsSampledRespirationAndGas), PhysicalVentilationControlsSampledRespirationAndGas),
        new(nameof(DeadSpaceUsesEachBreathsDepthAndPreservesTransport), DeadSpaceUsesEachBreathsDepthAndPreservesTransport),
        new(nameof(RetimedGasRetainsSmoothJunctionsAndCompleteMorphology), RetimedGasRetainsSmoothJunctionsAndCompleteMorphology),
        new(nameof(LiveVentilationStopsAndRestoresMeasuredBreathing), LiveVentilationStopsAndRestoresMeasuredBreathing),
        new(nameof(VentilationEditsPreserveTimingAndCurrentSource), VentilationEditsPreserveTimingAndCurrentSource),
        new(nameof(VentilationEditImmediatelyAfterContinuationKeepsNewRhythm), VentilationEditImmediatelyAfterContinuationKeepsNewRhythm),
    ];

    private static readonly VentilationTransportPlan Normal = new(450000, 150000, 210000);

    private static void PhysicalVentilationControlsSampledRespirationAndGas()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { RespAmplitudeCounts = -900, RespCardiacArtifactCounts = 160 };
        short[][] legacy = Generate(config, null);
        short[][] normal = Generate(config, Normal);
        Check.That(legacy.Zip(normal).All(pair => pair.First.SequenceEqual(pair.Second)), "reference coupling preserves every acquired channel bit for bit");
        short[][] shallow = Generate(config, Normal with { TidalVolumeMicrolitersBtps = 225000 });
        short[][] zero = Generate(config, Normal with { TidalVolumeMicrolitersBtps = 0 });
        short[][] blocked = Generate(config, Normal with { AirwayOpen = false });
        Check.That(normal[1].SequenceEqual(blocked[1]) && blocked[4].All(value => value == 0),
            "closed airway keeps chest effort while removing expired gas");
        Check.That(zero[1].Any(value => value != 0) && zero[4].All(value => value == 0) &&
            normal[1].Zip(shallow[1]).Zip(zero[1]).All(pair => Math.Abs(pair.First.First + pair.Second - 2 * pair.First.Second) <= 2),
            "VT scales signed respiratory excursion while the independent cardiac artifact remains");
        Check.That(shallow[4].Zip(normal[4]).Any(pair => pair.First < pair.Second) && shallow[4].Max() == normal[4].Max(),
            "reduced VT lengthens the dead-space segment without inventing a CO2 concentration solver");
        short[][] noAlveolar = Generate(config with { Co2BaselineMmHg = 5 }, Normal with { DeadSpaceMicrolitersBtps = 450000 });
        Check.That(noAlveolar[1].SequenceEqual(normal[1]) && noAlveolar[4].All(value => value == 0),
            "VD equal to VT removes gas excursion and preserves RESP (inspired baseline is in plane offset)");
        short[][] oxygen = Generate(config, Normal with { InspiredOxygenMillionths = 1000000 });
        Check.That(normal.Zip(oxygen).All(pair => pair.First.SequenceEqual(pair.Second)), "FiO2 does not fabricate changes in chest effort or CO2");
        short[][] large = Generate(config, Normal with { TidalVolumeMicrolitersBtps = 1500000, DeadSpaceMicrolitersBtps = 0 });
        Check.That(large[1].Min() < normal[1].Min() && large[4].Any(value => value > 0), "maximum VT and zero VD remain representable");
        foreach (int row in new[] { 0, 2, 3, 5, 6 })
        { Check.That(normal[row].SequenceEqual(shallow[row]) && normal[row].SequenceEqual(zero[row]), "ventilation edits leave other signal channels unchanged"); }
    }

    private static void DeadSpaceUsesEachBreathsDepthAndPreservesTransport()
    {
        var config = PhysiologyIllustrationConfiguration.Default with
        {
            RespiratoryPattern = RespiratoryPattern.CheyneStokesIllustration,
            Co2TransportDelayMilliseconds = 300,
            Co2DispersionStepMilliseconds = 100
        };
        short[][] samples = Generate(config, Normal, 25_000_000_000);
        // First breath is 20% of 450 mL, less than 150 mL VD. Later deeper
        // breaths deliver alveolar gas; all three dispersion paths share gating.
        Check.That(samples[1].Take(3 * 125).Any(value => value != 0) && samples[4].Take(5 * 100).All(value => value == 0) &&
            samples[4].Skip(6 * 100).Any(value => value > 0), "per-breath physical depth gates gas without suppressing chest effort");
        var group = PhysiologyIllustrationSource.Create(config, ventilation: Normal);
        group.AdvanceTo(50_000_000, 50, 1, 100);
        var restored = PhysiologyWaveformGroup.Restore(group.CaptureState());
        for (long time = 100_000_000; time <= 5_000_000_000; time += 50_000_000)
        {
            var a = group.AdvanceTo(time, 50, 1, 100);
            var b = restored.AdvanceTo(time, 50, 1, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(pair => pair.First.SequenceEqual(pair.Second)), "coupled native samples survive checkpoint replay");
        }
    }

    private static void LiveVentilationStopsAndRestoresMeasuredBreathing()
    {
        var session = Session();
        session.DiscardStartup();
        AdvanceTo(session, 25_000_000_000);
        Check.That(session.Measurements!.ImpedanceRespiration.Status == WaveformMeasurementStatus.Valid &&
            session.Measurements.Capnography.EndTidalCentiMmHg.Status == WaveformMeasurementStatus.Valid, "initial respiration and capnography are measured");
        var before = session.Oxygenation;
        var readings = session.Measurements;
        var blocks = session.Blocks.ToArray();
        session.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = 0 });
        Check.That(session.Oxygenation == before && session.Measurements == readings && session.Blocks.SequenceEqual(blocks), "live edit preserves published samples and reservoir history");
        AdvanceTo(session, 50_000_000_000);
        Check.That(session.Samples(1, 40_000_000_000, 45_000_000_000).All(sample => sample.Value == 0) &&
            session.Samples(4, 40_000_000_000, 45_000_000_000).All(sample => sample.Value == 0) &&
            session.Measurements!.ImpedanceRespiration.Status != WaveformMeasurementStatus.Valid &&
            session.Measurements.Capnography.EndTidalCentiMmHg.Status != WaveformMeasurementStatus.Valid,
            "zero VT removes acquired breath signals and expires measured RR/EtCO2 through existing estimators");
        session.UpdateOxygenationVentilation(Normal);
        AdvanceTo(session, 75_000_000_000);
        Check.That(session.Samples(1, 65_000_000_000, 70_000_000_000).Any(sample => sample.Value > 0) &&
            session.Samples(4, 65_000_000_000, 70_000_000_000).Any(sample => sample.Value > 0) &&
            session.Measurements!.ImpedanceRespiration.Status == WaveformMeasurementStatus.Valid &&
            session.Measurements.Capnography.EndTidalCentiMmHg.Status == WaveformMeasurementStatus.Valid, "restoring ventilation restores both waveforms and measured values");
    }

    private static void RetimedGasRetainsSmoothJunctionsAndCompleteMorphology()
    {
        var spontaneous = PhysiologyIllustrationConfiguration.Default with
        {
            CapnogramIllustration = CapnogramIllustration.SpontaneousVentilation,
            Co2DeadSpaceMilliseconds = 500
        };
        var ventilation = Normal with { DeadSpaceMicrolitersBtps = 300000 };
        var source = GasState(spontaneous, ventilation);
        var curve = EventWaveformComposition.Restore(new(source.Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));
        const long junctionNs = 1_250_000_000;
        long at = curve.EvaluateAt(junctionNs);
        long incoming = at - curve.EvaluateAt(junctionNs - 1_000_000);
        long outgoing = curve.EvaluateAt(junctionNs + 1_000_000) - at;
        Check.That(incoming > 0 && outgoing > 0 && Math.Abs(incoming - outgoing) * 100 < Math.Max(incoming, outgoing) * 8,
            "changed dead space rebuilds physical-time slopes across the rise/plateau junction");

        foreach (var illustration in new[] { CapnogramIllustration.Reference, CapnogramIllustration.SpontaneousVentilation,
            CapnogramIllustration.IntermittentMandatoryVentilation })
        {
            var configuration = spontaneous with
            {
                CapnogramIllustration = illustration,
                Co2PlateauStartCentiMmHg = illustration == CapnogramIllustration.Reference ? 3200 : null,
                Co2TransportDelayMilliseconds = 300,
                Co2DispersionStepMilliseconds = 100
            };
            var actual = GasState(configuration, ventilation);
            var expectedPlan = configuration.ResolveCapnogram() with { DeadSpaceNs = 1_000_000_000 };
            var expected = expectedPlan.CreateChannel(configuration.ResolvePlan(), PhysiologyIllustrationSource.ChannelId(4), 0).Bands;
            Check.That(SameBands(actual.Bands, expected),
                "retiming preserves every transport path, IMV branch and explicit plateau while rebuilding its contour");
            Check.That(SameBands(GasState(configuration, Normal).Bands, GasState(configuration, null).Bands),
                "reference ventilation remains bit-identical for shaped and dispersed capnograms");
        }

        static PhysiologySignalState GasState(PhysiologyIllustrationConfiguration configuration, VentilationTransportPlan? ventilation) =>
            PhysiologyIllustrationSource.Create(configuration, ventilation: ventilation).CaptureState().Channels
                .Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator;

        static bool SameBands(IReadOnlyList<EventWaveformBand> left, IReadOnlyList<EventWaveformBand> right) =>
            left.Count == right.Count && left.Zip(right).All(pair =>
                pair.First.DelayNs == pair.Second.DelayNs && pair.First.DurationNs == pair.Second.DurationNs &&
                pair.First.TableQ32.SequenceEqual(pair.Second.TableQ32) &&
                (pair.First.PhasePoints ?? []).SequenceEqual(pair.Second.PhasePoints ?? []) &&
                (pair.First.ExpirationCycleGainsPermille ?? []).SequenceEqual(pair.Second.ExpirationCycleGainsPermille ?? []));
    }

    private static void VentilationEditsPreserveTimingAndCurrentSource()
    {
        var large = Session();
        var small = Session();
        foreach (var session in new[] { large, small })
        {
            session.Advance(50_000_000);
            Check.That(session.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = 0 }) == 56_000_000, "waveform and oxygenation edits share the 8ms effective boundary");
            session.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = 225000 });
            try { session.UpdateOxygenationVentilation(Normal with { DeadSpaceMicrolitersBtps = -1 }); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Expected invalid ventilation rejection.");
        }
        AdvanceTo(large, 20_000_000_000);
        AdvanceTo(small, 20_000_000_000, 17_000_000);
        Check.That(large.Oxygenation == small.Oxygenation && Enumerable.Range(0, 7).All(row =>
            large.Samples(row, 0, 17_000_000_000).SequenceEqual(small.Samples(row, 0, 17_000_000_000))),
            "replacement and rejected edits preserve deterministic sampling across frame subdivisions");
        var changed = PhysiologyIllustrationConfiguration.Default with { BreathPeriodMilliseconds = 6000, RespAmplitudeCounts = -700, Co2EndExpiratoryMmHg = 55 };
        large.ScheduleSource(Session(changed), 0);
        large.Advance(50_000_000);
        large.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = 225000 });
        AdvanceTo(large, 45_000_000_000);
        double[] resp = large.Samples(1, 30_000_000_000, 40_000_000_000).Select(sample => sample.Value).ToArray();
        Check.That(resp.Min() is >= -351 and <= -349 && resp.Max() <= 0 &&
            large.Measurements!.Capnography.RespirationsMilliPerMinute.Value is >= 9900 and <= 10100,
            "live ventilation uses the active continued source, preserving new polarity, magnitude and respiratory rate");
        large.ScheduleSource(new(changed, MonitorDisplayConfiguration.Default(), true), 0);
        large.Advance(50_000_000);
        AdvanceTo(large, 65_000_000_000);
        Check.That(large.Oxygenation is null && large.Samples(1, 55_000_000_000, 60_000_000_000).Min(sample => sample.Value) <= -699,
            "disabling realtime mode restores the authored respiratory amplitude");
    }

    private static void VentilationEditImmediatelyAfterContinuationKeepsNewRhythm()
    {
        var changed = PhysiologyIllustrationConfiguration.Default with { BreathPeriodMilliseconds = 6000, InspirationMilliseconds = 3000 };
        var continued = Session();
        var reference = Session(changed);
        continued.ScheduleSource(Session(changed), 0);
        foreach (var session in new[] { continued, reference })
        {
            session.Advance(1_000_000);
            var before = session.Oxygenation;
            session.UpdateOxygenationVentilation(Normal with { TidalVolumeMicrolitersBtps = 225000 }, 2);
            Check.That(session.Oxygenation == before, "an edit inside the first solver interval preserves the published snapshot");
            AdvanceTo(session, 20_000_000_000);
        }
        bool SameWaveform(int row) => continued.Samples(row, 0, 17_000_000_000)
            .SequenceEqual(reference.Samples(row, 0, 17_000_000_000));
        Check.That(continued.Oxygenation == reference.Oxygenation && SameWaveform(1) && SameWaveform(4),
            "a live edit before the first 8ms step preserves the continued rhythm in both oxygen transport and waveforms");
    }

    private static LocalMonitorPreviewSession Session(PhysiologyIllustrationConfiguration? configuration = null) =>
        new(configuration ?? PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true,
            realtimeOxygenation: RealtimeOxygenationConfiguration.ReferenceAdult);

    private static void AdvanceTo(LocalMonitorPreviewSession session, long targetNs, long stepNs = 250_000_000)
    {
        while (session.SimulationTimeNs < targetNs) { session.Advance(Math.Min(stepNs, targetNs - session.SimulationTimeNs)); }
    }

    private static short[][] Generate(PhysiologyIllustrationConfiguration configuration, VentilationTransportPlan? ventilation,
        long endNs = 12_000_000_000)
    {
        var source = PhysiologyIllustrationSource.Create(configuration, ventilation: ventilation);
        List<short>[] samples = Enumerable.Range(0, 7).Select(_ => new List<short>()).ToArray();
        for (long time = 50_000_000; time <= endNs; time += 50_000_000)
        {
            foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
            {
                var block = WaveformEnvelopeCodec.Decode(wire);
                for (int row = 0; row < samples.Length; row++)
                { samples[row].AddRange(block.Planes.Single(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Samples); }
            }
        }
        return samples.Select(values => values.ToArray()).ToArray();
    }
}
