// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewWindow
{
    private DefibrillatorConfiguration _editableDefibrillatorConfiguration = DefibrillatorConfiguration.Default;
    private bool _customDefibrillatorEnergySteps;
    private DeviceOperatingMode _deviceOperatingMode = DeviceOperatingMode.Monitor;
    private bool _deviceAedStartPending;
    private bool _pacingQuarterRequested;
    private bool _pacingQuarterApplied;
    private bool _stopPacingOnPowerOn;
    private int _editableEnergyJoules;
    private bool HasDevicePanel => _monitor?.Skin?.DevicePanel is not null;
    private static readonly double[] DeviceEcgGains = [.25, .5, 1, 2, 4];

    private void ConnectDevicePanel(IMonitorDevicePanel panel)
    {
        panel.SetOperatingMode(_deviceOperatingMode);
        panel.OperatingModeRequested += mode => ChangeDeviceOperatingMode(panel, mode);
        panel.GainRequested += () => ChangeDeviceEcgGain(panel);
        panel.EventRequested += () =>
        {
            if (_closed || !ReferenceEquals(_monitor.Skin?.DevicePanel, panel) || _deviceOperatingMode == DeviceOperatingMode.Off) { return; }
            _session.MarkOperatorEvent();
            SetStatus("devicePanel.eventMarked", _session.SimulationTimeNs / 1_000_000_000m);
        };
        panel.AlarmResetRequested += () =>
        {
            if (_closed || !ReferenceEquals(_monitor.Skin?.DevicePanel, panel) || _deviceOperatingMode == DeviceOperatingMode.Off) { return; }
            foreach (var lifecycle in Settings.Alerts.AlarmLifecycles)
            {
                foreach (var state in lifecycle.Attention.Conditions.ToArray())
                {
                    if (state.Episode is not null) { lifecycle.Attention.Acknowledge(state.Episode, state.Revision); }
                }
            }
            MonitorView.RefreshAttention();
            Settings.Sound.RefreshAlarmNotices(MonitorView.ActiveNotices);
            RefreshAlarmControls();
        };
        panel.PacingQuarterHeld += held => ChangePacingQuarter(panel, held);
    }

    private void ConfigureSkinTherapyDevice()
    {
        var profile = _skinPackage.DeviceProfile;
        var configuration = profile is null ? _editableDefibrillatorConfiguration :
            _deviceOperatingMode == DeviceOperatingMode.Aed ? profile.AedConfiguration : profile.ManualConfiguration;
        configuration = (configuration with
        {
            EnergyStepsJoules = _customDefibrillatorEnergySteps
                ? _editableDefibrillatorConfiguration.EnergyStepsJoules : _skinPackage.DefaultEnergyStepsJoules
        }).Snapshot();
        _defibrillator = new(Guid.NewGuid(), configuration, profile is null ? _editableEnergyJoules : _therapy.EnergyJoules);
        _aed = new(_defibrillator, (_editableDefibrillatorConfiguration.AedEnergySequenceJoules ??
            profile?.AedEnergySequenceJoules)?.Select(configuration.NearestEnergy).ToArray());
        _therapy = _therapy with { EnergyJoules = _defibrillator.EnergyJoules };
        Settings.Defibrillator.SetSkinDefaults(profile?.ManualConfiguration, _skinPackage.DefaultEnergyStepsJoules,
            _customDefibrillatorEnergySteps, _editableDefibrillatorConfiguration);
    }

    private void ChangeDeviceOperatingMode(IMonitorDevicePanel panel, DeviceOperatingMode mode)
    {
        if (_closed || _applying || !HasDevicePanel || !ReferenceEquals(_monitor.Skin?.DevicePanel, panel) || !Enum.IsDefined(mode)) { return; }
        if (mode == DeviceOperatingMode.Off)
        {
            DisarmDefibrillator();
            _aed.Disable(_safetyClock());
            _stopPacingOnPowerOn = _session.ActivePacing is not null || _pendingPacingPaper is not null;
            _pacingQuarterRequested = false;
            _deviceOperatingMode = mode;
            ConfigureSkinTherapyDevice();
            panel.SetOperatingMode(mode);
            _session.Nibp.ConfigureAuto(false, _session.Nibp.AutoIntervalMinutes);
            _session.Nibp.Stop();
            Pause();
            return;
        }
        bool finishingPowerOff = _deviceOperatingMode == DeviceOperatingMode.Off;
        if (!finishingPowerOff && (_pendingPacingPaper is not null || _pendingPresentation is not null || _session.PendingSourceTimeNs is not null))
        {
            panel.SetOperatingMode(_deviceOperatingMode);
            SetStatus("skin.pacingWaiting");
            return;
        }
        if (_deviceOperatingMode == mode) { return; }
        var previous = _deviceOperatingMode;
        DisarmDefibrillator();
        _aed.Disable(_safetyClock());
        if (_session.ActivePacing is not null) { SetPacing(_therapy, stop: true); }
        _deviceOperatingMode = mode;
        ConfigureSkinTherapyDevice();
        panel.SetOperatingMode(mode);
        if (mode == DeviceOperatingMode.Manual)
        {
            _defibrillator.SelectEnergy(_defibrillator.Configuration.NearestEnergy(
                _skinPackage.DeviceProfile!.DefaultManualEnergyJoules), _safetyClock());
            if (_monitor.Skin is GenericMonitorSkin skin) { skin.Energy.SelectedItem = _defibrillator.EnergyJoules; }
        }
        if (mode == DeviceOperatingMode.Aed) { _deviceAedStartPending = true; }
        if (previous == DeviceOperatingMode.Off && mode != DeviceOperatingMode.Off) { Start(); }
        RefreshDefibrillator();
        RefreshPacingState();
    }

    private void ChangeDeviceEcgGain(IMonitorDevicePanel panel)
    {
        if (_closed || !HasDevicePanel || _deviceOperatingMode == DeviceOperatingMode.Off || !ReferenceEquals(_monitor.Skin?.DevicePanel, panel)) { return; }
        var display = _session.Display;
        var ecg = display.Slots.FirstOrDefault(slot => slot.Channel == 0);
        if (ecg is null) { return; }
        double currentGain = ecg.Automatic ? 1 : 4000 / (ecg.Range.Maximum - ecg.Range.Minimum);
        int index = Array.FindIndex(DeviceEcgGains, gain => Math.Abs(gain - currentGain) < .001);
        double gain = DeviceEcgGains[(index + 1) % DeviceEcgGains.Length];
        var slots = display.Slots.Select(slot => slot.Channel == 0
            ? slot with { Automatic = false, Range = new(-2000 / gain, 2000 / gain) } : slot).ToArray();
        var updated = new MonitorDisplayConfiguration(display.Skin, slots);
        _session.UpdateDisplay(updated);
        Settings.RestoreDisplay(updated, Settings.PaperLayout.SelectedIndex, Settings.MeasurementUnits.SelectedIndex == 1);
        _displayPreferences = _displayPreferences with { Display = updated };
        panel.SetGain(gain);
        MonitorView.RefreshDisplay();
        SaveDisplayPreferences();
    }

    private void ChangePacingQuarter(IMonitorDevicePanel panel, bool held)
    {
        if (_closed || !ReferenceEquals(_monitor.Skin?.DevicePanel, panel)) { return; }
        _pacingQuarterRequested = held && HasDevicePanel && _deviceOperatingMode == DeviceOperatingMode.Pacer && _session.ActivePacing is not null;
        ApplyPendingPacingOutput();
    }

    private void ReleaseDeviceMomentaryInputs()
    {
        _monitor?.Skin?.DevicePanel?.ReleaseMomentaryInputs();
        _pacingQuarterRequested = false;
        ApplyPendingPacingOutput();
    }

    private void ApplyPendingPacingOutput()
    {
        if (_stopPacingOnPowerOn && _deviceOperatingMode != DeviceOperatingMode.Off && _pendingPacingPaper is null &&
            _pendingPresentation is null && _session.PendingSourceTimeNs is null)
        {
            _stopPacingOnPowerOn = false;
            if (_session.ActivePacing is not null) { SetPacing(_therapy, stop: true); }
            return;
        }
        if (!HasDevicePanel || _deviceOperatingMode != DeviceOperatingMode.Pacer || _session.ActivePacing is null ||
            _pendingPacingPaper is not null || _pendingPresentation is not null || _session.PendingSourceTimeNs is not null ||
            _pacingQuarterRequested == _pacingQuarterApplied) { return; }
        SetPacing(_therapy, stop: false);
    }
}
