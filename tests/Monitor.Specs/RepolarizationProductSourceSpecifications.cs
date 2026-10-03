// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RepolarizationProductSourceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RepolarizationVariantsPreserveMechanicsAndMeasuredRate), RepolarizationVariantsPreserveMechanicsAndMeasuredRate),
        new(nameof(RepolarizationVariantsRejectConflictingModes), RepolarizationVariantsRejectConflictingModes),
    ];

    private sealed record FamilyCase(string Name, PhysiologyIllustrationConfiguration Basic,
        PhysiologyIllustrationConfiguration[] Variants, PhysiologyIllustrationConfiguration[] Invalid,
        Func<PhysiologyIllustrationConfiguration, RegularPhysiologyPlan> ExpectedPlan);

    private static readonly bool[] NotchedPOptions = [false, true];

    private static FamilyCase[] Cases()
    {
        var lowPotassium = PhysiologyIllustrationConfiguration.Default with { HypokalemiaRepolarization = true };
        var calcium = PhysiologyIllustrationConfiguration.Default with { Calcium = CalciumIllustration.High };
        var digitalis = PhysiologyIllustrationConfiguration.Default with { DigitalisEffect = true };
        var quinidine = PhysiologyIllustrationConfiguration.Default with { Quinidine = QuinidineIllustration.LowT };
        return
        [
            new("Hypokalemia", lowPotassium,
                [lowPotassium, lowPotassium with { HypokalemiaInvertedT = true }, lowPotassium with { HypokalemiaTuFusion = true }, lowPotassium with { HypokalemiaConduction = true }],
                new[] { lowPotassium with { HypokalemiaRepolarization = false, HypokalemiaTuFusion = true },
                lowPotassium with { HypokalemiaRepolarization = false, HypokalemiaConduction = true },
                lowPotassium with { HypokalemiaRepolarization = false, HypokalemiaInvertedT = true },
                lowPotassium with { HyperkalemiaRepolarization = true }, lowPotassium with { HyperkalemiaConduction = true },
                lowPotassium with { Wpw = true }, lowPotassium with { CardiacActivity = CardiacActivity.VentricularOnly },
                lowPotassium with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } },
                _ => HypokalemiaRepolarizationReference.CreatePlan()),
            new("Calcium", calcium,
                Enum.GetValues<CalciumIllustration>().Where(mode => mode != CalciumIllustration.Reference).Select(mode => calcium with { Calcium = mode }).ToArray(),
                new[] { calcium with { Calcium = (CalciumIllustration)99 },
                calcium with { HypokalemiaRepolarization = true }, calcium with { HypokalemiaTuFusion = true },
                calcium with { HyperkalemiaRepolarization = true }, calcium with { HyperkalemiaConduction = true },
                calcium with { Wpw = true }, calcium with { CardiacActivity = CardiacActivity.VentricularOnly },
                calcium with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } },
                _ => CalciumRepolarizationReference.CreatePlan()),
            new("Digitalis", digitalis,
                Enum.GetValues<DigitalisTShape>().Select(mode => digitalis with { DigitalisShape = mode }).ToArray(),
                new[] { digitalis with { DigitalisShape = (DigitalisTShape)99 },
                digitalis with { HypokalemiaRepolarization = true }, digitalis with { DigitalisEffect = false, DigitalisShape = DigitalisTShape.LowT },
                digitalis with { HyperkalemiaRepolarization = true }, digitalis with { Calcium = CalciumIllustration.High },
                digitalis with { Wpw = true }, digitalis with { CardiacActivity = CardiacActivity.VentricularOnly },
                digitalis with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } },
                _ => DigitalisEffectReference.CreatePlan()),
            new("Quinidine", quinidine,
                Enum.GetValues<QuinidineIllustration>().Where(mode => mode != QuinidineIllustration.Reference).SelectMany(mode => NotchedPOptions.Select(notched => quinidine with { Quinidine = mode, QuinidineNotchedP = notched })).ToArray(),
                new[] { quinidine with { Quinidine = (QuinidineIllustration)99 },
                quinidine with { Quinidine = QuinidineIllustration.Reference, QuinidineNotchedP = true },
                quinidine with { DigitalisEffect = true }, quinidine with { HypokalemiaRepolarization = true },
                quinidine with { HyperkalemiaRepolarization = true }, quinidine with { Calcium = CalciumIllustration.High },
                quinidine with { Wpw = true }, quinidine with { CardiacActivity = CardiacActivity.VentricularOnly },
                quinidine with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } },
                config => QuinidineEffectReference.CreatePlan(config.Quinidine)),
        ];
    }

    private static void RepolarizationVariantsPreserveMechanicsAndMeasuredRate()
    {
        foreach (var family in Cases())
        {
            var reference = PhysiologyIllustrationSource.Create(family.Basic);
            var expectedBlocks = Enumerable.Range(1, 110)
                .Select(step => reference.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray()).ToArray();
            foreach (var config in family.Variants)
            {
                Check.That(config.ResolvePlan() == family.ExpectedPlan(config), $"{family.Name}: authored 60 bpm event grid");
                var source = PhysiologyIllustrationSource.Create(config);
                var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
                EcgHeartRateMeasurement? restored = null;
                var beats = new List<DetectedEcgBeat>();
                bool changed = false;
                for (int step = 1; step <= 110; step++)
                {
                    var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                    var expected = expectedBlocks[step - 1];
                    Check.That(actual.Count == expected.Length, $"{family.Name}: variant sample frontiers match");
                    foreach (var pair in actual.Zip(expected))
                    {
                        var a = WaveformEnvelopeCodec.Decode(pair.First);
                        foreach (var plane in a.Planes)
                        {
                            bool equal = plane.Samples.SequenceEqual(pair.Second.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                            if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                            else { Check.That(equal, $"{family.Name}: morphology does not alter mechanical or respiratory channels"); }
                        }
                        var events = detector.Consume(pair.First);
                        beats.AddRange(events);
                        long endNs = a.StartSimTimeNs + 196_000_000;
                        var reading = detector.Read(endNs);
                        if (restored is not null)
                        {
                            Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(endNs),
                                $"{family.Name}: repolarization candidate handling restores exactly");
                        }
                        restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                        if (endNs > 3_000_000_000)
                        {
                            Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 60000,
                                $"{family.Name}: P/T/U morphology does not become an extra QRS: {reading}");
                        }
                    }
                    if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
                }
                Check.That(changed == (config != family.Basic), $"{family.Name}: each nonbaseline variant changes acquired ECG");
                Check.That(beats.Count >= 18 && beats.Zip(beats.Skip(1)).All(p => p.Second.PeakTimeNs - p.First.PeakTimeNs == 1_000_000_000),
                    $"{family.Name}: one detection per QRS");
            }
        }
    }

    private static void RepolarizationVariantsRejectConflictingModes()
    {
        foreach (var family in Cases())
        {
            foreach (var invalid in family.Invalid)
            {
                bool rejected = false;
                try { PhysiologyIllustrationSource.Create(invalid); }
                catch (ArgumentException) { rejected = true; }
                Check.That(rejected, $"{family.Name}: orphan and incompatible modes reject at construction");
            }
            _ = PhysiologyIllustrationSource.Create(family.Basic with { VentricularMechanicalEnabled = false });
        }
    }
}
