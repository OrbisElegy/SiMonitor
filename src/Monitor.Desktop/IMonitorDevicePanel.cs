// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal enum DeviceOperatingMode
{
    Off,
    Monitor,
    Manual,
    Aed,
    Pacer
}

// A host-facing contract shared by enclosure skins. Mode requests are accepted
// by the host before it publishes state with SetOperatingMode.
internal interface IMonitorDevicePanel
{
    public DeviceOperatingMode OperatingMode { get; }
    public event Action<DeviceOperatingMode>? OperatingModeRequested;
    public event Action? GainRequested;
    public event Action? EventRequested;
    public event Action? AlarmResetRequested;
    public event Action<bool>? PacingQuarterHeld;
    public void SetOperatingMode(DeviceOperatingMode mode);
    public void SetGain(double? gain);
    public void SetAlarmState(MonitorNoticeLevel? level);
    public void ReleaseMomentaryInputs();
}
