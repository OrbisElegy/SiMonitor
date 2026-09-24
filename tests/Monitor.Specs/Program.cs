// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Specs;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            using CancellationTokenSource cancellation = new();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                if (args[0] == "--audio-tone-fixture")
                { return AudioFixtureCommand.Execute(args, Console.OpenStandardOutput(), Console.Error, cancellation.Token); }
                return SvgFixtureCommand.Execute(args, Console.Out, Console.Error, cancellation.Token);
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        Specification[] specifications =
        [
            .. TherapySpecifications.All,
            .. AssessmentSpecifications.All,
            .. IdentitySpecifications.All,
            .. AuthenticationSpecifications.All,
            .. PersistenceSpecifications.All,
            .. KeyProtectionSpecifications.All,
            .. BackupSpecifications.All,
            .. DeterminismSpecifications.All,
            .. NumericDeterminismSpecifications.All,
            .. AuthorityOrderingSpecifications.All,
            .. DeterminismKernelSpecifications.All,
            .. SignalSampleClockSpecifications.All,
            .. PeriodicSignalGeneratorSpecifications.All,
            .. RegularPhysiologyTimelineSpecifications.All,
            .. EventWaveformSpecifications.All,
            .. PhysiologySignalGeneratorSpecifications.All,
            .. PhysiologyWaveformGroupSpecifications.All,
            .. TextbookEcgReferenceSpecifications.All,
            .. EcgElectrodeProjectionSpecifications.All,
            .. ElectrodeSignalGeneratorSpecifications.All,
            .. ElectrodeWaveformGroupSpecifications.All,
            .. ChestProgressionSpecifications.All,
            .. PtAndUWaveSpecifications.All,
            .. EcgQtCorrectionSpecifications.All,
            .. PlethPulseSpecifications.All,
            .. PlethRunoffSpecifications.All,
            .. ArterialPulseSpecifications.All,
            .. VascularPressureSpecifications.All,
            .. PressureZeroOffsetSpecifications.All,
            .. VascularPressureMorphologySpecifications.All,
            .. PhysiologyForkSpecifications.All,
            .. EcgLimbPlacementSpecifications.All,
            .. CapnogramSpecifications.All,
            .. CapnogramPlateauSpecifications.All,
            .. CapnogramTransportSpecifications.All,
            .. CapnogramDispersionSpecifications.All,
            .. RespirationSpecifications.All,
            .. RespiratoryPatternSpecifications.All,
            .. RespiratoryCo2ResponseSpecifications.All,
            .. GroupedConductionSpecifications.All,
            .. RightBundleBlockSpecifications.All,
            .. LeftBundleBlockSpecifications.All,
            .. BundleBlockSpecifications.All,
            .. WpwSpecifications.All,
            .. ShortPrSpecifications.All,
            .. NormalPrDeltaSpecifications.All,
            .. SvtSpecifications.All,
            .. SvtBundleBlockSpecifications.All,
            .. VtSpecifications.All,
            .. AcceleratedVentricularSpecifications.All,
            .. AcceleratedJunctionalSpecifications.All,
            .. AcceleratedAtrialSpecifications.All,
            .. AtrialEscapeSpecifications.All,
            .. SinusArrhythmiaSpecifications.All,
    .. SinusArrestSpecifications.All,
    .. SinusArrhythmiaPerfusionSpecifications.All,
            .. AtrialEscapePerfusionSpecifications.All,
            .. VtFusionSpecifications.All,
            .. AcceleratedVentricularFusionSpecifications.All,
            .. AcceleratedVentricularCaptureSpecifications.All,
            .. VtCaptureSpecifications.All,
            .. BidirectionalVtSpecifications.All,
            .. TwistingVtSpecifications.All,
            .. VtPerfusionSpecifications.All,
            .. AcceleratedVentricularPerfusionSpecifications.All,
            .. AcceleratedJunctionalPerfusionSpecifications.All,
            .. AcceleratedAtrialPerfusionSpecifications.All,
            .. FlutterOneToOnePerfusionSpecifications.All,
            .. SvtPerfusionSpecifications.All,
            .. PrematureAtrialSpecifications.All,
            .. BlockedPrematureAtrialSpecifications.All,
            .. AberrantPrematureAtrialSpecifications.All,
            .. PrematureJunctionalSpecifications.All,
            .. RetrogradeJunctionalSpecifications.All,
            .. PrematureVentricularSpecifications.All,
            .. VentricularGroupedSpecifications.All,
            .. DiversePvcSpecifications.All,
            .. InterpolatedPvcSpecifications.All,
            .. VentricularCoupletSpecifications.All,
            .. RonTPvcSpecifications.All,
            .. ShortCoupledPvcSpecifications.All,
            .. PrematurePerfusionSpecifications.All,
            .. JunctionalEscapeSpecifications.All,
            .. VentricularEscapeSpecifications.All,
            .. AtrialFlutterSpecifications.All,
            .. AtrialFibrillationSpecifications.All,
            .. AtrialFibrillationPerfusionSpecifications.All,
            .. AtrialFibrillationDeficitSpecifications.All,
            .. AtrialFibrillationAberrancySpecifications.All,
            .. HyperkalemiaRepolarizationSpecifications.All,
            .. HyperkalemiaConductionSpecifications.All,
            .. HyperkalemiaFusionSpecifications.All,
            .. HypokalemiaRepolarizationSpecifications.All,
            .. CalciumSpecifications.All,
            .. DigitalisSpecifications.All,
            .. QuinidineSpecifications.All,
            .. VentricularDisorganizationSpecifications.All,
            .. InspiratoryPauseSpecifications.All,
            .. ExpiratoryPauseSpecifications.All,
            .. RespCardiacArtifactSpecifications.All,
            .. RespiratoryActivitySpecifications.All,
            .. RespiratoryTransitionSpecifications.All,
            .. RespiratoryResumptionSpecifications.All,
            .. ConductionRatioSpecifications.All,
            .. CardiacActivitySpecifications.All,
            .. IndependentVentricularSpecifications.All,
            .. VentricularPhaseSpecifications.All,
            .. TWaveScaleSpecifications.All,
            .. TWaveShapeSpecifications.All,
            .. PWaveComponentSpecifications.All,
            .. AtrialIllustrationSpecifications.All,
            .. VentricularIllustrationSpecifications.All,
            .. TContourSpecifications.All,
            .. StSegmentSpecifications.All,
            .. StArchSpecifications.All,
            .. StTFusionSpecifications.All,
            .. InfarctionIllustrationSpecifications.All,
            .. InfarctionTerritorySpecifications.All,
            .. RegionalRepolarizationSpecifications.All,
            .. InfarctionComponentSpecifications.All,
            .. InfarctionZoneSpecifications.All,
            .. QrsTemplateBlendSpecifications.All,
            .. AuthoredQrsMeasurementSpecifications.All,
            .. QrsContributionSpecifications.All,
            .. ToneVoiceSpecifications.All,
            .. ElectrodeForkSpecifications.All,
            .. MechanicalUncouplingSpecifications.All,
            .. MechanicalTransitionSpecifications.All,
            .. MechanicalResumptionSpecifications.All,
            .. MechanicalStrideSpecifications.All,
            .. ShortCycleEcgSpecifications.All,
            .. PulmonaryArterySpecifications.All,
            .. CentralVenousPressureSpecifications.All,
            .. PeriodicWaveformPipelineSpecifications.All,
            .. PeriodicWaveformGroupSpecifications.All,
            .. SignalAcquisitionDelaySpecifications.All,
            .. SensorFaultSpecifications.All,
            .. WaveformEnvelopeCodecSpecifications.All,
            .. WaveformBlockAssemblerSpecifications.All,
            .. WaveformBlockRingSpecifications.All,
            .. WaveformRecordArchiveSpecifications.All,
            .. WaveformRecoveryPlannerSpecifications.All,
            .. ConnectionHealthSpecifications.All,
            .. DataContinuitySpecifications.All,
            .. NoDataPresentationSpecifications.All,
            .. NoDataSweepSpecifications.All,
            .. SweepStateProjectionSpecifications.All,
            .. SweepPlanSchedulerSpecifications.All,
            .. SweepTraceCompositionSpecifications.All,
            .. SweepPlotGeometrySpecifications.All,
            .. SweepColumnCoverageSpecifications.All,
            .. SweepColumnEnvelopeSpecifications.All,
            .. SweepColumnSegmentSpecifications.All,
            .. EcgVerticalGeometrySpecifications.All,
            .. EcgCalibrationGeometrySpecifications.All,
            .. SweepSegmentClipperSpecifications.All,
            .. SweepPathBuilderSpecifications.All,
            .. SweepFramePathSpecifications.All,
            .. SweepFrameReconstructionSpecifications.All,
            .. SweepFrameScaleSpecifications.All,
            .. SweepVoltageMappingSpecifications.All,
            .. SweepSampleOffsetSpecifications.All,
            .. EcgStripSpecifications.All,
            .. EcgManualMeasurementSpecifications.All,
            .. Ecg12ViewAdmissionSpecifications.All,
            .. Ecg12ThemeSpecifications.All,
            .. Ecg12ZoomSpecifications.All,
            .. Ecg12ScreenTransformSpecifications.All,
            .. EcgPaperGridSpecifications.All,
            .. EcgPaperGridSvgSpecifications.All,
            .. SvgFixtureSpecifications.All,
            .. SweepDisplayCompositionSpecifications.All,
            .. FillOnceThenHoldSpecifications.All,
            .. ContinuitySignatureSpecifications.All,
            .. ContinuityCapsuleChainSpecifications.All,
            .. ContinuityShadowSpecifications.All,
            .. ClientRecoverySessionSpecifications.All,
            .. RecoveryHandshakeSpecifications.All,
            .. RecoveryResyncPlanFactorySpecifications.All,
            .. RecoveryRelockSpecifications.All,
            .. RecoveryWireCodecSpecifications.All,
            .. WaveformSubscriberQueueSpecifications.All,
        ];

        for (int index = 0; index < specifications.Length; index++)
        {
            specifications[index].Body();
            Console.WriteLine($"ok {index + 1} - {specifications[index].Name}");
        }

        Console.WriteLine($"ok: {specifications.Length} executable specifications passed");
        return 0;
    }
}

internal sealed record Specification(string Name, Action Body);

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
