// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PhysiologyDemoSource
{
    internal static Guid ChannelId(int row) => row switch
    {
        0 => Guid.Parse("11111111-1111-4111-8111-111111111111"),
        1 => Guid.Parse("33333333-3333-4333-8333-333333333333"),
        2 => Guid.Parse("22222222-2222-4222-8222-222222222222"),
        3 => Guid.Parse("44444444-4444-4444-8444-444444444444"),
        4 => Guid.Parse("55555555-5555-4555-8555-555555555555"),
        5 => Guid.Parse("66666666-6666-4666-8666-666666666666"),
        6 => Guid.Parse("77777777-7777-4777-8777-777777777777"),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    internal static PhysiologyWaveformGroup Create(PhysiologyDemoConfiguration? configuration = null)
    {
        configuration ??= PhysiologyDemoConfiguration.Default;
        RegularPhysiologyPlan plan = configuration.ResolvePlan();
        bool flutter = AtrialFlutterReference.IsPattern(plan.ConductionPattern);
        bool fibrillation = AtrialFibrillationReference.IsPattern(plan.ConductionPattern);
        bool prematureBeat = PrematureAtrialReference.IsPattern(plan.ConductionPattern) || PrematureJunctionalReference.IsPattern(plan.ConductionPattern) || PrematureVentricularReference.IsPattern(plan.ConductionPattern);
        bool beatPerfusion = PrematureBeatPerfusion.IsPattern(plan.ConductionPattern);
        bool variablePerfusion = beatPerfusion || fibrillation;
        bool shortCoupled = plan.ConductionPattern == AvConductionPattern.ShortCoupledRonTPvcIllustration;
        bool blockedAtrial = plan.ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration;
        long PulseDuration(long normal) => shortCoupled ? normal : prematureBeat && !blockedAtrial ? Math.Min(normal, PrematureAtrialReference.Timing.RrIntervalNs - 80_000_000) : fibrillation ? Math.Min(normal, AtrialFibrillationReference.MinimumRrNs - 80_000_000) : flutter ? Math.Min(normal, plan.HeartPeriodNs * plan.VentricularConductionRatio - 80_000_000) : normal;
        // Preserve independent pressure morphology while the RC source retains
        // pressure across missing and resumed ejections. Teaching parameters only.
        return PhysiologyWaveformGroup.Start(ChannelId(0), ChannelId(2), 1, 1, 1, 0, 16,
            [new(plan, new(ChannelId(0), "AcqECGMonitor250@1", 1, 1, 0, 1),
                configuration.NormalPrDelta ? NormalPrDeltaReference.CreateLeadIIBands() :
                configuration.ShortPr ? ShortPrReference.CreateLeadIIBands() :
                configuration.Wpw ? WpwReference.CreateLeadIIBands(configuration.WpwNegativeV1) :
                configuration.BundleBlock != EcgBundleBlockIllustration.Reference ? BundleBlockReference.CreateLeadIIBands(configuration.BundleBlock) :
                VentricularDisorganizationReference.IsPattern(plan.ConductionPattern) ? VentricularDisorganizationReference.CreateLeadIIBands(plan.ConductionPattern) :
                plan.ConductionPattern == AvConductionPattern.MobitzTwoRbbbFourToThreeIllustration ? RightBundleBlockReference.CreateLeadIIBands() :
                plan.ConductionPattern == AvConductionPattern.MobitzTwoLbbbFourToThreeIllustration ? LeftBundleBlockReference.CreateLeadIIBands() :
                PrematureVentricularReference.IsPattern(plan.ConductionPattern) ? PrematureVentricularReference.CreateLeadIIBands(plan.ConductionPattern) :
                PrematureJunctionalReference.IsPattern(plan.ConductionPattern) ? PrematureJunctionalReference.CreateLeadIIBands() :
                plan.ConductionPattern == AvConductionPattern.AberrantPrematureAtrialIllustration ? PrematureAtrialReference.CreateAberrantLeadIIBands() :
                prematureBeat ? PrematureAtrialReference.CreateLeadIIBands(blockedAtrial) :
                fibrillation ? AtrialFibrillationReference.CreateLeadIIBands(plan.ConductionPattern == AvConductionPattern.AtrialFibrillationFineIllustration, configuration.IllustrateAfAberrancy) :
                flutter ? AtrialFlutterReference.CreateLeadIIBands(plan.VentricularConductionRatio) :
                plan.ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration
                    ? CompleteAvBlockVentricularReference.CreateLeadIIBands() : TextbookEcgReference.CreateBands(), 10, 0),
             new RespirationPlan(configuration.RespAmplitudeCounts, configuration.RespCardiacArtifactCounts).CreateChannel(plan, ChannelId(1), 0),
             new(plan, new(ChannelId(2), "AcqPleth125@1", 1, 1, 0, 1),
                Array.Empty<EventWaveformBand>(), 250, 0, PlethRunoff: new(80_000_000, variablePerfusion ? 512_000_000 : PulseDuration(512_000_000), variablePerfusion ? 1250 : 1000, UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation, IllustrateAfSystemicPulseDeficit: configuration.IllustrateAfSystemicPulseDeficit)),
             configuration.UseVascularReservoir
                ? new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000,
                    Morphology: new(VascularPressureMorphologyKind.Arterial, variablePerfusion ? 600_000_000 : PulseDuration(600_000_000), 4000, MaximumPulseOverlap: variablePerfusion ? 2 : 1), UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation, IllustrateAfSystemicPulseDeficit: configuration.IllustrateAfSystemicPulseDeficit).CreateChannel(plan, ChannelId(3), 0)
                : new ArterialPulsePlan(80_000_000, PulseDuration(600_000_000), 80, 40).CreateChannel(plan, ChannelId(3), 0),
             configuration.ResolveCapnogram().CreateChannel(plan, ChannelId(4), 0),
             configuration.UseVascularReservoir
                ? new VascularPressurePlan(40_000_000, 200_000_000, 700_000_000, 1000, 500, 5000,
                    Morphology: new(VascularPressureMorphologyKind.PulmonaryArtery, variablePerfusion ? 640_000_000 : PulseDuration(640_000_000), 1500, MaximumPulseOverlap: variablePerfusion ? 2 : 1), UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation).CreateChannel(plan, ChannelId(5), 0)
                : new PulmonaryArteryPulsePlan(40_000_000, PulseDuration(640_000_000), 10, 15).CreateChannel(plan, ChannelId(5), 0),
             new CentralVenousPressurePlan(600,
                 new(0, 120_000_000, 200), new(0, 120_000_000, 80),
                 new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
                 new(400_000_000, 160_000_000, 120), -100, MaximumComponentOverlap: shortCoupled ? 2 : 1).CreateChannel(plan, ChannelId(6), 0)]);
    }
}
