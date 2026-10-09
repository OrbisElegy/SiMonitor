// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Simulation.Authoring;

public static class PhysiologyIllustrationSource
{
    public static Guid ChannelId(int row) => row switch
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

    // Physical scale is supplied explicitly. Select the same authored per-beat
    // response as the waveform source without reading amplitudes or measured PR.
    public static PhysiologyTransportSource CreateTransport(PhysiologyIllustrationConfiguration configuration,
        VentilationTransportPlan ventilation, int referenceStrokeVolumeMicroliters, long ejectionDurationNs = 240_000_000)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var plan = configuration.ResolvePlan();
        StrokeVolumeResponse response = PrematureBeatPerfusion.IsPattern(plan.ConductionPattern) ? StrokeVolumeResponse.PrematureBeat :
            AtrialFibrillationReference.IsPattern(plan.ConductionPattern) ? StrokeVolumeResponse.AtrialFibrillation :
            ConductedFlutterPerfusion.Supports(plan) ? StrokeVolumeResponse.ConductedFlutter :
            ResolveFixedPerfusion(configuration, plan)?.Arterial.UseCardiacFillingPerfusion == true ||
                configuration.UseVascularReservoir && CardiacFillingPerfusion.Supports(plan) ? StrokeVolumeResponse.CardiacFilling :
            plan.SeededRate is not null ? StrokeVolumeResponse.SeededRate : StrokeVolumeResponse.Constant;
        return PhysiologyTransportSource.Create(plan, ventilation,
            new(referenceStrokeVolumeMicroliters, ejectionDurationNs, response, configuration.IllustrateAfSystemicPulseDeficit));
    }

    public static PhysiologyWaveformGroup Create(PhysiologyIllustrationConfiguration? configuration = null, int abpZeroOffsetCentiMmHg = 0, int paZeroOffsetCentiMmHg = 0, int cvpZeroOffsetCentiMmHg = 0,
        VentilationTransportPlan? ventilation = null)
    {
        configuration ??= PhysiologyIllustrationConfiguration.Default;
        // Preserve the existing authored control range, not a clinical range.
        if (new[] { abpZeroOffsetCentiMmHg, paZeroOffsetCentiMmHg, cvpZeroOffsetCentiMmHg }.Any(value => value is < -1000 or > 1000))
        { throw new ArgumentException("Pressure demo offset must be within +/-10 mmHg."); }
        if (configuration.CvpBaselineCentiMmHg is < -500 or > 3000)
        { throw new ArgumentException("Physiology.CvpBaselineOutOfRange"); }
        if (configuration.AbpPulsePermille is < 500 or > 2000 || configuration.PaPulsePermille is < 500 or > 2000)
        { throw new ArgumentException("Physiology.PressurePulseOutOfRange"); }
        configuration.AbpTarget?.Validate(arterial: true);
        configuration.PaTarget?.Validate(arterial: false);
        configuration.PressureVariation?.Validate();
        if (configuration.AbpTarget is not null && configuration.AbpPulsePermille != 1000 ||
            configuration.PaTarget is not null && configuration.PaPulsePermille != 1000)
        { throw new ArgumentException("Physiology.PressureTargetConflictsWithPulse"); }
        RegularPhysiologyPlan plan = configuration.ResolvePlan();
        bool sinusArrest = plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration;
        bool sinusArrhythmia = plan.ConductionPattern == AvConductionPattern.SinusArrhythmiaIllustration;
        bool conductedFlutter = ConductedFlutterPerfusion.Supports(plan);
        bool cardiacFilling = configuration.UseVascularReservoir && CardiacFillingPerfusion.Supports(plan);
        bool flutter = AtrialFlutterReference.IsPattern(plan.ConductionPattern);
        var fixedPerfusion = ResolveFixedPerfusion(configuration, plan);
        bool fibrillation = AtrialFibrillationReference.IsPattern(plan.ConductionPattern);
        bool prematureBeat = PrematureAtrialReference.IsPattern(plan.ConductionPattern) || PrematureJunctionalReference.IsPattern(plan.ConductionPattern) || PrematureVentricularReference.IsPattern(plan.ConductionPattern);
        bool beatPerfusion = PrematureBeatPerfusion.IsPattern(plan.ConductionPattern);
        bool variablePerfusion = beatPerfusion || fibrillation;
        bool shortCoupled = plan.ConductionPattern == AvConductionPattern.ShortCoupledRonTPvcIllustration;
        bool blockedAtrial = plan.ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration;
        long PulseDuration(long normal) => plan.RateAdjustment is not null || plan.PacingOutput is not null ? Math.Min(normal, (long)plan.VentricularPeriodNs - 80_000_000) : configuration.SeededRate is { } rate ? Math.Min(normal, rate.MinimumPeriodNs - 80_000_000) : shortCoupled ? normal : prematureBeat && !blockedAtrial ? Math.Min(normal, PrematureAtrialReference.Timing.RrIntervalNs - 80_000_000) : fibrillation ? Math.Min(normal, AtrialFibrillationReference.MinimumRrNs - 80_000_000) : flutter ? Math.Min(normal, plan.HeartPeriodNs * plan.VentricularConductionRatio - 80_000_000) : normal;
        // Preserve independent pressure morphology while the RC source retains
        // pressure across missing and resumed ejections. Teaching parameters only.
        PhysiologyWaveformChannelPlan[] channels =
            [new(plan, new(ChannelId(0), "AcqECGMonitor250@1", 1, 1, 0, 1),
                configuration.Pacing is { } pacing ? PacingReference.CreateLeadIIBands(pacing, configuration.PacingOutput) :
                sinusArrest ? SinusArrestReference.CreateLeadIIBands() :
                sinusArrhythmia ? SinusArrhythmiaReference.CreateLeadIIBands() :
                configuration.Zones is { } zones ? zones.CreateLeadIIBands() :
                configuration.Infarction is { } infarction ? infarction.CreateLeadIIBands() :
                configuration.TContour is { } contour ? contour.CreateLeadIIBands() :
                configuration.VentricularShape != EcgVentricularIllustration.Reference ? EcgVentricularIllustrations.CreateLeadIIBands(configuration.VentricularShape) :
                configuration.AtrialShape != EcgAtrialIllustration.Reference ? EcgAtrialIllustrations.CreateLeadIIBands(configuration.AtrialShape) :
                configuration.Quinidine != QuinidineIllustration.Reference ? QuinidineEffectReference.CreateLeadIIBands(configuration.Quinidine, configuration.QuinidineNotchedP) :
                configuration.DigitalisEffect ? DigitalisEffectReference.CreateLeadIIBands(configuration.DigitalisShape) :
                configuration.Calcium != CalciumIllustration.Reference ? CalciumRepolarizationReference.CreateLeadIIBands(configuration.Calcium) :
                configuration.HypokalemiaRepolarization ? HypokalemiaRepolarizationReference.CreateLeadIIBands(configuration.HypokalemiaTuFusion, configuration.HypokalemiaInvertedT, configuration.HypokalemiaConduction) :
                configuration.HyperkalemiaFusion ? HyperkalemiaFusionReference.CreateLeadIIBands() :
                configuration.HyperkalemiaConduction ? HyperkalemiaConductionReference.CreateLeadIIBands(configuration.HyperkalemiaAbsentP) :
                configuration.HyperkalemiaRepolarization ? HyperkalemiaRepolarizationReference.CreateLeadIIBands() :
                configuration.AtrialEscape ? AtrialEscapeReference.CreateLeadIIBands() :
                configuration.Aar ? AcceleratedAtrialReference.CreateLeadIIBands() :
                configuration.Ajr ? AcceleratedJunctionalReference.CreateLeadIIBands() :
                configuration.Aivr ? AcceleratedVentricularReference.CreateLeadIIBands(configuration.AivrFusion, configuration.AivrCapture) :
                configuration.Vt ? VentricularTachycardiaReference.CreateLeadIIBands(configuration.VtFusion, configuration.VtCapture, configuration.VtBidirectional, configuration.VtTwisting) :
                configuration.Svt ? SupraventricularTachycardiaReference.CreateLeadIIBands(configuration.SvtRbbb, configuration.SvtLbbb) :
                configuration.NormalPrDelta ? NormalPrDeltaReference.CreateLeadIIBands(configuration.ProlongedPrDelta) :
                configuration.ShortPr ? ShortPrReference.CreateLeadIIBands() :
                configuration.Wpw ? WpwReference.CreateLeadIIBands(configuration.WpwNegativeV1, configuration.WpwSmallerDelta) :
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
                    ? CompleteAvBlockVentricularReference.CreateLeadIIBands() : TextbookEcgReference.CreateBands(configuration.SeededRate?.Timing), 10, 0),
             new RespirationPlan(configuration.RespAmplitudeCounts, configuration.RespCardiacArtifactCounts).CreateChannel(plan, ChannelId(1), 0),
             new(plan, new(ChannelId(2), "AcqPleth125@1", 1, 1, 0, 1),
                Array.Empty<EventWaveformBand>(), 250, 0, PlethRunoff: fixedPerfusion?.Pleth ?? new(80_000_000, variablePerfusion ? 512_000_000 : PulseDuration(512_000_000), variablePerfusion ? 1250 : 1000, UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation, IllustrateAfSystemicPulseDeficit: configuration.IllustrateAfSystemicPulseDeficit, UseConductedFlutterPerfusion: conductedFlutter, UseCardiacFillingPerfusion: cardiacFilling)),
             fixedPerfusion?.Arterial.CreateChannel(plan, ChannelId(3), 0) ?? (configuration.UseVascularReservoir
                ? new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000,
                    Morphology: new(VascularPressureMorphologyKind.Arterial, variablePerfusion ? 600_000_000 : PulseDuration(600_000_000), 4000, MaximumPulseOverlap: variablePerfusion ? 2 : 1), UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation, IllustrateAfSystemicPulseDeficit: configuration.IllustrateAfSystemicPulseDeficit, UseConductedFlutterPerfusion: conductedFlutter, UseCardiacFillingPerfusion: cardiacFilling).CreateChannel(plan, ChannelId(3), 0)
                : new ArterialPulsePlan(80_000_000, PulseDuration(600_000_000), 80, 40).CreateChannel(plan, ChannelId(3), 0)),
             configuration.ResolveCapnogram().CreateChannel(plan, ChannelId(4), 0),
             fixedPerfusion?.Pulmonary.CreateChannel(plan, ChannelId(5), 0) ?? (configuration.UseVascularReservoir
                ? new VascularPressurePlan(40_000_000, 200_000_000, 700_000_000, 1000, 500, 5000,
                    Morphology: new(VascularPressureMorphologyKind.PulmonaryArtery, variablePerfusion ? 640_000_000 : PulseDuration(640_000_000), 1500, MaximumPulseOverlap: variablePerfusion ? 2 : 1), UsePrematureBeatPerfusion: beatPerfusion, UseAtrialFibrillationPerfusion: fibrillation, UseConductedFlutterPerfusion: conductedFlutter, UseCardiacFillingPerfusion: cardiacFilling).CreateChannel(plan, ChannelId(5), 0)
                : new PulmonaryArteryPulsePlan(40_000_000, PulseDuration(640_000_000), 10, 15).CreateChannel(plan, ChannelId(5), 0)),
             ((fixedPerfusion?.Venous ?? new CentralVenousPressurePlan(600,
                 new(0, 120_000_000, 200), new(0, 120_000_000, 80),
                 new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
                 new(400_000_000, 160_000_000, 120), -100, MaximumComponentOverlap: shortCoupled || configuration.SeededRate is not null || plan.RateAdjustment is not null ? 2 : 1)) with { BaselineCentiMmHg = configuration.CvpBaselineCentiMmHg }).CreateChannel(plan, ChannelId(6), 0)];
        if (fibrillation)
        { channels[0] = channels[0] with { Bands = Array.AsReadOnly(channels[0].Bands.Select(b => b.AfBeatSelection is null ? b : b with { AfTiming = plan }).ToArray()) }; }
        if (plan.RateAdjustment is { } adjustment)
        { channels[0] = channels[0] with { Bands = adjustment.AdjustBands(plan, channels[0].Bands) }; }
        if (ventilation is not null)
        {
            channels[1] = VentilationWaveformCoupling.Respiration(channels[1], ventilation);
            channels[4] = VentilationWaveformCoupling.Capnogram(channels[4], ventilation);
        }
        foreach (var (row, gain) in new[] { (3, configuration.AbpPulsePermille), (5, configuration.PaPulsePermille) })
        {
            if (gain == 1000) { continue; }
            var pressure = channels[row].VascularPressure;
            if (pressure?.Morphology is not { } morphology)
            { throw new ArgumentException("Physiology.PressurePulseRequiresReservoirMorphology"); }
            int height = checked((int)Monitor.Simulation.Determinism.FixedPointMath.RoundDivideTiesToEven(
                (Int128)morphology.PulseHeightCentiMmHg * gain, 1000));
            channels[row] = (pressure with { Morphology = morphology with { PulseHeightCentiMmHg = height } })
                .CreateChannel(plan, ChannelId(row), channels[row].QualityFlags);
        }
        foreach (var (row, target) in new[] { (3, configuration.AbpTarget), (5, configuration.PaTarget) })
        {
            if (target is null) { continue; }
            channels[row] = ApplyTarget(channels[row], target, plan, ChannelId(row));
        }
        if (configuration.PressureVariation is { } variation)
        {
            channels[3] = ApplyVariation(channels[3], variation.AbpAmplitudeCentiMmHg,
                variation.SeedHex, "physiology.pressure.arterial", plan, ChannelId(3));
            channels[5] = ApplyVariation(channels[5], variation.PaAmplitudeCentiMmHg,
                variation.SeedHex, "physiology.pressure.pulmonary", plan, ChannelId(5));
        }
        foreach (var (row, offset) in new[] { (3, abpZeroOffsetCentiMmHg), (5, paZeroOffsetCentiMmHg), (6, cvpZeroOffsetCentiMmHg) })
        { channels[row] = channels[row] with { PressureZeroOffsetCentiMmHg = offset }; }
        return PhysiologyWaveformGroup.Start(ChannelId(0), ChannelId(2), 1, 1, 1, 0, 16, channels);
    }
    private static PhysiologyWaveformChannelPlan ApplyVariation(PhysiologyWaveformChannelPlan channel, int amplitudeCentiMmHg,
        string seedHex, string streamName, RegularPhysiologyPlan plan, Guid channelId)
    {
        if (amplitudeCentiMmHg == 0) { return channel; }
        if (plan.CardiacActivity is CardiacActivity.Absent or CardiacActivity.AtrialOnly ||
            !plan.VentricularMechanicalEnabled && plan.MechanicalAfterCycles is null) { return channel; }
        var pressure = channel.VascularPressure ?? throw new ArgumentException("Physiology.PressureVariationRequiresReservoir");
        int permille = VascularPressureSource.SolveVariationAmplitude(plan, pressure, amplitudeCentiMmHg)
            ?? throw new ArgumentException("Physiology.PressureVariationTooLarge");
        var varied = pressure with { Variation = new SeededVascularVariation(permille, seedHex, streamName) };
        try { return varied.CreateChannel(plan, channelId, channel.QualityFlags); }
        catch (EventWaveformException) { throw new ArgumentException("Physiology.PressureVariationTooLarge"); }
    }

    private static PhysiologyWaveformChannelPlan ApplyTarget(PhysiologyWaveformChannelPlan channel, VascularPressureTarget target,
        RegularPhysiologyPlan plan, Guid channelId)
    {
        // Targets describe effective beats. With no ventricular mechanical activity,
        // retain the selected source's runoff instead of rejecting the scenario or
        // synthesizing ejections to satisfy the stored target.
        if (plan.CardiacActivity is CardiacActivity.Absent or CardiacActivity.AtrialOnly ||
            !plan.VentricularMechanicalEnabled && plan.MechanicalAfterCycles is null)
        { return channel; }
        var pressure = channel.VascularPressure;
        if (pressure?.Morphology is not { } morphology)
        { throw new ArgumentException("Physiology.PressureTargetRequiresReservoirMorphology"); }
        var solved = VascularPressureSource.SolveTarget(plan, pressure, target.SystolicCentiMmHg, target.DiastolicCentiMmHg) ??
            throw new ArgumentException("Physiology.PressureTargetUnreachable");
        // Start at the diastolic target so the startup transient does not mask it.
        var adjusted = pressure with
        {
            EjectionEquilibriumCentiMmHg = solved.EjectionEquilibriumCentiMmHg,
            InitialPressureCentiMmHg = target.DiastolicCentiMmHg,
            Morphology = morphology with { PulseHeightCentiMmHg = solved.PulseHeightCentiMmHg }
        };
        try { return adjusted.CreateChannel(plan, channelId, channel.QualityFlags); }
        catch (EventWaveformException) { throw new ArgumentException("Physiology.PressureTargetUnreachable"); }
    }
    // Select the bundle once so Pleth/ABP/PA/CVP cannot drift into separate
    // per-channel rhythm mappings. Existing configuration validation runs first.
    private static FixedPerfusionPreset? ResolveFixedPerfusion(
        PhysiologyIllustrationConfiguration configuration, RegularPhysiologyPlan plan)
    {
        var preset = ResolveReferencePerfusion(configuration, plan);
        if (preset is null || plan.RateAdjustment is null) { return preset; }
        VascularPressurePlan WithOverlap(VascularPressurePlan pressure) => pressure with
        {
            Morphology = pressure.Morphology! with
            { MaximumPulseOverlap = (int)((pressure.Morphology!.DurationNs + plan.VentricularPeriodNs - 1) / plan.VentricularPeriodNs) }
        };
        return preset with
        {
            Arterial = WithOverlap(preset.Arterial),
            Pulmonary = WithOverlap(preset.Pulmonary),
            Venous = preset.Venous with { MaximumComponentOverlap = 8 }
        };
    }

    private static FixedPerfusionPreset? ResolveReferencePerfusion(
        PhysiologyIllustrationConfiguration configuration, RegularPhysiologyPlan plan)
    {
        if (plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration || configuration.AtrialEscape)
        { return FixedPerfusionPresets.FillingSinglePulse; }
        if (plan.ConductionPattern == AvConductionPattern.SinusArrhythmiaIllustration)
        { return FixedPerfusionPresets.FillingPulmonaryOverlap; }
        if (configuration.Aivr) { return FixedPerfusionPresets.AcceleratedVentricular; }
        if (configuration.Aar || configuration.Ajr) { return FixedPerfusionPresets.AcceleratedSupraventricular; }
        if (AtrialFlutterReference.IsPattern(plan.ConductionPattern) && plan.VentricularConductionRatio == 1)
        { return new(FlutterOneToOnePerfusionReference.Pleth, FlutterOneToOnePerfusionReference.Arterial, FlutterOneToOnePerfusionReference.Pulmonary, FlutterOneToOnePerfusionReference.Venous); }
        if (configuration.Vt)
        { return new(VtPerfusionReference.Pleth, VtPerfusionReference.Arterial, VtPerfusionReference.Pulmonary, VtPerfusionReference.Venous); }
        if (configuration.Svt)
        { return new(SvtPerfusionReference.Pleth, SvtPerfusionReference.Arterial, SvtPerfusionReference.Pulmonary, SvtPerfusionReference.Venous); }
        return null;
    }

}
