// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    internal EcgAdvancedParameterEditor EcgParameters { get; }
    private ProjectedEcgDemoConfiguration? _appliedEcgDraft;
    private bool _appliedAfPulseDeficit;
    private readonly TextBlock _currentEcgTemplate = Text("");

    internal static bool IsEcgAdvancedError(ArgumentException error) => error.Message.StartsWith("Preview.InvalidEcg", StringComparison.Ordinal) ||
        error.Message == "Preview.EcgFusionLeadRequired" || error.Message == "DemoVentricularTiming.InvalidOffset" ||
        error is EventWaveformException { ReasonCode: "EcgTiming.InconsistentIntervals" or "EcgInfarction.UOverlap" } ||
        error is EventWaveformException waveform && waveform.ReasonCode.StartsWith("EcgUWave.", StringComparison.Ordinal);

    internal static string EcgAdvancedErrorKey(ArgumentException error) => error switch
    {
        EventWaveformException { ReasonCode: "EcgTiming.InconsistentIntervals" } => "ecgAdvanced.invalidTiming",
        EventWaveformException { ReasonCode: "EcgInfarction.UOverlap" } => "ecgAdvanced.invalidU",
        EventWaveformException { ReasonCode: "EcgUWave.InvalidPlan" } => "ecgAdvanced.invalidU",
        _ when error.Message == "DemoVentricularTiming.InvalidOffset" => "ecgAdvanced.invalidEscape",
        _ when error.Message == "Preview.EcgFusionLeadRequired" => "ecgAdvanced.fusionLeadRequired",
        _ => "ecgAdvanced.invalid"
    };

    internal ProjectedEcgDemoConfiguration ReadEcgParameters(ProjectedEcgDemoConfiguration preset)
    {
        var zones = InfarctionParameters.ReadZones(preset.Infarction);
        return EcgParameters.Read(preset with
        {
            Zones = zones,
            TContour = TContourParameters.Read(preset.TContour),
            Infarction = zones is null ? InfarctionParameters.Read(preset.Infarction) : null
        });
    }

    internal static PhysiologyDemoConfiguration WithAuthoredEcg(PhysiologyDemoConfiguration physiology,
        ProjectedEcgDemoConfiguration ecg)
    {
        var source = ProjectedEcgDemoSource.Create(ecg with { RateAdjustment = null, RhythmSchedule = null }).CaptureState().Generator;
        return physiology with
        {
            EcgDefinition = new AuthoredEcgDefinition(source.Timeline.Plan.HeartPeriodNs,
            source.Timeline.Plan.VentricularElectricalOffsetNs, source.Electrodes, source.Placement)
        };
    }
}
