// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Specs;

internal static class Program
{
    public static int Main(string[] args)
    {
        int shard = 0, shards = 1;
        if (args.Length > 0 && args[0] == "--shard")
        {
            if (args.Length != 3 || !int.TryParse(args[1], out shard) || !int.TryParse(args[2], out shards) || shards is < 1 or > 32 || shard < 0 || shard >= shards)
            { Console.Error.WriteLine("Usage: --shard INDEX COUNT (0 <= INDEX < COUNT <= 32)"); return 2; }
            args = [];
        }
        if (args.Length != 0)
        {
            using CancellationTokenSource cancellation = new();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                if (args[0] is "--audio-native-audition" or "--audio-native-check" or "--audio-native-diagnostics" or "--audio-native-clock-probe")
                { return NativeAudioCommand.Execute(args, Console.Out, Console.Error, cancellation.Token); }
                if (ManagedAudioCommand.Commands.Contains(args[0]))
                { return ManagedAudioCommand.Execute(args, Console.Out, Console.Error, cancellation.Token); }
                if (args[0] == "--audio-tone-fixture")
                { return AudioFixtureCommand.Execute(args, Console.OpenStandardOutput(), Console.Error, cancellation.Token); }
                if (args[0] is "--oxygen-transport-fixture" or "--oxygenation-replay-check" or
                    "--oxygen-deep-transport-fixture" or "--oxygenation-deep-replay-check" or "--oxygenation-realtime-check")
                { return OxygenationFixtureCommand.Execute(args, Console.Out, Console.Error, cancellation.Token); }
                return SvgFixtureCommand.Execute(args, Console.Out, Console.Error, cancellation.Token);
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        Specification[] specifications =
        [
            .. LocalizationSpecifications.All,
            .. LanguagePreferenceSpecifications.All,
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
            .. PhysiologyIllustrationSpecifications.All,
            .. MonitorDisplaySpecifications.All,
            .. MonitorContinuationSpecifications.All,
            .. CapnographyMeasurementSpecifications.All,
            .. MeasurementDisplaySpecifications.All,
            .. EcgHeartRateMeasurementSpecifications.All,
            .. EcgDetectionRegressionSpecifications.All,
            .. EcgRhythmSpecifications.All,
            .. PlethMeasurementSpecifications.All,
            .. PulseOximeterChainSpecifications.All,
            .. OxygenationTransportSpecifications.All,
            .. DeepOxygenationSpecifications.All,
            .. RealtimeOxygenationSpecifications.All,
            .. VentilationWaveformSpecifications.All,
            .. OxygenationDefaultsSpecifications.All,
            .. ImpedanceRespirationMeasurementSpecifications.All,
            .. LiveWaveformMeasurementSpecifications.All,
            .. MeanPressureMeasurementSpecifications.All,
            .. MonitorAlertSpecifications.All,
            .. SeededRateSpecifications.All,
            .. SeededCo2Specifications.All,
            .. MeasuredLimitSpecifications.All,
            .. PressureLimitSpecifications.All,
            .. AlarmConfirmationSpecifications.All,
            .. NoExpirationConfirmationSpecifications.All,
            .. AlarmLifecycleSpecifications.All,
            .. AlarmAttentionSpecifications.All,
            .. AlarmNotificationSpecifications.All,
            .. NotificationSoundSpecifications.All,
            .. NotificationSettingsSpecifications.All,
            .. TextbookEcgReferenceSpecifications.All,
            .. EcgElectrodeProjectionSpecifications.All,
            .. ElectrodeSignalGeneratorSpecifications.All,
            .. MorphologyRecoverySpecifications.All,
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
            .. SvtRightBundleSpecifications.All,
            .. IndependentRhythmSpecifications.All,
            .. EctopicAtrialSpecifications.All,
            .. SinusArrhythmiaSpecifications.All,
            .. SinusArrestSpecifications.All,
            .. SinusArrhythmiaPerfusionSpecifications.All,
            .. RegularRhythmPerfusionSpecifications.All,
            .. VtFusionSpecifications.All,
            .. AcceleratedVentricularFusionSpecifications.All,
            .. AcceleratedVentricularCaptureSpecifications.All,
            .. VtCaptureSpecifications.All,
            .. BidirectionalVtSpecifications.All,
            .. TwistingVtSpecifications.All,
            .. VtPerfusionSpecifications.All,
            .. CardiacFillingSpecifications.All,
            .. RhythmFillingAuditSpecifications.All,
            .. FlutterOneToOnePerfusionSpecifications.All,
            .. ConductedFlutterPerfusionSpecifications.All,
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
            .. HyperkalemiaProductSourceSpecifications.All,
            .. RepolarizationProductSourceSpecifications.All,
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
            .. AtrialShapeProductSpecifications.All,
            .. VentricularShapeProductSpecifications.All,
            .. TContourProductSpecifications.All,
            .. RegionalInfarctionProductSpecifications.All,
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
            .. AudioPcmBufferSpecifications.All,
            .. SampleToneRendererSpecifications.All,
            .. AudioRenderSessionSpecifications.All,
            .. AudioOutputLifecycleSpecifications.All,
            .. SoundPreviewSpecifications.All,
            .. NativeAudioCommandSpecifications.All,
            .. EndpointAudioOutputSpecifications.All,
            .. NativeAudioClockSpecifications.All,
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

        int executed = 0;
        int failed = 0;
        for (int index = 0; index < specifications.Length; index++)
        {
            if (index % shards != shard) { continue; }
            executed++;
            // Runner boundary: report every failing specification instead of stopping at the first.
            try
            {
                specifications[index].Body();
                Console.WriteLine($"ok {index + 1} - {specifications[index].Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"not ok {index + 1} - {specifications[index].Name}");
                Console.Error.WriteLine($"{specifications[index].Name}: {exception}");
            }
        }

        if (failed != 0)
        {
            Console.WriteLine($"failed: {failed} of {executed} executable specifications (shard {shard + 1}/{shards}; total {specifications.Length})");
            return 1;
        }
        Console.WriteLine($"ok: {executed} executable specifications passed (shard {shard + 1}/{shards}; total {specifications.Length})");
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
