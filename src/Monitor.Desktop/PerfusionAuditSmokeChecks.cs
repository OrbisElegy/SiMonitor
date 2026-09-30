// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PerfusionAuditSmokeChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static void Verify()
    {
        var rows = new List<object>();
        for (int i = 0; i < DesignPreviewSettings.EcgChoiceCount; i++)
        {
            var config = DesignPreviewWindow.ResolveStyle(i, 0, 0).Physiology;
            var state = PhysiologyIllustrationSource.Create(config).CaptureState();
            var abp = state.Channels[3].Generator;
            var plan = abp.Timeline.Plan;
            var pressure = abp.VascularPressure ?? throw new InvalidOperationException("Missing reservoir");
            if (i is >= 23 and <= 25 && pressure != SvtPerfusionReference.Arterial ||
                i is >= 26 and <= 30 && pressure != VtPerfusionReference.Arterial)
            { throw new InvalidOperationException("Fast template bypassed filling-limited reference"); }
            var source = VascularPressureSource.Create(plan, pressure);
            double[] samples = Enumerable.Range(0, 100).Select(n => source.EvaluateAt(300_000_000_000L + n * 20_000_000L) / (double)FixedPointMath.Q32One / 100).ToArray();
            if (samples.Any(v => !double.IsFinite(v) || v < 0)) { throw new InvalidOperationException("Invalid pressure"); }
            rows.Add(new
            {
                Preset = i,
                Group = EcgChooserGroups.For(i),
                Pattern = plan.ConductionPattern.ToString(),
                plan.HeartPeriodNs,
                plan.IndependentVentricularPeriodNs,
                plan.VentricularMechanicalEnabled,
                pressure.EjectionEquilibriumCentiMmHg,
                pressure.UsePrematureBeatPerfusion,
                pressure.UseAtrialFibrillationPerfusion,
                pressure.UseConductedFlutterPerfusion,
                SampledAbpMinimum = samples.Min(),
                SampledAbpMaximum = samples.Max()
            });
        }
        string seed = new('a', 64);
        foreach (int rate in new[] { 30, 75, 120, 160, 180 })
        {
            var schedule = new SeededCardiacRate(rate, seed, 50);
            var config = PhysiologyIllustrationConfiguration.Default with { SeededRate = schedule };
            var generator = PhysiologyIllustrationSource.Create(config).CaptureState().Channels[3].Generator;
            var corrected = VascularPressureSource.Create(generator.Timeline.Plan, generator.VascularPressure!);
            var unchanged = VascularPressureSource.Create(generator.Timeline.Plan with { SeededRate = null }, generator.VascularPressure!);
            double[] values = Enumerable.Range(0, 100).Select(n => corrected.EvaluateAt(300_000_000_000L + n * 20_000_000L) / (double)FixedPointMath.Q32One / 100).ToArray();
            if (rate == 180 && (values.Max() >= 180 || values.Max() >= unchanged.EvaluateAt(300_000_000_000) / (double)FixedPointMath.Q32One / 100)) { throw new InvalidOperationException("Fast seeded rate still overpumps"); }
            for (ulong beat = 0; beat < 512; beat++)
            {
                if (schedule.EjectionGainPermille(beat) != schedule.EjectionGainPermille(beat + 256))
                { throw new InvalidOperationException("Seeded perfusion lost repeatability"); }
            }
            rows.Add(new
            {
                SeededRate = rate,
                MinimumGain = Enumerable.Range(0, 256).Min(n => schedule.EjectionGainPermille((ulong)n)),
                SampledAbpMinimum = values.Min(),
                SampledAbpMaximum = values.Max(),
                ReferenceAt300Seconds = unchanged.EvaluateAt(300_000_000_000)
            });
        }
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/perfusion-preset-audit.json", JsonSerializer.Serialize(rows, JsonOptions));
    }
}
