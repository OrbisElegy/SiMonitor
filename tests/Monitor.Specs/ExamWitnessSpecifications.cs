// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ExamWitnessSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ExamWitnessGrantsOneLeaseAndFencesExpiredRenewals), ExamWitnessGrantsOneLeaseAndFencesExpiredRenewals),
        new(nameof(ExamWitnessRestartQuarantinesEarlierMonotonicTime), ExamWitnessRestartQuarantinesEarlierMonotonicTime),
        new(nameof(ExamWitnessRejectsStateLossOrRollback), ExamWitnessRejectsStateLossOrRollback),
        new(nameof(ExamWitnessNeverGrantsWithoutBothDurableWrites), ExamWitnessNeverGrantsWithoutBothDurableWrites),
    ];

    private static void ExamWitnessGrantsOneLeaseAndFencesExpiredRenewals()
    {
        WithWitness((path, cluster) =>
        {
            var clock = new WitnessClock();
            using var witness = new ExamWitnessStore(path, cluster, clock);
            var first = new ExamWitnessRequest(cluster, 0, 0, 0);
            var winners = new ConcurrentBag<ExamWitnessLease>();
            Parallel.For(0, 12, _ =>
            {
                if (witness.TryAcquire(Guid.NewGuid(), first, out var grant)) { winners.Add(grant); }
            });
            var lease = winners.Single();
            Guid owner = lease.OwnerId!.Value;
            var standby = Guid.NewGuid();
            Check.That(lease.AuthorityEpoch == 1 && lease.FencingToken == 1 && lease.Revision == 1 &&
                lease.RemainingMs == 3000, "CAS grants exactly one initial three-second lease");
            clock.Advance(TimeSpan.FromMilliseconds(ExamWitnessStore.RenewIntervalMs));
            var renew = new ExamWitnessRequest(cluster, lease.Revision, lease.AuthorityEpoch, lease.FencingToken);
            Check.That(!witness.TryAcquire(standby, renew, out _) && !witness.TryRenew(standby, renew, out _) &&
                !witness.TryRenew(owner, renew with { FencingToken = 0 }, out _) &&
                !witness.TryRenew(owner, renew with { AuthorityEpoch = 0 }, out _), "competing or stale tokens cannot renew");
            Check.That(witness.TryRenew(owner, renew, out var renewed) && renewed.Revision == 2 &&
                renewed.RemainingMs == 3000 && renewed.AuthorityEpoch == lease.AuthorityEpoch &&
                renewed.FencingToken == lease.FencingToken && !witness.TryRenew(owner, renew, out _),
                "renewal keeps the fence and invalidates its original CAS revision");
            clock.Advance(TimeSpan.FromMilliseconds(3000) - TimeSpan.FromTicks(1));
            var current = renew with { ExpectedRevision = renewed.Revision };
            Check.That(witness.Read().RemainingMs == 0 && !witness.TryAcquire(standby, current, out _),
                "rounding the advertised remainder down cannot move the takeover boundary early");
            clock.Advance(TimeSpan.FromTicks(1));
            Check.That(!witness.TryRenew(owner, current, out _) &&
                witness.TryAcquire(standby, current with { AuthorityEpoch = 8, FencingToken = 13 }, out var promoted) &&
                promoted.AuthorityEpoch == 9 && promoted.FencingToken == 14 && promoted.Revision == 3,
                "promotion exceeds both witness state and a higher manually fenced host frontier");
            Check.That(!witness.TryRenew(owner, current with { ExpectedRevision = 3 }, out _),
                "the former primary cannot fence the promoted standby");
        });
    }

    private static void ExamWitnessRestartQuarantinesEarlierMonotonicTime()
    {
        WithWitness((path, cluster) =>
        {
            var primary = Guid.NewGuid();
            var standby = Guid.NewGuid();
            var initial = new ExamWitnessRequest(cluster, 0, 0, 0);
            using (var witness = new ExamWitnessStore(path, cluster, new WitnessClock()))
            {
                Check.That(witness.TryAcquire(primary, initial, out _), "first owner was durably granted");
                Expect<SqliteException>(() => { using var duplicate = new ExamWitnessStore(path, cluster); });
            }
            var rebootedClock = new WitnessClock();
            rebootedClock.Advance(TimeSpan.FromDays(300));
            using var restarted = new ExamWitnessStore(path, cluster, rebootedClock);
            var previous = restarted.Read();
            Check.That(previous.OwnerId == primary && previous.RemainingMs == 0 && previous.FencingToken == 1 &&
                previous.AuthorityEpoch == 1 && previous.Revision == 1, "a restart keeps the fence but trusts no old monotonic timestamp");
            var request = new ExamWitnessRequest(cluster, previous.Revision, previous.AuthorityEpoch, previous.FencingToken);
            rebootedClock.Advance(TimeSpan.FromMilliseconds(2999));
            Check.That(!restarted.TryAcquire(standby, request, out _) && !restarted.TryRenew(primary, request, out _),
                "no grant or renewal overlaps an unexpired pre-restart lease");
            rebootedClock.Advance(TimeSpan.FromMilliseconds(1));
            Check.That(restarted.TryAcquire(standby, request, out var granted) && granted.AuthorityEpoch == 2 &&
                granted.FencingToken == 2, "safe restart promotion advances the persisted pair");
        });
    }

    private static void ExamWitnessRejectsStateLossOrRollback()
    {
        WithWitness((path, cluster) =>
        {
            Expect<IOException>(() => ExamWitnessStore.Initialize(path, cluster));
            Expect<InvalidDataException>(() => { using var wrong = new ExamWitnessStore(path, Guid.NewGuid()); });
            string backup = path + ".before";
            File.Copy(path, backup);
            using (var witness = new ExamWitnessStore(path, cluster))
            {
                Check.That(witness.TryAcquire(Guid.NewGuid(), new(cluster, 0, 0, 0), out _), "grant precedes rollback");
            }
            File.Copy(backup, path, overwrite: true);
            Expect<InvalidDataException>(() => { using var stale = new ExamWitnessStore(path, cluster); });
            File.Delete(path);
            Expect<FileNotFoundException>(() => { using var missing = new ExamWitnessStore(path, cluster); });
            Expect<IOException>(() => ExamWitnessStore.Initialize(path, cluster));
            Check.That(!File.Exists(path), "start and repeated provisioning preserve the remaining watermark");
        });
    }

    private static void ExamWitnessNeverGrantsWithoutBothDurableWrites()
    {
        WithWitness((path, cluster) =>
        {
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path + ".watermark",
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                db.Open();
                using var simulate = db.CreateCommand();
                simulate.CommandText = """
                    CREATE TRIGGER disk_failure BEFORE UPDATE ON anchor
                    BEGIN SELECT RAISE(ABORT, 'simulated witness storage failure'); END;
                    """;
                simulate.ExecuteNonQuery();
            }
            using (var witness = new ExamWitnessStore(path, cluster))
            {
                Expect<SqliteException>(() => witness.TryAcquire(Guid.NewGuid(), new(cluster, 0, 0, 0), out _));
                Check.That(witness.Read() == new ExamWitnessLease(cluster, null, 0, 0, 0, 0),
                    "failure in the second durable update rolls back the main lease");
            }
            using var reopened = new ExamWitnessStore(path, cluster);
            Check.That(reopened.Read().Revision == 0, "no partial grant survives process restart");
        });
    }

    private static void WithWitness(Action<string, Guid> check)
    {
        var directory = Directory.CreateTempSubdirectory("simonitor-exam-witness-");
        try
        {
            string path = Path.Combine(directory.FullName, "witness.db");
            var cluster = Guid.NewGuid();
            ExamWitnessStore.Initialize(path, cluster);
            check(path, cluster);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class WitnessClock : TimeProvider
    {
        private long _ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock is not a witness lease clock.");
        internal void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
