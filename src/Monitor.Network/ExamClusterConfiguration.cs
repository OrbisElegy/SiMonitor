// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monitor.Network;

internal sealed record ExamClusterPeer(Guid Id, Uri Origin, string[] CertificateSha256);

internal sealed record ExamClusterConfiguration(Guid ClusterId, Guid NodeId, string ListenAddress, string Database,
    string Certificate, string TrustAnchor, ExamClusterPeer Primary, ExamClusterPeer Standby, ExamClusterPeer Witness,
    string? DataKeyRing = null, string TlsProfile = "TLS13-InstitutionCA-Standard@1", string Revocation = "Online")
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        MaxDepth = 16
    };

    internal ExamClusterPeer Self => Members.Single(p => p.Id == NodeId);
    internal ExamClusterPeer OtherHost => NodeId == Primary.Id ? Standby : Primary;
    internal ExamClusterPeer[] Members => [Primary, Standby, Witness];
    internal bool IsWitness => NodeId == Witness.Id;
    internal FileStream AcquireProcessLock() =>
        new(Database + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal static ExamClusterConfiguration Load(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var config = ReadFile<ExamClusterConfiguration>(path);
        config.Validate();
        if (config.Database is ":memory:" || config.Database.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            config.Database.StartsWith("//", StringComparison.Ordinal) || config.Database.StartsWith(@"\\", StringComparison.Ordinal))
        { throw new InvalidDataException("Exam databases require local storage."); }
        return config with
        {
            Database = ExamReplicaStore.LocalPath(Path.GetFullPath(config.Database, directory)),
            Certificate = ResolveCertificate(config.Certificate, directory),
            TrustAnchor = Path.GetFullPath(config.TrustAnchor, directory),
            DataKeyRing = config.DataKeyRing is null ? null : Path.GetFullPath(config.DataKeyRing, directory)
        };
    }

    internal static T ReadFile<T>(string path) where T : class
    {
        using var file = File.OpenRead(path);
        if (file.Length is < 2 or > 65536) { throw new InvalidDataException("Invalid cluster configuration size."); }
        return JsonSerializer.Deserialize<T>(file, Json) ?? throw new InvalidDataException("Missing cluster configuration.");
    }

    internal static string ResolveCertificate(string reference, string directory) =>
        reference.StartsWith("store:", StringComparison.Ordinal) ? reference : Path.GetFullPath(reference, directory);

    private void Validate()
    {
        if (TlsProfile != "TLS13-InstitutionCA-Standard@1" || Revocation is not ("Online" or "Offline"))
        { throw new InvalidDataException("Unsupported exam TLS or revocation profile."); }
        if (ClusterId == Guid.Empty || NodeId == Guid.Empty || !IPAddress.TryParse(ListenAddress, out _) ||
            string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(Certificate) || string.IsNullOrWhiteSpace(TrustAnchor))
        { throw new InvalidDataException("Missing exam node identity, address, certificate or storage."); }
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExamClusterPeer peer in Members)
        {
            if (peer is null || peer.Id == Guid.Empty || peer.Origin is null || !ClassroomPairingTicket.IsOrigin(peer.Origin) ||
                peer.Origin.HostNameType != UriHostNameType.Dns || peer.CertificateSha256 is not { Length: >= 1 and <= 2 } ||
                peer.CertificateSha256.Any(pin => pin is not { Length: 64 } || !pin.All(char.IsAsciiHexDigit) || !pins.Add(pin)))
            { throw new InvalidDataException("Each exam endpoint requires a DNS HTTPS origin and distinct SHA-256 certificate pins."); }
        }
        if (Members.Select(p => p.Id).Distinct().Count() != 3 || Members.Select(p => p.Origin).Distinct().Count() != 3 ||
            Members.All(p => p.Id != NodeId) || (IsWitness ? DataKeyRing is not null : string.IsNullOrWhiteSpace(DataKeyRing)))
        { throw new InvalidDataException("Invalid three-node exam roster or host data-key assignment."); }
    }
}
