// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ClassroomDiscoverySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ClassroomDiscoveryAdvertisesPublicServiceOnly), ClassroomDiscoveryAdvertisesPublicServiceOnly),
        new(nameof(ClassroomPairingLinksAreExplicitCredentials), ClassroomPairingLinksAreExplicitCredentials)
    ];

    private static void ClassroomDiscoveryAdvertisesPublicServiceOnly()
    {
        const ushort id = 4123;
        byte[] question = ClassroomDiscoveryDns.Question(id);
        var info = new ClassroomDiscoveredHost("技能课教室", new Uri("https://teacher.example:7443/"), new string('c', 64));
        Check.That(ClassroomDiscoveryDns.ReadQuestion(question, out ushort parsedId, out bool preferUnicast) && parsedId == id && !preferUnicast,
            "one-shot multicast query uses the classroom PTR service and query ID");
        byte[] answer = ClassroomDiscoveryDns.Answer(info, IPAddress.Loopback, id, true);
        Check.That(ClassroomDiscoveryDns.ReadAnswer(answer, id) == info &&
            ClassroomDiscoveryDns.ReadAnswer(answer, (ushort)(id + 1)) is null, "service discovery preserves the public name and TLS endpoint");
        for (int cutoff = 0; cutoff < answer.Length; cutoff++)
        {
            Check.That(ClassroomDiscoveryDns.ReadAnswer(answer.AsSpan(0, cutoff), id) is null, "partial DNS records cannot publish an endpoint");
        }
        byte[] circular = (byte[])question.Clone();
        circular[12] = 0xc0;
        circular[13] = 12;
        Check.That(!ClassroomDiscoveryDns.ReadQuestion(circular, out _, out _), "cyclic compressed labels are rejected");
    }

    private static void ClassroomPairingLinksAreExplicitCredentials()
    {
        var id = Guid.NewGuid();
        var origin = new Uri("https://teacher.school.example:7443/");
        string fingerprint = new string('a', 64);
        const string code = "H72BS8VC";
        var ticket = ClassroomPairingTicket.Create(origin, id, fingerprint, code);
        var copy = ClassroomPairingTicket.Parse(ticket.Encode());
        Check.That(copy.Origin == origin && copy.SessionId == id && copy.CertificateSha256 == fingerprint && copy.JoinCode == code &&
            !ticket.ToString().Contains(code, StringComparison.Ordinal), "the QR form carries one scoped invitation and diagnostics omit it");
        foreach (string bad in new[] { "http://teacher/", "https://user@teacher/", "https://teacher/path", "https://teacher/?code=1" })
        {
            bool rejected = false;
            try { ClassroomPairingTicket.Create(new Uri(bad), id, fingerprint, code); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "only plain HTTPS origins are allowed for classroom tickets");
        }
    }

    internal static async Task ExercisePairingAsync(Uri origin, X509Certificate2 root, X509Certificate2 leaf,
        ClassroomAdmission admission, CancellationToken ct)
    {
        string pin = leaf.GetCertHashString(HashAlgorithmName.SHA256);
        var student = Guid.NewGuid();
        string code = admission.IssueInvitation(student);
        var wrongPin = ClassroomPairingTicket.Create(origin, admission.SessionId, new string('0', 64), code);
        try
        {
            using var rejected = await ClassroomStudentClient.JoinAsync(wrongPin, root, ct).ConfigureAwait(false);
            Check.That(false, "a different certificate cannot redeem the QR invitation");
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.SecureConnectionError) { }
        var stale = ClassroomPairingTicket.Create(origin, Guid.NewGuid(), pin, code);
        try
        {
            using var rejected = await ClassroomStudentClient.JoinAsync(stale, root, ct).ConfigureAwait(false);
            Check.That(false, "a QR ticket is tied to a specific session");
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) { }
        var valid = ClassroomPairingTicket.Create(origin, admission.SessionId, pin, code);
        using var paired = await ClassroomStudentClient.JoinAsync(ClassroomPairingTicket.Parse(valid.Encode()), root, ct).ConfigureAwait(false);
        Check.That((await paired.GetSessionAsync(ct).ConfigureAwait(false)).StudentId == student, "only valid pairing consumes the invitation");
    }
}
