// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record ProjectedEcgDemoConfiguration(int HeartRateBpm, int QtcMilliseconds, string? MethodId, int VentricularConductionRatio = 1,
    int PDurationMilliseconds = 100, int PrIntervalMilliseconds = 160,
    int QrsDurationMilliseconds = 80, int TDurationMilliseconds = 180,
    ProjectedEcgUConfiguration? UWave = null, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, EcgLimbPlacement Placement = EcgLimbPlacement.Standard, int? IndependentVentricularPeriodMilliseconds = null, int? IndependentVentricularOffsetMilliseconds = null, ProjectedEcgTConfiguration? TWave = null)
{
    internal static ProjectedEcgDemoConfiguration Default { get; } = new(75, 400, null);

    internal long ResolveAtrialPeriodNs()
    {
        if (HeartRateBpm is < 30 or > 200) { throw new ArgumentException("Invalid base rate."); }
        return (long)Monitor.Simulation.Determinism.FixedPointMath.RoundDivideTiesToEven(60_000_000_000, HeartRateBpm);
    }

    internal EcgCycleTiming ResolveTiming()
    {
        if (!Enum.IsDefined(Placement) || VentricularConductionRatio is < 1 or > 4 || !Enum.IsDefined(CardiacActivity)) { throw new ArgumentException("Invalid conduction ratio or cardiac activity."); }
        long atrialPeriod = ResolveAtrialPeriodNs();
        if (IndependentVentricularPeriodMilliseconds is { } independent &&
            (independent is < 800 or > 3200 || independent * 1_000_000L < atrialPeriod || VentricularConductionRatio != 1))
        { throw new ArgumentException("Invalid independent ventricular period."); }
        long rr = IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : atrialPeriod * VentricularConductionRatio;
        if (MethodId is null)
        {
            if (this with { VentricularConductionRatio = 1, UWave = null, CardiacActivity = CardiacActivity.AtrialAndVentricular, Placement = EcgLimbPlacement.Standard, IndependentVentricularPeriodMilliseconds = null, IndependentVentricularOffsetMilliseconds = null, TWave = null } != Default) { throw new ArgumentException("Invalid fixed reference configuration."); }
            return TextbookEcgReference.Timing with { RrIntervalNs = rr };
        }
        if (HeartRateBpm is < 30 or > 200 || QtcMilliseconds is < 1 or > 1000 ||
            PDurationMilliseconds is < 1 or > 1000 || PrIntervalMilliseconds is < 1 or > 1000 ||
            QrsDurationMilliseconds is < 1 or > 1000 || TDurationMilliseconds is < 1 or > 1000)
        { throw new ArgumentException("Demo parameter outside supported input bounds."); }
        long qt = new EcgQtCorrection(MethodId, QtcMilliseconds * 1_000_000L, rr).ResolveQtIntervalNs();
        // Validate the complete requested timing, not the adult reference's
        // fixed PR/QRS/T durations against a shorter requested RR interval.
        var timing = new EcgCycleTiming(rr, PDurationMilliseconds * 1_000_000L,
            PrIntervalMilliseconds * 1_000_000L, QrsDurationMilliseconds * 1_000_000L,
            qt, TDurationMilliseconds * 1_000_000L);
        timing.Validate();
        return timing;
    }
}

// Desktop binding for the shared textbook-constrained electrode reference.
internal static class ProjectedEcgDemoSource
{
    internal static string[] LeadNames => ["I", "II", "III", "aVR", "aVL", "aVF", "V1", "V2", "V3", "V4", "V5", "V6"];
    internal static Guid ChannelId(EcgLead lead) => Guid.Parse($"00000000-0000-4000-8000-{12 - (int)lead:D12}");

    internal static ElectrodeWaveformGroup Create(ProjectedEcgDemoConfiguration? configuration = null)
    {
        configuration ??= ProjectedEcgDemoConfiguration.Default;
        var timing = configuration.ResolveTiming();
        long offset = DemoVentricularTiming.ResolveOffset(configuration.IndependentVentricularPeriodMilliseconds, configuration.IndependentVentricularOffsetMilliseconds, timing.PrIntervalNs);
        var electrodes = TextbookElectrodeReference.CreateElectrodes(configuration.UWave?.Resolve(timing), timing, configuration.TWave?.Resolve(), configuration.TWave?.ResolveShape());
        RegularPhysiologyPlan plan = new(0, configuration.ResolveAtrialPeriodNs(), offset,
            80_000_000, offset + 80_000_000, 3_750_000_000, 1_875_000_000,
            VentricularConductionRatio: configuration.VentricularConductionRatio, CardiacActivity: configuration.CardiacActivity,
            IndependentVentricularPeriodNs: configuration.IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : null);
        return ElectrodeWaveformGroup.Start(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), 1, 1, 1, 0, 16, plan, electrodes,
            Enum.GetValues<EcgLead>().Select(lead => new ElectrodeChannelPlan(lead, ChannelId(lead), 10, 0)).ToArray(), configuration.Placement);
    }
}
