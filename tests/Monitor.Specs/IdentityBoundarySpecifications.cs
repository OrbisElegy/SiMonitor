// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;

namespace Monitor.Specs;

internal static class IdentityBoundarySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MalformedPasswordVerifierFieldsFailClosed), MalformedPasswordVerifierFieldsFailClosed),
        new(nameof(PasswordVerifierKeepsSupportedEncodingAndPasswordChecks), PasswordVerifierKeepsSupportedEncodingAndPasswordChecks),
        new(nameof(FailureRecordingReportsAnExistingLock), FailureRecordingReportsAnExistingLock),
        new(nameof(DelayedFailuresDoNotShortenExistingLocks), DelayedFailuresDoNotShortenExistingLocks),
        new(nameof(InvalidFailureTimesDoNotAppendAudit), InvalidFailureTimesDoNotAppendAudit),
        new(nameof(NullFailureSubjectsDoNotChangeAuditOrCounts), NullFailureSubjectsDoNotChangeAuditOrCounts),
        new(nameof(OutOfOrderFailuresPreserveLaterAttempts), OutOfOrderFailuresPreserveLaterAttempts),
        new(nameof(FailureWindowsExcludeFutureAndExpiredAttempts), FailureWindowsExcludeFutureAndExpiredAttempts),
    ];

    private static void MalformedPasswordVerifierFieldsFailClosed()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var verifier = new PasswordVerifier("PBKDF2-HMAC-SHA-256", IdentityPolicy.PasswordIterations,
            Convert.ToBase64String(new byte[IdentityPolicy.PasswordSaltBytes]),
            Convert.ToBase64String(new byte[IdentityPolicy.PasswordSubkeyBytes]));
        var missing = JsonSerializer.Deserialize<PasswordVerifier>("""
            {"Algorithm":"PBKDF2-HMAC-SHA-256","Iterations":600000}
            """)!;
        foreach (var invalid in new[]
        {
            missing,
            verifier with { SaltBase64 = null! },
            verifier with { SubkeyBase64 = null! },
            verifier with { SaltBase64 = "not-base64" },
            verifier with { SubkeyBase64 = "AA==" },
            verifier with { SaltBase64 = new string('A', 65536) },
            verifier with { SubkeyBase64 = new string('A', 65536) },
        })
        {
            Check.That(!hasher.IsSupported(invalid) && !hasher.Verify("fixture-password", invalid),
                "missing, malformed and wrong-sized verifier fields reject without throwing or running password derivation");
        }
    }

    private static void PasswordVerifierKeepsSupportedEncodingAndPasswordChecks()
    {
        var hasher = new Pbkdf2PasswordHasher();
        const string password = "identity-boundary-fixture";
        var verifier = hasher.Hash(password);
        var whitespace = verifier with
        {
            SaltBase64 = " \r\n" + verifier.SaltBase64 + "\t",
            SubkeyBase64 = "\t" + verifier.SubkeyBase64 + " \n"
        };
        Check.That(hasher.IsSupported(whitespace) && hasher.Verify(password, whitespace) &&
            !hasher.Verify("different-fixture-password", verifier),
            "fixed-size decoding preserves supported Base64 whitespace and fixed-time password verification");
    }

    private static void FailureRecordingReportsAnExistingLock()
    {
        var tracker = new InMemoryAuthenticationFailureTracker();
        var start = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < IdentityPolicy.FailureLimit; index++)
        {
            tracker.RecordFailure(Failure(start.AddMinutes(index * 2)));
        }
        DateTimeOffset expected = start.AddMinutes(8) + IdentityPolicy.LockDuration;
        var recorded = tracker.RecordFailure(Failure(start.AddMinutes(22)));
        Check.That(recorded.IsLocked && recorded.LockedUntilUtc == expected &&
            tracker.GetLockState("TEACHER", "192.0.2.1", start.AddMinutes(22)) == recorded,
            "pruning the failure window does not hide an independently active account or source lock");
    }

    private static void DelayedFailuresDoNotShortenExistingLocks()
    {
        var tracker = new InMemoryAuthenticationFailureTracker();
        var now = new DateTimeOffset(2026, 10, 23, 0, 20, 0, TimeSpan.Zero);
        for (int index = 0; index < IdentityPolicy.FailureLimit; index++)
        {
            tracker.RecordFailure(Failure(now));
        }
        DateTimeOffset expected = now + IdentityPolicy.LockDuration;
        for (int index = 0; index < IdentityPolicy.FailureLimit; index++)
        {
            tracker.RecordFailure(Failure(now.AddMinutes(-1)));
        }
        Check.That(tracker.GetLockState("TEACHER", "192.0.2.2", now).LockedUntilUtc == expected &&
            tracker.GetLockState("OTHER", "192.0.2.1", now).LockedUntilUtc == expected,
            "late completion of older failures cannot shorten either account or source lock");
    }

    private static void InvalidFailureTimesDoNotAppendAudit()
    {
        foreach (var timestamp in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue })
        {
            var tracker = new InMemoryAuthenticationFailureTracker();
            bool rejected = false;
            try { tracker.RecordFailure(Failure(timestamp)); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected && tracker.AuditRecords.Count == 0,
                "invalid failure-window or lock-expiry arithmetic rejects before publishing an audit");
        }
    }

    private static void NullFailureSubjectsDoNotChangeAuditOrCounts()
    {
        var now = new DateTimeOffset(2026, 10, 23, 0, 20, 0, TimeSpan.Zero);
        foreach (var invalid in new[]
        {
            Failure(now) with { CanonicalUsername = null! },
            Failure(now) with { SourceAddress = null! },
        })
        {
            var tracker = new InMemoryAuthenticationFailureTracker();
            for (int index = 0; index < IdentityPolicy.FailureLimit - 2; index++)
            {
                tracker.RecordFailure(Failure(now));
            }
            var before = tracker.AuditRecords;
            bool rejected = false;
            try { tracker.RecordFailure(invalid); }
            catch (ArgumentException error) { rejected = error.ParamName == "audit"; }
            Check.That(rejected && tracker.AuditRecords.SequenceEqual(before),
                "null account or source rejects before appending an authentication audit");
            Check.That(!tracker.RecordFailure(Failure(now)).IsLocked && tracker.RecordFailure(Failure(now)).IsLocked,
                "a rejected failure cannot partially increment either failure dimension");
        }
        var compatible = new InMemoryAuthenticationFailureTracker();
        Check.That(!compatible.RecordFailure(Failure(now) with { CanonicalUsername = "", SourceAddress = "" }).IsLocked,
            "the in-memory adapter preserves its existing acceptance of empty failure subjects");
    }

    private static AuthenticationFailureAuditRecord Failure(DateTimeOffset recordedAtUtc) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "TEACHER", "192.0.2.1",
            InstitutionAuthenticationContext.Teaching, null, "identity.password.invalid", recordedAtUtc);

    private static void OutOfOrderFailuresPreserveLaterAttempts() => ForEachFailureTracker(tracker =>
    {
        var now = new DateTimeOffset(2026, 10, 23, 0, 20, 0, TimeSpan.Zero);
        int[] offsets = [0, 0, -1, 0, 0];
        for (int index = 0; index < offsets.Length; index++)
        {
            var result = tracker.RecordFailure(Failure(now.AddMinutes(offsets[index])));
            Check.That(result.IsLocked == (index == offsets.Length - 1),
                "an older failure completing late retains newer attempts and the fifth committed failure establishes the lock");
        }
        DateTimeOffset expected = now + IdentityPolicy.LockDuration;
        Check.That(tracker.GetLockState("TEACHER", "192.0.2.2", now).LockedUntilUtc == expected &&
            tracker.GetLockState("OTHER", "192.0.2.1", now).LockedUntilUtc == expected,
            "out-of-order failures preserve independent account and source throttling");
    });

    private static void FailureWindowsExcludeFutureAndExpiredAttempts()
    {
        ForEachFailureTracker(tracker =>
        {
            var now = new DateTimeOffset(2026, 10, 23, 0, 20, 0, TimeSpan.Zero);
            for (int index = 0; index < IdentityPolicy.FailureLimit - 1; index++)
            {
                tracker.RecordFailure(Failure(now));
            }
            Check.That(!tracker.RecordFailure(Failure(now.AddMinutes(-1))).IsLocked,
                "a delayed request does not count failures outside its own event-time window");
            Check.That(tracker.RecordFailure(Failure(now)).IsLocked,
                "the later event-time window still contains the preserved attempts");
        });
        ForEachFailureTracker(tracker =>
        {
            var now = new DateTimeOffset(2026, 10, 23, 0, 20, 0, TimeSpan.Zero);
            tracker.RecordFailure(Failure(now - IdentityPolicy.FailureWindow));
            for (int index = 0; index < IdentityPolicy.FailureLimit - 1; index++)
            {
                Check.That(!tracker.RecordFailure(Failure(now)).IsLocked,
                    "an attempt exactly at the start of the failure window remains excluded");
            }
            Check.That(tracker.RecordFailure(Failure(now)).IsLocked,
                "five attempts at the inclusive end of the window establish a lock");
        });
    }

    private static void ForEachFailureTracker(Action<IAuthenticationFailureTracker> action)
    {
        action(new InMemoryAuthenticationFailureTracker());
        string directory = Path.Combine(Path.GetTempPath(), "monitor-identity-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var protector = new AesGcmIdentityPayloadProtector(new byte[AesGcmIdentityPayloadProtector.MasterKeySize]);
            action(new SqliteAuthenticationFailureTracker(Path.Combine(directory, "identity.sqlite"), protector));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
