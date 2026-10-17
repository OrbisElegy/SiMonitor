// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SpontaneousRespirationIntegrationSpecifications
{
    private static PhysiologyIllustrationConfiguration Configuration => PhysiologyIllustrationConfiguration.Default with
    {
        RespiratoryPattern = RespiratoryPattern.SpontaneousIllustration,
        CapnogramIllustration = CapnogramIllustration.SpontaneousVentilation
    };

    public static Specification[] All =>
    [
        new(nameof(NaturalBreathsKeepEveryGasTemplateAndDeadSpaceGate), NaturalBreathsKeepEveryGasTemplateAndDeadSpaceGate),
        new(nameof(NaturalBreathsPreserveCardiacSamplesAndContinuation), NaturalBreathsPreserveCardiacSamplesAndContinuation),
        new(nameof(NaturalBreathsKeepSeededPressureAndFixedSensorDelay), NaturalBreathsKeepSeededPressureAndFixedSensorDelay),
    ];

    private static void NaturalBreathsKeepEveryGasTemplateAndDeadSpaceGate()
    {
        var physiology = Configuration.ResolvePlan();
        foreach (var illustration in Enum.GetValues<CapnogramIllustration>())
        {
            var configuration = Configuration with { CapnogramIllustration = illustration };
            var reference = GasBands(PhysiologyIllustrationSource.Create(configuration));
            var coupled = GasBands(PhysiologyIllustrationSource.Create(configuration,
                ventilation: new(450_000, 150_000, 210_000)));
            var shallow = GasBands(PhysiologyIllustrationSource.Create(configuration,
                ventilation: new(150_000, 150_000, 210_000)));
            var blocked = GasBands(PhysiologyIllustrationSource.Create(configuration,
                ventilation: new(450_000, 150_000, 210_000, AirwayOpen: false)));
            for (ulong cycle = 0; cycle < 32; cycle++)
            {
                PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, cycle)];
                long terminalNs = SpontaneousRespirationSchedule.PeriodNs(physiology, cycle) -
                    SpontaneousRespirationSchedule.InspirationDurationNs(physiology, cycle);
                long original = EventWaveformComposition.Restore(new(reference, events)).EvaluateAt(terminalNs);
                long actual = EventWaveformComposition.Restore(new(coupled, events)).EvaluateAt(terminalNs);
                long gated = EventWaveformComposition.Restore(new(shallow, events)).EvaluateAt(terminalNs);
                Check.That(actual == original && (SpontaneousRespirationSchedule.DepthPermille(cycle) > 1000
                    ? gated == original : gated == 0),
                    $"{illustration}: breath depth changes dead space without multiplying terminal gas concentration");
                Check.That(EventWaveformComposition.Restore(new(blocked, events)).EvaluateAt(terminalNs) == 0,
                    "closed airway preserves chest motion but produces no alveolar CO2 excursion");
            }
        }
    }

    private static void NaturalBreathsPreserveCardiacSamplesAndContinuation()
    {
        var regular = PhysiologyIllustrationSource.Create(Configuration with { RespiratoryPattern = RespiratoryPattern.Regular });
        var natural = PhysiologyIllustrationSource.Create(Configuration);
        HashSet<Guid> independent = [PhysiologyIllustrationSource.ChannelId(0), PhysiologyIllustrationSource.ChannelId(2),
            PhysiologyIllustrationSource.ChannelId(3), PhysiologyIllustrationSource.ChannelId(5)];
        for (long timeNs = 50_000_000; timeNs <= 5_000_000_000; timeNs += 50_000_000)
        {
            regular.AdvanceTo(timeNs, 50, 1, 100, out var oldSamples);
            natural.AdvanceTo(timeNs, 50, 1, 100, out var newSamples);
            Check.That(oldSamples.Where(sample => independent.Contains(sample.ChannelId))
                .SequenceEqual(newSamples.Where(sample => independent.Contains(sample.ChannelId))),
                "natural respiration leaves ECG, PLETH, ABP and PA samples unchanged");
        }
        natural.AdvanceTo(5_123_000_000, 100, 2, 100);
        var unchanged = PhysiologyWaveformGroup.Restore(natural.CaptureState());
        natural.ContinueWith(PhysiologyIllustrationSource.Create(Configuration));
        Check.That(natural.CaptureState().Channels.All(channel => channel.Generator.History.Count == 0),
            "equivalent authored cycle arrays do not accumulate continuation history");
        EqualAdvance(natural, unchanged, 6_000_000_000);

        natural.ContinueWith(PhysiologyIllustrationSource.Create(Configuration with
        { RespiratoryPattern = RespiratoryPattern.Regular }));
        var replay = PhysiologyWaveformGroup.Restore(JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(
            JsonSerializer.Serialize(natural.CaptureState()))!);
        for (long timeNs = 6_050_000_000; timeNs <= 11_000_000_000; timeNs += 50_000_000)
        { EqualAdvance(natural, replay, timeNs); }

        static void EqualAdvance(PhysiologyWaveformGroup first, PhysiologyWaveformGroup second, long timeNs)
        {
            var expected = first.AdvanceTo(timeNs, 1000, 20, 100, out var expectedSamples);
            var actual = second.AdvanceTo(timeNs, 1000, 20, 100, out var actualSamples);
            Check.That(expectedSamples.SequenceEqual(actualSamples) && expected.Count == actual.Count &&
                expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                "native samples, pending blocks and outgoing respiratory tails restore exactly");
        }
    }

    private static void NaturalBreathsKeepSeededPressureAndFixedSensorDelay()
    {
        var physiology = Configuration.ResolvePlan();
        var seeded = new SeededExpirationPressure(40, 300,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        var plan = Configuration.ResolveCapnogram() with { SeededPressure = seeded };
        var regular = plan.CreateChannel(physiology with { RespiratoryPattern = RespiratoryPattern.Regular }, Guid.NewGuid(), 0);
        var natural = plan.CreateChannel(physiology, Guid.NewGuid(), 0);
        var delayed = (plan with { TransportDelayNs = 350_000_000 }).CreateChannel(physiology, Guid.NewGuid(), 0);
        var dispersed = (plan with { DispersionStepNs = 30_000_000 }).CreateChannel(physiology, Guid.NewGuid(), 0);
        for (ulong cycle = 0; cycle < 64; cycle++)
        {
            PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, cycle)];
            var plainCurve = EventWaveformComposition.Restore(new(natural.Bands, events));
            var delayCurve = EventWaveformComposition.Restore(new(delayed.Bands, events));
            var dispersionCurve = EventWaveformComposition.Restore(new(dispersed.Bands, events));
            long terminalNs = SpontaneousRespirationSchedule.PeriodNs(physiology, cycle) -
                SpontaneousRespirationSchedule.InspirationDurationNs(physiology, cycle);
            Check.That(plainCurve.EvaluateAt(terminalNs) == EventWaveformComposition.Restore(new(regular.Bands, events))
                .EvaluateAt(physiology.BreathPeriodNs - physiology.InspirationDurationNs),
                "natural timing preserves the independent 64-breath pressure sequence");
            for (long timeNs = 0; timeNs <= terminalNs + 300_000_000; timeNs += 50_000_000)
            {
                long value = plainCurve.EvaluateAt(timeNs);
                Check.That(delayCurve.EvaluateAt(timeNs + 350_000_000) == value,
                    "sensor transport delay is fixed in physical time for every breath");
                long weighted = value + 2 * (timeNs >= 30_000_000 ? plainCurve.EvaluateAt(timeNs - 30_000_000) : 0) +
                    (timeNs >= 60_000_000 ? plainCurve.EvaluateAt(timeNs - 60_000_000) : 0);
                Check.That(Math.Abs(4 * dispersionCurve.EvaluateAt(timeNs) - weighted) <= 16,
                    "1:2:1 dispersion retains fixed delays and weights across variable periods");
            }
        }
    }

    private static IReadOnlyList<EventWaveformBand> GasBands(PhysiologyWaveformGroup source) => source.CaptureState()
        .Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(4)).Generator.Bands;
}
