// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

[Flags]
internal enum MonitorChannels
{
    None = 0,
    Ecg = 1,
    Resp = 2,
    Pleth = 4,
    Abp = 8,
    Co2 = 16,
    Pa = 32,
    Cvp = 64,
    All = Ecg | Resp | Pleth | Abp | Co2 | Pa | Cvp
}

internal static class MonitorChannelMapping
{
    internal static MonitorChannels ForChannel(int channel) => channel is >= 0 and <= 6
        ? (MonitorChannels)(1 << channel) : throw new ArgumentOutOfRangeException(nameof(channel));
    internal static MonitorChannels ForNumeric(MonitorNumeric numeric) => numeric switch
    {
        MonitorNumeric.HeartRate => MonitorChannels.Ecg,
        MonitorNumeric.RespirationRate => MonitorChannels.Resp,
        MonitorNumeric.SpO2 or MonitorNumeric.PulseRate => MonitorChannels.Pleth,
        MonitorNumeric.EtCo2 or MonitorNumeric.Co2RespirationRate => MonitorChannels.Co2,
        MonitorNumeric.AbpMean => MonitorChannels.Abp,
        MonitorNumeric.PaMean => MonitorChannels.Pa,
        MonitorNumeric.CvpMean => MonitorChannels.Cvp,
        _ => throw new ArgumentOutOfRangeException(nameof(numeric))
    };
}
