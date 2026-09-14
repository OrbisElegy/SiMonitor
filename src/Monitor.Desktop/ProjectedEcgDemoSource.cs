// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record ProjectedEcgDemoConfiguration(int HeartRateBpm, int QtcMilliseconds, string? MethodId)
{
    internal static ProjectedEcgDemoConfiguration Default { get; } = new(75, 400, null);

    internal EcgCycleTiming ResolveTiming()
    {
        if (MethodId is null)
        {
            if (this != Default) { throw new ArgumentException("Invalid fixed reference configuration."); }
            return TextbookEcgReference.Timing;
        }
        if (HeartRateBpm is < 30 or > 200 || QtcMilliseconds is < 1 or > 1000)
        { throw new ArgumentException("Demo parameter outside supported input bounds."); }
        long rr = (long)Monitor.Simulation.Determinism.FixedPointMath.RoundDivideTiesToEven(60_000_000_000, HeartRateBpm);
        return new EcgQtCorrection(MethodId, QtcMilliseconds * 1_000_000L, rr).ResolveTiming(TextbookEcgReference.Timing);
    }
}

// Desktop binding for the shared textbook-constrained electrode reference.
internal static class ProjectedEcgDemoSource
{
    internal static string[] LeadNames => ["I", "II", "III", "aVR", "aVL", "aVF", "V1", "V2", "V3", "V4", "V5", "V6"];
    internal static Guid ChannelId(EcgLead lead) => Guid.Parse($"00000000-0000-4000-8000-{12 - (int)lead:D12}");

    internal static ElectrodeWaveformGroup Create(ProjectedEcgDemoConfiguration? configuration = null)
    {
        var timing = (configuration ?? ProjectedEcgDemoConfiguration.Default).ResolveTiming();
        var electrodes = TextbookElectrodeReference.CreateElectrodes(timing: timing);
        RegularPhysiologyPlan plan = new(0, timing.RrIntervalNs, timing.PrIntervalNs,
            80_000_000, timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
        return ElectrodeWaveformGroup.Start(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), 1, 1, 1, 0, 16, plan, electrodes,
            Enum.GetValues<EcgLead>().Select(lead => new ElectrodeChannelPlan(lead, ChannelId(lead), 10, 0)).ToArray());
    }
}
