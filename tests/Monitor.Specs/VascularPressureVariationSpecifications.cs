// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VascularPressureVariationSpecifications
{
    private static readonly string SeedA = new('a', 64);
    private static readonly string SeedB = new('b', 64);

    public static Specification[] All =>
    [
        new(nameof(SeededDriftIsSmoothBoundedAndReproducible), SeededDriftIsSmoothBoundedAndReproducible),
        new(nameof(PressureDriftsAroundItsTarget), PressureDriftsAroundItsTarget),
        new(nameof(PressureVariationRejectsUnsupportedInputs), PressureVariationRejectsUnsupportedInputs),
    ];

    private static void SeededDriftIsSmoothBoundedAndReproducible()
    {
        var drift = new SeededVascularVariation(100, SeedA, "physiology.pressure.arterial");
        var same = new SeededVascularVariation(100, SeedA, "physiology.pressure.arterial");
        var other = new SeededVascularVariation(100, SeedB, "physiology.pressure.arterial");
        long[] times = Enumerable.Range(0, 400).Select(i => i * 2_400_000_000L).ToArray();
        Check.That(drift.GainPermille(0) == 1000 && drift.GainPermille(SeededVascularVariation.KnotPeriodNs * SeededVascularVariation.KnotCount) == 1000,
            "the drift starts and wraps at the level");
        Check.That(times.All(t => drift.GainPermille(t) is >= 900 and <= 1100), "the drift stays within its amplitude");
        Check.That(times.All(t => drift.GainPermille(t) == same.GainPermille(t)) && times.Any(t => drift.GainPermille(t) != other.GainPermille(t)),
            "the same seed reproduces the drift and another seed changes it");
        Check.That(times.Any(t => drift.GainPermille(t) >= 1060) && times.Any(t => drift.GainPermille(t) <= 940),
            "excursions reach most of the amplitude in both directions");
        Check.That(Enumerable.Range(0, 1000).All(i => Math.Abs(drift.GainPermille((i + 1) * 100_000_000L) - drift.GainPermille(i * 100_000_000L)) <= 3),
            "the drift changes smoothly between beats");
    }

    private static void PressureDriftsAroundItsTarget()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            AbpTarget = new(12000, 8000),
            PressureVariation = new(1000, 300, SeedA)
        };
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        session.DiscardStartup();
        var diastolic = new List<int>();
        for (int step = 0; step < 180 * 20; step++)
        {
            session.Advance(50_000_000);
            if (step % 100 == 99 && session.Measurements!.AbpMean.Pulse is { DiastolicCentiMmHg: { } value }) { diastolic.Add(value); }
        }
        Check.That(diastolic.Count > 30 && diastolic.Max() - diastolic.Min() >= 600 && Math.Abs(diastolic.Average() - 8000) <= 300,
            "measured diastolic pressure drifts by several mmHg around its target");
        string first = JsonSerializer.Serialize(PhysiologyIllustrationSource.Create(configuration).CaptureState());
        string second = JsonSerializer.Serialize(PhysiologyIllustrationSource.Create(configuration).CaptureState());
        Check.That(first == second, "the same configuration prepares the same drift");
    }

    private static void PressureVariationRejectsUnsupportedInputs()
    {
        static string Reason(PhysiologyIllustrationConfiguration configuration)
        {
            try { _ = PhysiologyIllustrationSource.Create(configuration); }
            catch (ArgumentException exception) { return exception.Message; }
            return "accepted";
        }
        var defaults = PhysiologyIllustrationConfiguration.Default;
        Check.That(Reason(defaults with { PressureVariation = new(2001, 0, SeedA) }) == "Physiology.PressureVariationOutOfRange" &&
            Reason(defaults with { PressureVariation = new(0, 501, SeedA) }) == "Physiology.PressureVariationOutOfRange",
            "amplitudes outside the channel ranges are rejected");
        Check.That(Reason(defaults with { UseVascularReservoir = false, PressureVariation = new(500, 0, SeedA) }) == "Physiology.PressureVariationRequiresReservoir",
            "drift needs the reservoir pressure source");
        Check.That(Reason(defaults with { PressureVariation = new(0, 0, SeedA) }) == "accepted" &&
            Reason(defaults with { PaTarget = new(1500, 1000), PressureVariation = new(0, 500, SeedA) }) == "Physiology.PressureVariationTooLarge",
            "zero amplitudes are inert and a drift too large for a low level is rejected");
        bool invalid = false;
        try { _ = new SeededVascularVariation(401, SeedA, "physiology.pressure.arterial"); }
        catch (ArgumentException) { invalid = true; }
        Check.That(invalid, "the drift factor is bounded");
    }
}
