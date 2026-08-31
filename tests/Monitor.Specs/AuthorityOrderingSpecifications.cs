// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Monitor.Domain.Authority;

namespace Monitor.Specs;

internal static class AuthorityOrderingSpecifications
{
    private const string GoldenCommitSha256 =
        "d24ac7428740d5b145d8109e053508bc310be83a8805e3599f59f5898966e27e";

    public static Specification[] All =>
    [
        new(nameof(GoldenCandidateOrderSurvivesAllPermutations),
            GoldenCandidateOrderSurvivesAllPermutations),
        new(nameof(FrozenEventRegistryMatchesEveryRank), FrozenEventRegistryMatchesEveryRank),
        new(nameof(EveryTupleFieldBreaksTiesInFrozenDirection),
            EveryTupleFieldBreaksTiesInFrozenDirection),
        new(nameof(InvalidAndAmbiguousCandidateKeysReject), InvalidAndAmbiguousCandidateKeysReject),
    ];

    private static void GoldenCandidateOrderSurvivesAllPermutations()
    {
        Candidate[] candidates =
        [
            New("momentary-lease-expired", 0, "lease.shock", 0),
            New("interaction-released", 0, "interaction.shock", 0),
            New("intrinsic-qrs", 0, "qrs.intrinsic", 19),
            New("therapy-sense-accepted", 0, "sense.sync", 3),
            New("pacing-stimulus-candidate", 0, "pacer.candidate", 7),
            New("synchronized-shock-delivery", 0, "shock.delivery", 1),
            New("scenario-effect-committed", 10, "effect.alpha", 2),
            New("scenario-effect-committed", 20, "effect.beta", 1),
            New("checkpoint-written", 0, "checkpoint.main", 64),
        ];
        string[] expectedEvents =
        [
            "momentary-lease-expired",
            "interaction-released",
            "intrinsic-qrs",
            "therapy-sense-accepted",
            "pacing-stimulus-candidate",
            "synchronized-shock-delivery",
            "scenario-effect-committed",
            "scenario-effect-committed",
            "checkpoint-written",
        ];

        IReadOnlyList<Candidate> expected = Order(candidates);
        Check.That(expected.Select(item => item.Key.EventType).SequenceEqual(expectedEvents),
            "candidate order must match the frozen safety-to-checkpoint sequence");
        Check.That(expected[6].Key.StableId == "effect.beta" &&
            expected[7].Key.StableId == "effect.alpha",
            "higher explicit scenario priority must commit first");
        Check.That(HashCanonicalCandidates(expected) == GoldenCommitSha256,
            "the canonical ordered candidate bytes must match the frozen golden hash");

        for (int seed = 1; seed <= 128; seed++)
        {
            IReadOnlyList<Candidate> actual = Order(Permute(candidates, seed));
            Check.That(HashCanonicalCandidates(actual) == GoldenCommitSha256,
                $"candidate permutation must not affect commit bytes: {seed}");
        }
    }

    private static void FrozenEventRegistryMatchesEveryRank()
    {
        EventVector[] vectors =
        [
            new("authority-epoch-changed", 0, 0),
            new("permission-revoked", 0, 10),
            new("safety-fault-raised", 0, 20),
            new("momentary-lease-expired", 0, 30),
            new("interaction-released", 10, 0),
            new("operation-cancelled", 10, 10),
            new("therapy-disarmed", 10, 20),
            new("session-ended", 20, 0),
            new("session-paused", 20, 10),
            new("configuration-committed", 30, 0),
            new("intrinsic-qrs", 100, 0),
            new("acquisition-quality-changed", 110, 0),
            new("estimate-committed", 120, 0),
            new("rate-source-selected", 130, 0),
            new("therapy-sense-accepted", 140, 0),
            new("pacing-stimulus-candidate", 150, 0),
            new("synchronized-shock-delivery", 160, 0),
            new("therapy-response-committed", 170, 0),
            new("scenario-trigger-matched", 180, 0, true),
            new("scenario-effect-committed", 190, 0, true),
            new("alarm-evaluated", 200, 0),
            new("numeric-published", 210, 0),
            new("projection-emitted", 220, 0),
            new("prompt-intent-emitted", 230, 0),
            new("assessment-evidence-recorded", 240, 0),
            new("checkpoint-written", 250, 0),
        ];

        foreach (EventVector vector in vectors)
        {
            AuthorityCandidateKey key = new(
                0,
                vector.EventType,
                vector.AllowsExplicitPriority ? 1 : 0,
                "candidate.registry",
                0);
            Check.That(key.PhaseRank == vector.PhaseRank &&
                key.EventTypeRank == vector.EventTypeRank,
                $"event registry rank must remain frozen: {vector.EventType}");
        }
    }

