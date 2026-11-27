// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Monitor.Network;

public static class ClassroomWire
{
    public const string WaveformSubprotocol = "simonitor.waveforms.v1";
    public const int AcknowledgementBytes = 48;
    internal const JsonNumberHandling DecimalString = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString;
}

public sealed record ClassroomManifest(
    int ProtocolVersion,
    Guid StudentId,
    Guid SessionId,
    Guid InstanceId,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong TimebaseEpoch,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong StreamEpoch,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong OldestBlockSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextBlockSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] long NextBlockStartSimTimeNs);

public sealed record ClassroomClockReply(
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T1,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T2,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T3);

public sealed record ClassroomClockSample(long ClientSendNs, long HostReceiveNs, long HostSendNs, long ClientReceiveNs)
{
    public long RoundTripNs => checked((ClientReceiveNs - ClientSendNs) - (HostSendNs - HostReceiveNs));
    public long HostOffsetNs => checked((long)(((Int128)HostReceiveNs - ClientSendNs + HostSendNs - ClientReceiveNs) / 2));
}
