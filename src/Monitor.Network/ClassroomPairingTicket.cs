// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Monitor.Network;

public sealed class ClassroomPairingTicket
{
    private const string Prefix = "simonitor://join/v1#";
    private readonly string _joinCode;

    private ClassroomPairingTicket(Uri origin, Guid sessionId, string certificateSha256, string code)
    {
        Origin = origin;
        SessionId = sessionId;
        CertificateSha256 = certificateSha256;
        _joinCode = code;
    }

    public Uri Origin { get; }
    public Guid SessionId { get; }
    public string CertificateSha256 { get; }
    internal string JoinCode => _joinCode;

    public static ClassroomPairingTicket Create(Uri origin, Guid sessionId, string certificateSha256, string joinCode)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!IsOrigin(origin) || origin.AbsoluteUri.Length > 512) { throw new ArgumentException("Invalid classroom HTTPS origin.", nameof(origin)); }
        if (sessionId == Guid.Empty) { throw new ArgumentException("Missing classroom identity.", nameof(sessionId)); }
        if (certificateSha256 is not { Length: 64 } || !certificateSha256.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Expected a SHA-256 leaf certificate fingerprint.", nameof(certificateSha256));
        }
        if (joinCode is not { Length: >= 8 and <= 16 } || !joinCode.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Invalid classroom pairing invitation.", nameof(joinCode));
        }
        return new ClassroomPairingTicket(origin, sessionId, certificateSha256.ToLowerInvariant(), joinCode);
    }

    internal static bool IsOrigin(Uri origin) => origin.IsAbsoluteUri && origin.Scheme == Uri.UriSchemeHttps &&
        origin.UserInfo.Length == 0 && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0 &&
        origin.Host.Length > 0 && origin.Port > 0;

    // This explicit serialization contains a short-lived credential. Neither the HTTP query nor
    // the diagnostic representation of the ticket contains it.
    public string Encode() => Prefix + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
        $"{Origin.AbsoluteUri}\n{SessionId:D}\n{CertificateSha256}\n{_joinCode}"));

    public static ClassroomPairingTicket Parse(string ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket.Length > 1024 || !ticket.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Invalid classroom pairing link.");
        }
        string encoded = ticket[Prefix.Length..];
        if (!encoded.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new FormatException("Invalid classroom pairing encoding.");
        }
        byte[] bytes = WebEncoders.Base64UrlDecode(encoded);
        string[] fields = new UTF8Encoding(false, true).GetString(bytes).Split('\n');
        if (fields.Length != 4 || !Uri.TryCreate(fields[0], UriKind.Absolute, out Uri? origin) ||
            !Guid.TryParseExact(fields[1], "D", out Guid sessionId))
        {
            throw new FormatException("Invalid classroom pairing fields.");
        }
        return Create(origin, sessionId, fields[2], fields[3]);
    }

    public override string ToString() => $"Classroom {SessionId:D} at {Origin}";
}

public sealed partial class ClassroomStudentClient
{
    public static Task<ClassroomStudentClient> JoinAsync(ClassroomPairingTicket ticket, X509Certificate2? trustedRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return JoinCoreAsync(ticket.Origin, ticket.JoinCode, trustedRoot, ticket.CertificateSha256, ticket.SessionId, null, null, cancellationToken);
    }
    public static Task<ClassroomStudentClient> JoinWithPresentationAsync(ClassroomPairingTicket ticket, JsonElement hello,
        X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return JoinCoreAsync(ticket.Origin, ticket.JoinCode, trustedRoot, ticket.CertificateSha256, ticket.SessionId, null, hello, cancellationToken);
    }

}
