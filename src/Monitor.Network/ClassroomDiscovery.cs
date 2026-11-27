// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Monitor.Network;

public sealed record ClassroomDiscoveredHost(string DisplayName, Uri Origin, string CertificateSha256)
{
    internal bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Length <= 48 &&
        !DisplayName.Any(char.IsControl) && Origin is not null && ClassroomPairingTicket.IsOrigin(Origin) &&
        Origin.AbsoluteUri.Length <= 200 && ClassroomArtifactDescriptor.IsContentHash(CertificateSha256);
}

public static class ClassroomDiscovery
{
    private const int MdnsPort = 5353;

    // A link-local DNS-SD service. Announcements are discovery hints only; the caller
    // still needs an invitation, an institution trust anchor and a successful TLS handshake.
    public static async Task AdvertiseAsync(IPAddress interfaceAddress, ClassroomDiscoveredHost advertisement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        if (!advertisement.IsValid) { throw new ArgumentException("Invalid public classroom advertisement.", nameof(advertisement)); }
        var network = FindInterface(interfaceAddress);
        IPEndPoint multicast = MulticastEndpoint(interfaceAddress, network.Index);
        using var socket = new Socket(interfaceAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.ExclusiveAddressUse = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        var any = new IPEndPoint(interfaceAddress.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, MdnsPort);
        socket.Bind(any);
        SelectInterface(socket, interfaceAddress, network.Index);
        if (interfaceAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicast.Address, interfaceAddress));
        }
        else
        {
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(multicast.Address, network.Index));
        }

        byte[] buffer = new byte[ClassroomDiscoveryDns.MaximumPacketBytes + 1];
        var clock = Stopwatch.StartNew();
        int replies = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult packet;
            try
            {
                packet = await socket.ReceiveMessageFromAsync(buffer.AsMemory(), SocketFlags.None, any, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.MessageSize) { continue; }
            if (packet.ReceivedBytes > ClassroomDiscoveryDns.MaximumPacketBytes || packet.SocketFlags.HasFlag(SocketFlags.Truncated) ||
                packet.PacketInformation.Interface != network.Index || packet.RemoteEndPoint is not IPEndPoint sender ||
                !SameLink(interfaceAddress, sender.Address, network.PrefixLength)) { continue; }
            if (!ClassroomDiscoveryDns.ReadQuestion(buffer.AsSpan(0, packet.ReceivedBytes), out ushort id, out bool preferUnicast)) { continue; }
            if (clock.Elapsed >= TimeSpan.FromSeconds(1)) { replies = 0; clock.Restart(); }
            if (replies >= 48) { continue; }
            replies++;
            bool legacy = sender.Port != MdnsPort;
            byte[] reply = ClassroomDiscoveryDns.Answer(advertisement, interfaceAddress, legacy ? id : (ushort)0, legacy);
            await socket.SendToAsync(reply, SocketFlags.None, legacy || preferUnicast ? sender : multicast, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<ClassroomDiscoveredHost>> BrowseAsync(IPAddress interfaceAddress, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10)) { throw new ArgumentOutOfRangeException(nameof(duration)); }
        var network = FindInterface(interfaceAddress);
        using var socket = new Socket(interfaceAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(interfaceAddress, 0));
        SelectInterface(socket, interfaceAddress, network.Index);
        IPEndPoint group = MulticastEndpoint(interfaceAddress, network.Index);
        ushort id = (ushort)RandomNumberGenerator.GetInt32(1, ushort.MaxValue + 1);
        byte[] question = ClassroomDiscoveryDns.Question(id);
        byte[] buffer = new byte[ClassroomDiscoveryDns.MaximumPacketBytes + 1];
        Dictionary<Uri, ClassroomDiscoveredHost> discovered = [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(duration);
        try
        {
            while (discovered.Count < 64)
            {
                await socket.SendToAsync(question, SocketFlags.None, group, deadline.Token).ConfigureAwait(false);
                using var nextQuery = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                nextQuery.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    while (discovered.Count < 64)
                    {
                        SocketReceiveFromResult packet;
                        try
                        {
                            packet = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, group, nextQuery.Token).ConfigureAwait(false);
                        }
                        catch (SocketException error) when (error.SocketErrorCode == SocketError.MessageSize) { continue; }
                        if (packet.ReceivedBytes > ClassroomDiscoveryDns.MaximumPacketBytes ||
                            packet.RemoteEndPoint is not IPEndPoint { Port: MdnsPort } sender ||
                            !SameLink(interfaceAddress, sender.Address, network.PrefixLength)) { continue; }
                        ClassroomDiscoveredHost? found = ClassroomDiscoveryDns.ReadAnswer(buffer.AsSpan(0, packet.ReceivedBytes), id);
                        if (found is not null) { discovered.TryAdd(found.Origin, found); }
                    }
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested && nextQuery.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested) { }
        return discovered.Values.OrderBy(host => host.DisplayName, StringComparer.Ordinal)
            .ThenBy(host => host.Origin.AbsoluteUri, StringComparer.Ordinal).ToArray();
    }

    private static (int Index, int PrefixLength) FindInterface(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) { continue; }
            IPInterfaceProperties properties = nic.GetIPProperties();
            UnicastIPAddressInformation? selected = properties.UnicastAddresses.FirstOrDefault(candidate => candidate.Address.Equals(address));
            if (selected is not null)
            {
                int index = address.AddressFamily == AddressFamily.InterNetwork
                    ? properties.GetIPv4Properties().Index : properties.GetIPv6Properties().Index;
                return (index, selected.PrefixLength);
            }
        }
        throw new ArgumentException("Select an address assigned to an active local interface.", nameof(address));
    }

    private static IPEndPoint MulticastEndpoint(IPAddress address, int index) => new(address.AddressFamily == AddressFamily.InterNetwork
        ? IPAddress.Parse("224.0.0.251") : new IPAddress(IPAddress.Parse("ff02::fb").GetAddressBytes(), index), MdnsPort);

    private static void SelectInterface(Socket socket, IPAddress address, int index)
    {
        bool ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        SocketOptionLevel level = ipv4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6;
        socket.SetSocketOption(level, SocketOptionName.PacketInformation, true);
        if (ipv4) { socket.SetSocketOption(level, SocketOptionName.MulticastInterface, address.GetAddressBytes()); }
        else { socket.SetSocketOption(level, SocketOptionName.MulticastInterface, index); }
        socket.SetSocketOption(level, SocketOptionName.MulticastTimeToLive, 255);
    }

    private static bool SameLink(IPAddress local, IPAddress peer, int prefixLength)
    {
        if (local.AddressFamily != peer.AddressFamily || peer.Equals(IPAddress.Any) || peer.Equals(IPAddress.IPv6Any) ||
            peer.IsIPv6Multicast) { return false; }
        byte[] first = local.GetAddressBytes();
        byte[] second = peer.GetAddressBytes();
        if (first.Length == 4 && (second[0] >= 224 || peer.Equals(IPAddress.Broadcast))) { return false; }
        int fullBytes = prefixLength / 8;
        int remaining = prefixLength % 8;
        return first.AsSpan(0, fullBytes).SequenceEqual(second.AsSpan(0, fullBytes)) &&
            (remaining == 0 || (first[fullBytes] ^ second[fullBytes]) >> (8 - remaining) == 0);
    }
}
