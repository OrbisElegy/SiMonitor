// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Monitor.Network;

// The classroom profile uses a DNS-SD PTR browse with related SRV, TXT and address records
// in the same datagram. A one-shot querier (RFC 6762 §5.1 / §6.7) needs no system DNS changes.
internal static class ClassroomDiscoveryDns
{
    internal const int MaximumPacketBytes = 2048;
    private const string Service = "_simonitor._tcp.local";
    private const ushort In = 1;
    private const ushort Ptr = 12;
    private const ushort Txt = 16;
    private const ushort Srv = 33;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] Question(ushort id)
    {
        using var packet = new MemoryStream();
        Header(packet, id, 0, 1, 0, 0);
        QuestionSection(packet);
        return packet.ToArray();
    }

    internal static bool ReadQuestion(ReadOnlySpan<byte> packet, out ushort id, out bool preferUnicast)
    {
        id = 0;
        preferUnicast = false;
        if (packet.Length is < 12 or > MaximumPacketBytes || U16(packet[2..]) != 0 || U16(packet[4..]) != 1) { return false; }
        try
        {
            var reader = new Reader(packet, 12);
            if (!reader.Name().Equals(Service, StringComparison.OrdinalIgnoreCase) || reader.U16() != Ptr) { return false; }
            ushort rrclass = reader.U16();
            id = U16(packet);
            preferUnicast = (rrclass & 0x8000) != 0;
            return (rrclass & 0x7fff) == In;
        }
        catch (InvalidDataException) { return false; }
    }

    internal static byte[] Answer(ClassroomDiscoveredHost host, IPAddress address, ushort id, bool legacy)
    {
        using var packet = new MemoryStream();
        Header(packet, id, 0x8400, legacy ? (ushort)1 : (ushort)0, 1, 3);
        if (legacy) { QuestionSection(packet); }
        string label = "sm-" + host.CertificateSha256[..16] + "-" + host.Origin.Port.ToString(CultureInfo.InvariantCulture);
        string instance = label + "." + Service;
        string target = host.Origin.HostNameType == UriHostNameType.Dns ? host.Origin.IdnHost.TrimEnd('.') : label + ".local";
        ushort uniqueClass = legacy ? In : (ushort)(0x8000 | In);
        uint ttl = legacy ? 10u : 120u;
        Record(packet, Service, Ptr, In, ttl, data => Name(data, instance));
        Record(packet, instance, Srv, uniqueClass, ttl, data =>
        {
            Word(data, 0);
            Word(data, 0);
            Word(data, (ushort)host.Origin.Port);
            Name(data, target);
        });
        Record(packet, instance, Txt, uniqueClass, ttl, data =>
        {
            TxtString(data, "v=1");
            TxtString(data, "tls=1");
            TxtString(data, "origin=" + host.Origin.AbsoluteUri);
            TxtString(data, "name=" + host.DisplayName);
            TxtString(data, "sha256=" + host.CertificateSha256);
        });
        ushort addressType = address.AddressFamily == AddressFamily.InterNetwork ? (ushort)1 : (ushort)28;
        Record(packet, target, addressType, uniqueClass, ttl, data => data.Write(address.GetAddressBytes()));
        return packet.ToArray();
    }

    internal static ClassroomDiscoveredHost? ReadAnswer(ReadOnlySpan<byte> packet, ushort expectedId)
    {
        if (packet.Length is < 12 or > MaximumPacketBytes || U16(packet) != expectedId || (U16(packet[2..]) & 0xfA0f) != 0x8000)
        {
            return null;
        }
        try
        {
            int questions = U16(packet[4..]);
            int records = U16(packet[6..]) + U16(packet[8..]) + U16(packet[10..]);
            if (questions > 4 || records is < 3 or > 32) { return null; }
            var reader = new Reader(packet, 12);
            for (int i = 0; i < questions; i++)
            {
                _ = reader.Name();
                reader.Take(4);
            }
            string? ptr = null, srv = null, txt = null;
            ushort port = 0;
            Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records; i++)
            {
                string owner = reader.Name();
                ushort type = reader.U16();
                ushort rrclass = reader.U16();
                uint ttl = BinaryPrimitives.ReadUInt32BigEndian(reader.Take(4));
                ushort size = reader.U16();
                var payload = reader;
                ReadOnlySpan<byte> data = reader.Take(size);
                if ((rrclass & 0x7fff) != In || ttl == 0) { continue; }
                if (type == Ptr && owner.Equals(Service, StringComparison.OrdinalIgnoreCase))
                {
                    ptr = payload.Name();
                    if (payload.Offset != reader.Offset || !ptr.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase)) { return null; }
                }
                else if (type == Srv && owner.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase))
                {
                    srv = owner;
                    payload.Take(4);
                    port = payload.U16();
                    _ = payload.Name();
                    if (payload.Offset != reader.Offset || port == 0) { return null; }
                }
                else if (type == Txt && owner.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase))
                {
                    txt = owner;
                    while (!data.IsEmpty)
                    {
                        int length = data[0];
                        if (length >= data.Length || fields.Count >= 12) { return null; }
                        string value = Utf8.GetString(data.Slice(1, length));
                        int equals = value.IndexOf('=');
                        if (equals > 0 && !fields.TryAdd(value[..equals], value[(equals + 1)..])) { return null; }
                        data = data[(length + 1)..];
                    }
                }
            }
            if (reader.Offset != packet.Length || ptr is null || !ptr.Equals(srv, StringComparison.OrdinalIgnoreCase) ||
                !ptr.Equals(txt, StringComparison.OrdinalIgnoreCase) || fields.GetValueOrDefault("v") != "1" ||
                fields.GetValueOrDefault("tls") != "1" || !fields.TryGetValue("name", out string? display) ||
                !fields.TryGetValue("sha256", out string? hash) || !Uri.TryCreate(fields.GetValueOrDefault("origin"), UriKind.Absolute, out Uri? origin) ||
                !ClassroomPairingTicket.IsOrigin(origin) || origin.Port != port) { return null; }
            var result = new ClassroomDiscoveredHost(display, origin, hash);
            return result.IsValid ? result : null;
        }
        catch (InvalidDataException) { return null; }
        catch (DecoderFallbackException) { return null; }
    }

    private static ushort U16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static void Word(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Header(Stream stream, ushort id, ushort flags, ushort questions, ushort answers, ushort additional)
    {
        Word(stream, id);
        Word(stream, flags);
        Word(stream, questions);
        Word(stream, answers);
        Word(stream, 0);
        Word(stream, additional);
    }

    private static void QuestionSection(Stream stream)
    {
        Name(stream, Service);
        Word(stream, Ptr);
        Word(stream, In);
    }

    private static void Record(Stream stream, string owner, ushort type, ushort rrclass, uint ttl, Action<Stream> write)
    {
        Name(stream, owner);
        Word(stream, type);
        Word(stream, rrclass);
        Span<byte> seconds = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(seconds, ttl);
        stream.Write(seconds);
        using var payload = new MemoryStream();
        write(payload);
        Word(stream, checked((ushort)payload.Length));
        payload.WriteTo(stream);
    }

    private static void Name(Stream stream, string name)
    {
        foreach (string label in name.Split('.'))
        {
            if (label.Length is < 1 or > 63) { throw new ArgumentException("Invalid DNS label.", nameof(name)); }
            stream.WriteByte((byte)label.Length);
            stream.Write(Encoding.ASCII.GetBytes(label));
        }
        stream.WriteByte(0);
    }

    private static void TxtString(Stream stream, string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private ref struct Reader(ReadOnlySpan<byte> packet, int start)
    {
        private readonly ReadOnlySpan<byte> _packet = packet;
        internal int Offset { get; private set; } = start;

        internal ReadOnlySpan<byte> Take(int bytes)
        {
            if (bytes > _packet.Length - Offset) { throw new InvalidDataException("Incomplete DNS record."); }
            ReadOnlySpan<byte> result = _packet.Slice(Offset, bytes);
            Offset += bytes;
            return result;
        }

        internal ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

        internal string Name()
        {
            var name = new StringBuilder();
            int position = Offset;
            int resume = -1;
            int hops = 0;
            while (position < _packet.Length)
            {
                int length = _packet[position++];
                if (length == 0)
                {
                    Offset = resume == -1 ? position : resume;
                    return name.ToString();
                }
                if ((length & 0xc0) == 0xc0)
                {
                    if (position >= _packet.Length || ++hops > 16) { break; }
                    int pointer = ((length & 0x3f) << 8) | _packet[position++];
                    if (pointer >= position - 2) { break; }
                    if (resume == -1) { resume = position; }
                    position = pointer;
                    continue;
                }
                if (length > 63 || length > _packet.Length - position || name.Length + 1 + length > 253) { break; }
                if (name.Length > 0) { name.Append('.'); }
                foreach (byte value in _packet.Slice(position, length))
                {
                    if (value is 0 or (byte)'.' or > 127) { throw new InvalidDataException("Invalid classroom DNS name."); }
                    name.Append((char)value);
                }
                position += length;
            }
            throw new InvalidDataException("Invalid DNS name.");
        }
    }
}
