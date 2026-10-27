// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed partial class LocalMonitorPreviewSession
{
    private void PrepareTimelineInitialState(PatientTimelineEvent initial, PatientTimelineSource? source, bool fork)
    {
        // Entry state is part of the branch baseline, not an entered event. All
        // work stays on the unpublished view, including waveform continuation.
        _configuration = fork ? FreezePatientConfiguration(_configuration) : _configuration;
        _configuration = _configuration with
        {
            AbpTarget = initial.Targets.Abp ?? _configuration.AbpTarget,
            PaTarget = initial.Targets.Pa ?? _configuration.PaTarget
        };
        ConfigurePatientTimelineCore([initial], Array.AsReadOnly(new[] { source }), _programOxygenation,
            SimulationTimeNs, fork);
        if (_timelineSources[0] is { } definition)
        {
            var continued = _source.Fork();
            continued.ContinueWith(definition._source);
            var ecg = _ecgSource?.Fork();
            ecg?.ContinueWith(definition._ecgSource!);
            RealtimeOxygenationSource? oxygenation = null;
            if (definition._realtimeConfiguration is { } model)
            {
                long time = checked(SimulationTimeNs + _sourceTimeOffsetNs);
                var transport = PhysiologyIllustrationSource.CreateTransport(definition._configuration,
                    model.Ventilation, model.ReferenceStrokeVolumeMicroliters);
                long gridTime = time - time % OxygenReservoirModel.StepNs;
                oxygenation = _realtimeOxygenation?.Fork() ?? new(transport, model.Parameters, model.OxygenDemandMultiplier, gridTime);
                oxygenation.ChangeTransport(transport, time, model.OxygenDemandMultiplier);
            }
            _source = continued;
            _ecgSource = ecg;
            _configuration = definition._configuration;
            _realtimeConfiguration = definition._realtimeConfiguration;
            _realtimeOxygenation = oxygenation;
            OxygenationParameters = definition.OxygenationParameters;
            ElectricalTherapy = definition.ElectricalTherapy;
            _opticalSource = definition._opticalSource;
            _authoredOpticalSaturationMilliPercent = definition._authoredOpticalSaturationMilliPercent;
            _usesOxygenation = definition._usesOxygenation;
        }
        if (initial.EcgTemplateId is not null) { _beforePacing = null; }
        if (initial.Ventilation is not null) { _timelineOpticalCurve = null; }
        if (initial.Targets.SaturationMilliPercent is { } saturation)
        { _timelineOpticalCurve = new(saturation, []); }
        ManualVitals = new(initial.Targets.Nibp ?? ManualVitals.Nibp,
            initial.Targets.TemperatureDeciCelsius ?? ManualVitals.TemperatureDeciCelsius, ManualVitals.Custom1, ManualVitals.Custom2);
    }
}
