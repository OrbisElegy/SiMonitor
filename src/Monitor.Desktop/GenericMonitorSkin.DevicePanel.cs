// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Host commands belong to the device-panel contract. A particular enclosure
// supplies controls and renders accepted state without owning therapy authority.
internal sealed partial class GenericMonitorSkin
{
    private bool _pacingRunning;
    private bool _pacingQuarterHeld;
    private bool _pacingQuarterPointerHeld;
    private IPointer? _pacingQuarterPointer;
    private Key? _pacingQuarterKeyHeld;
    private double? _deviceGainValue;
    private MonitorNoticeLevel? _deviceAlarmLevel;
    public IMonitorDevicePanel? DevicePanel => _package.Defibrillator ? this : null;
    internal bool DeviceTherapyEnabled => OperatingMode is DeviceOperatingMode.Manual or DeviceOperatingMode.Aed;
    public DeviceOperatingMode OperatingMode { get; private set; } = DeviceOperatingMode.Monitor;
    public event Action<DeviceOperatingMode>? OperatingModeRequested;
    public event Action? GainRequested;
    public event Action? EventRequested;
    public event Action? AlarmResetRequested;
    public event Action<bool>? PacingQuarterHeld;

    public void SetOperatingMode(DeviceOperatingMode mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        ReleaseMomentaryInputs();
        OperatingMode = mode;
        if (_package.Defibrillator) { RenderDeviceOperatingMode(mode); }
    }

    public void SetGain(double? gain)
    {
        bool changed = _deviceGainValue != gain;
        _deviceGainValue = gain;
        if (_package.Defibrillator) { RenderDeviceGain(gain, changed); }
    }

    public void SetAlarmState(MonitorNoticeLevel? level)
    {
        _deviceAlarmLevel = level;
        if (_package.Defibrillator) { RenderDeviceAlarmState(level); }
    }

    public void ReleaseMomentaryInputs()
    {
        ReleasePacingQuarter();
        Defibrillator.ReleaseHeldInput();
    }

    private void ConfigurePacingQuarterKey(Button key)
    {
        key.AddHandler(Control.PointerPressedEvent, (_, args) =>
        {
            if (OperatingMode != DeviceOperatingMode.Pacer || !key.IsEffectivelyEnabled || _pacingQuarterHeld ||
                !args.GetCurrentPoint(key).Properties.IsLeftButtonPressed) { return; }
            args.Handled = true;
            args.Pointer.Capture(key);
            _pacingQuarterPointer = args.Pointer;
            _pacingQuarterPointerHeld = true;
            _pacingQuarterHeld = true;
            PacingQuarterHeld?.Invoke(true);
        }, RoutingStrategies.Tunnel);
        key.AddHandler(Control.PointerReleasedEvent, (_, args) =>
        {
            if (!_pacingQuarterPointerHeld) { return; }
            args.Handled = true;
            ReleasePacingQuarter();
            args.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel);
        key.PointerCaptureLost += (_, _) => ReleasePacingQuarter();
        key.PointerExited += (_, _) => { if (_pacingQuarterPointerHeld) { ReleasePacingQuarter(); } };
        key.AddHandler(Control.KeyDownEvent, (_, args) =>
        {
            if (OperatingMode != DeviceOperatingMode.Pacer || !key.IsEffectivelyEnabled || args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true;
            if (_pacingQuarterHeld) { return; }
            _pacingQuarterKeyHeld = args.Key;
            _pacingQuarterHeld = true;
            PacingQuarterHeld?.Invoke(true);
        }, RoutingStrategies.Tunnel);
        key.AddHandler(Control.KeyUpEvent, (_, args) =>
        {
            if (args.Key != _pacingQuarterKeyHeld) { return; }
            args.Handled = true;
            ReleasePacingQuarter();
        }, RoutingStrategies.Tunnel);
        key.LostFocus += (_, _) => ReleasePacingQuarter();
        key.DetachedFromVisualTree += (_, _) => ReleasePacingQuarter();
    }

    private void ReleasePacingQuarter()
    {
        bool held = _pacingQuarterHeld;
        var pointer = _pacingQuarterPointer;
        _pacingQuarterPointer = null;
        _pacingQuarterHeld = false;
        _pacingQuarterPointerHeld = false;
        _pacingQuarterKeyHeld = null;
        pointer?.Capture(null);
        if (held) { PacingQuarterHeld?.Invoke(false); }
    }

}
