// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Media;

namespace Monitor.Desktop;

// In-process desktop composition port. Skins receive controls and layout metadata;
// acquisition, measurement validity, alarm projection and commands belong to the host.
internal interface IMonitorSkin
{
    public IMonitorDevicePanel? DevicePanel => null;
    public MonitorChannels Channels { get; }
    public MonitorChannels AdditionalMonitoredChannels => MonitorChannels.None;
    // Presentation capability only; it does not disable acquisition or erase user settings.
    public MonitorManualVitals SupportedManualVitals => MonitorManualVitals.All;
    public string Id { get; }
    public IBrush Background { get; }
    public bool CompactWaveformLabels => false;
    public double NumericFontSize => 64;
    public IBrush WarningBrush => Brush.Parse("#F2C94C");
    public IBrush ChannelBrush(int channel);
    public Control Compose(MonitorSkinContent content);
    public void RefreshLayout() { }
}

[Flags]
internal enum MonitorManualVitals
{
    None = 0,
    Temperature = 1,
    Custom1 = 2,
    Custom2 = 4,
    Nibp = 8,
    All = Temperature | Custom1 | Custom2 | Nibp
}

internal sealed record MonitorSkinContent(
    Control Header,
    Control Waveforms,
    Control TwelveLeadWaveforms,
    Control Numerics,
    Control Temperature,
    Control Custom1,
    Control Custom2,
    Control Nibp,
    Func<int> WaveformRowCount,
    Control? Trends = null);
