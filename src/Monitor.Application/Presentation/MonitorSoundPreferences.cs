// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record MonitorSoundPreferences(int Volume, int HeartbeatVolume, bool HeartbeatEnabled,
    int BeatSource, int PitchSource, int PauseSeconds, MonitorSoundTiming Timing)
{
    public string? OutputDeviceId { get; init; }
    public bool Muted { get; init; }
    public static MonitorSoundPreferences Default => new(50, 100, true, 0, 1, 120, new());
    public void Validate()
    {
        if (Volume is < 0 or > 100 || HeartbeatVolume is < 0 or > 100 ||
            BeatSource is < 0 or > 2 || PitchSource is < 0 or > 1 || PauseSeconds is < 1 or > 3600 || Timing is null)
        { throw new ArgumentException("SoundPreferences.Invalid"); }
        if (OutputDeviceId is not null && (string.IsNullOrWhiteSpace(OutputDeviceId) || OutputDeviceId.Length > 1024 || OutputDeviceId.Contains('\0')))
        { throw new ArgumentException("SoundPreferences.InvalidDevice"); }
        Timing.Validate();
    }
}
