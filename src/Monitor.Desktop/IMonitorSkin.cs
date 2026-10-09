// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Media;

namespace Monitor.Desktop;

// In-process desktop composition port. Skins receive presentation controls only;
// acquisition, measurement validity, alarm projection and commands belong to the host.
internal interface IMonitorSkin
{
    public MonitorChannels Channels { get; }
    public string Id { get; }
    public IBrush Background { get; }
    public IBrush ChannelBrush(int channel);
    public Control Compose(MonitorSkinContent content);
}

internal sealed record MonitorSkinContent(
    Control Header,
    Control Waveforms,
    Control Numerics,
    Control Temperature,
    Control Custom1,
    Control Custom2,
    Control Nibp);
