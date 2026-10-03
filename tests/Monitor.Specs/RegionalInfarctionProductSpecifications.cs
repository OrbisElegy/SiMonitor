// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RegionalInfarctionProductSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(IndependentZonesShareProjectionAndRestore), IndependentZonesShareProjectionAndRestore),
        new(nameof(RegionalSnapshotsPreserveQrsDetectionAndNonEcgChannels), RegionalSnapshotsPreserveQrsDetectionAndNonEcgChannels),
        new(nameof(RegionalSnapshotsMatchProjectionAndRejectConflicts), RegionalSnapshotsMatchProjectionAndRejectConflicts),
    ];
    private static void IndependentZonesShareProjectionAndRestore()
    {
        var zones = new EcgInfarctionZones(new(0, InfarctionTerritory.Inferior), new(2), new(0, InfarctionTerritory.Lateral),
            new(NecrosisIllustrationShape.QWithReducedR, -400, 200, 100, 150, 600), 80_000_000);
        var config = PhysiologyIllustrationConfiguration.Default with { Zones = zones };
        var plan = config.ResolvePlan();
        Check.That(plan == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "independent zones retain event clocks");
        var projection = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(zones: zones)).GenerateBefore(800_000_000, 200, 100);
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, zones.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(projection.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[(int)EcgLead.II] - p.Second.NormalizedValue) <= 1), "zone monitor is projected paper II");
        var source = PhysiologyIllustrationSource.Create(config);
        var restored = PhysiologyIllustrationSource.Create(config);
        var baseline = PhysiologyIllustrationSource.Create();
        bool changed = false;
        for (int step = 1; step <= 30; step++)
        {
            var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            var recovered = restored.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            var reference = baseline.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            Check.That(actual.Count == recovered.Count && actual.Zip(recovered).All(p => p.First.SequenceEqual(p.Second)), "zone source restores exact acquisition bytes");
            foreach (var pair in actual.Zip(reference))
            {
                var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                foreach (var plane in a.Planes)
                {
                    bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                    if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                    else { Check.That(equal, "independent zones preserve mechanical and respiratory channels"); }
                }
            }
            restored = PhysiologyWaveformGroup.Restore(restored.CaptureState());
        }
        Check.That(changed, "mixed zones change the actual monitor ECG");
        foreach (var invalid in new[] { config with { Infarction = Snapshots().First() }, config with { TContour = new(1, EcgTContourShape.Notched, 300) },
            config with { Svt = true }, config with { VentricularConductionRatio = 2 }, config with { DigitalisEffect = true },
            config with { Zones = zones with { Ischemia = new(64) } } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "conflicting or invalid zones reject before construction");
        }
    }
    private static IEnumerable<EcgChestInfarctionPlan> Snapshots() =>
        new[] { InfarctionTerritory.Inferior, InfarctionTerritory.Lateral, InfarctionTerritory.Anteroseptal, InfarctionTerritory.Anterior, InfarctionTerritory.ExtensiveAnterior }.SelectMany(territory =>
            Enum.GetValues<InfarctionIllustrationStage>().Where(stage => stage != InfarctionIllustrationStage.None)
                .Select(stage => new EcgChestInfarctionPlan(0, stage, territory)));

    private static void RegionalSnapshotsPreserveQrsDetectionAndNonEcgChannels()
    {
        var reference = PhysiologyIllustrationSource.Create();
        var referenceBlocks = Enumerable.Range(1, 110)
            .Select(step => reference.AdvanceTo(step * 200_000_000L, 50, 1, 100)).ToArray();
        foreach (var shape in Snapshots())
        {
            var config = PhysiologyIllustrationConfiguration.Default with { Infarction = shape };
            Check.That(config.ResolvePlan() == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "QRS shape does not shift atrial/ventricular event clocks");
            var source = PhysiologyIllustrationSource.Create(config);
            var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            EcgHeartRateMeasurement? restored = null;
            int count = 0; bool changed = false;
            for (int step = 1; step <= 110; step++)
            {
                var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var expected = referenceBlocks[step - 1];
                Check.That(actual.Count == expected.Count, "regional snapshot retains sample frontiers");
                foreach (var pair in actual.Zip(expected))
                {
                    var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    foreach (var plane in a.Planes)
                    {
                        bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                        if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                        else { Check.That(equal, "regional snapshot does not invent altered mechanics or respiration"); }
                    }
                    var events = detector.Consume(pair.First); count += events.Count;
                    Check.That(events.All(e => e.PeakTimeNs % 800_000_000 >= 160_000_000 &&
                        e.PeakTimeNs % 800_000_000 < 260_000_000), "detected peaks stay inside QRS, not secondary T");
                    long end = a.StartSimTimeNs + 196_000_000; var reading = detector.Read(end);
                    if (restored is not null)
                    { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "QRS candidate restores exactly"); }
                    restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                    if (end > 3_000_000_000)
                    { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 75000, "regional snapshot preserves75bpm sample rate"); }
                }
                if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
            }
            Check.That(count == 25, "one QRS per cycle remains detected");
            if (shape.Territory == InfarctionTerritory.Inferior)
            { Check.That(changed, "inferior morphology changes monitored II"); }
        }
    }
    private static void RegionalSnapshotsMatchProjectionAndRejectConflicts()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (var shape in Snapshots())
        {
            var projected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(infarction: shape)).GenerateBefore(800_000_000, 200, 100);
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                shape.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
            Check.That(projected.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[(int)EcgLead.II] - p.Second.NormalizedValue) <= 1), "monitor II is LL minus RA of the paper electrodes");
            int mask = shape.Territory switch
            {
                InfarctionTerritory.Inferior => 0,
                InfarctionTerritory.Lateral => 48,
                InfarctionTerritory.Anteroseptal => 7,
                InfarctionTerritory.Anterior => 28,
                _ => 31
            };
            for (int chest = 0; chest < 6; chest++)
            {
                int lead = (int)EcgLead.V1 + chest;
                bool selected = (mask & (1 << chest)) != 0;
                Check.That(selected ? projected.Zip(baseline).Any(p => Math.Abs(p.First.MicrovoltValues[lead] - p.Second.MicrovoltValues[lead]) > 10) :
                    projected.Zip(baseline).All(p => Math.Abs(p.First.MicrovoltValues[lead] - p.Second.MicrovoltValues[lead]) <= 1),
                    "regional snapshot changes selected chest leads and preserves the rest");
            }
            if (shape.Territory >= InfarctionTerritory.Anteroseptal)
            { Check.That(projected.Zip(baseline).All(p => Enumerable.Range(0, 6).All(lead => p.First.MicrovoltValues[lead] == p.Second.MicrovoltValues[lead])), "chest-only regions preserve all limb leads"); }
            if (shape.Territory == InfarctionTerritory.Lateral)
            {
                foreach (var lead in new[] { EcgLead.I, EcgLead.AVL })
                { Check.That(projected.Zip(baseline).Any(pair => Math.Abs(pair.First.MicrovoltValues[(int)lead] - pair.Second.MicrovoltValues[(int)lead]) > 10), "lateral snapshot changes targeted limb leads"); }
            }
        }
        var config = PhysiologyIllustrationConfiguration.Default with { Infarction = Snapshots().First() };
        foreach (var invalid in new[] { config with { Infarction = Snapshots().First() with { Stage = (InfarctionIllustrationStage)99 } },
            config with { Infarction = Snapshots().First() with { Territory = (InfarctionTerritory)99 } },
            config with { TContour = new(1, EcgTContourShape.Notched, 300) },
            config with { VentricularShape = EcgVentricularIllustration.LeftHypertrophyWithStrain },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true },
            config with { Wpw = true }, config with { AtrialShape = EcgAtrialIllustration.LeftAtrialAbnormality },
            config with { SeededRate = new SeededCardiacRate(75, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible regional snapshot combination rejects before construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