    private static void InvalidAndAmbiguousCandidateKeysReject()
    {
        Check.That(CandidateReason(() => _ = new AuthorityCandidateKey(
            0, "unregistered-event", 0, "candidate.valid", 0)) ==
            "AuthorityCandidate.UnregisteredEventType",
            "an event outside the closed registry must reject");
        Check.That(CandidateReason(() => _ = new AuthorityCandidateKey(
            0, "intrinsic-qrs", 1, "candidate.valid", 0)) ==
            "AuthorityCandidate.ExplicitPriorityForbidden",
            "an undeclared explicit priority must reject");
        Check.That(CandidateReason(() => _ = new AuthorityCandidateKey(
            0, "intrinsic-qrs", 0, "Candidate.invalid", 0)) ==
            "AuthorityCandidate.InvalidStableId",
            "a non-ASCII-ordinal StableId must reject");

        Candidate duplicate = New("intrinsic-qrs", 0, "qrs.duplicate", 7);
        Check.That(CandidateReason(() => Order([duplicate, duplicate])) ==
            "AuthorityCandidate.DuplicateOrderKey",
            "equal total-order keys must reject instead of inheriting input order");
    }

    private static void EveryTupleFieldBreaksTiesInFrozenDirection()
    {
        AuthorityCandidateKeyComparer comparer = AuthorityCandidateKeyComparer.Instance;
        Check.That(comparer.Compare(
            Key(9, "checkpoint-written", 0, "checkpoint.early", 0),
            Key(10, "authority-epoch-changed", 0, "epoch.late", 0)) < 0,
            "simulation time must be ascending before every other field");
        Check.That(comparer.Compare(
            Key(10, "interaction-released", 0, "interaction.release", 0),
            Key(10, "intrinsic-qrs", 0, "qrs.intrinsic", 0)) < 0,
            "phase rank must be ascending");
        Check.That(comparer.Compare(
            Key(10, "authority-epoch-changed", 0, "epoch.changed", 0),
            Key(10, "permission-revoked", 0, "permission.revoked", 0)) < 0,
            "event type rank must be ascending within a phase");
        Check.That(comparer.Compare(
            Key(10, "scenario-effect-committed", 20, "effect.z", 0),
            Key(10, "scenario-effect-committed", 10, "effect.a", 0)) < 0,
            "explicit priority must be descending");
        Check.That(comparer.Compare(
            Key(10, "scenario-effect-committed", 10, "effect.a", 9),
            Key(10, "scenario-effect-committed", 10, "effect.b", 0)) < 0,
            "StableId must use ascending ASCII ordinal order before local sequence");
        Check.That(comparer.Compare(
            Key(10, "scenario-effect-committed", 10, "effect.same", 1),
            Key(10, "scenario-effect-committed", 10, "effect.same", 2)) < 0,
            "local sequence must be ascending as the final tie-break");
    }

    private static Candidate New(
        string eventType,
        int explicitPriority,
        string stableId,
        ulong localSequence) => new(new AuthorityCandidateKey(
            5_000_000_000,
            eventType,
            explicitPriority,
            stableId,
            localSequence));

    private static AuthorityCandidateKey Key(
        long simTimeNs,
        string eventType,
        int explicitPriority,
        string stableId,
        ulong localSequence) => new(
            simTimeNs,
            eventType,
            explicitPriority,
            stableId,
            localSequence);

    private static IReadOnlyList<Candidate> Order(IEnumerable<Candidate> candidates) =>
        AuthorityCandidateOrdering.Order(candidates, candidate => candidate.Key);

    private static Candidate[] Permute(IReadOnlyList<Candidate> candidates, int seed)
    {
        Candidate[] result = [.. candidates];
        ulong value = (uint)seed & 0x7fffffff;
        for (int index = result.Length - 1; index > 0; index--)
        {
            value = (1_103_515_245 * value + 12_345) & 0x7fffffff;
            int swapIndex = (int)(value % (uint)(index + 1));
            (result[index], result[swapIndex]) = (result[swapIndex], result[index]);
        }

        return result;
    }

    private static string HashCanonicalCandidates(IEnumerable<Candidate> candidates)
    {
        string body = string.Join(',', candidates.Select(candidate =>
        {
            AuthorityCandidateKey key = candidate.Key;
            return string.Create(CultureInfo.InvariantCulture,
                $"{{\"event\":\"{key.EventType}\",\"event_type_rank\":{key.EventTypeRank}," +
                $"\"explicit_priority\":{key.ExplicitPriority},\"local_seq\":{key.LocalSequence}," +
                $"\"phase_rank\":{key.PhaseRank},\"sim_time_ns\":\"{key.SimTimeNs}\"," +
                $"\"stable_id\":\"{key.StableId}\"}}");
        }));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"[{body}]\n"));
        return Convert.ToHexStringLower(digest);
    }

    private static string? CandidateReason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (AuthorityCandidateException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed record Candidate(AuthorityCandidateKey Key);

    private sealed record EventVector(
        string EventType,
        int PhaseRank,
        int EventTypeRank,
        bool AllowsExplicitPriority = false);
}
