// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed record ClassroomTimebase(ulong Revision, ulong TimebaseEpoch, long SimAnchorNs, long HostAnchorNs,
    int RateNumerator, int RateDenominator, string RunState, long PlayoutDelayNs)
{
    public JsonElement ToTimebaseState() => JsonSerializer.SerializeToElement(new
    {
        schema_id = "TimebaseState@1",
        timebase_epoch = TimebaseEpoch.ToString(CultureInfo.InvariantCulture),
        revision = Revision.ToString(CultureInfo.InvariantCulture),
        sim_time_ns = SimAnchorNs.ToString(CultureInfo.InvariantCulture),
        host_monotonic_anchor_ns = HostAnchorNs.ToString(CultureInfo.InvariantCulture),
        rate_numerator = RateNumerator.ToString(CultureInfo.InvariantCulture),
        rate_denominator = RateDenominator.ToString(CultureInfo.InvariantCulture),
        run_state = RunState,
        changed_at_authority_monotonic_ns = HostAnchorNs.ToString(CultureInfo.InvariantCulture)
    });

    public long PresentationDeadlineNs(long simTimeNs)
    {
        if (simTimeNs < SimAnchorNs || RunState != "Running") { return 0; }
        return checked(HostAnchorNs + PlayoutDelayNs + (long)(((Int128)(simTimeNs - SimAnchorNs) * RateDenominator + RateNumerator - 1) / RateNumerator));
    }

    public long SimTimeAt(long hostMonotonicNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hostMonotonicNs);
        return RunState == "Running" && hostMonotonicNs > HostAnchorNs
            ? checked(SimAnchorNs + (long)((Int128)(hostMonotonicNs - HostAnchorNs) * RateNumerator / RateDenominator))
            : SimAnchorNs;
    }
}

internal sealed class ClassroomTimeline(long startSimTimeNs, ulong epoch)
{
    internal ClassroomTimebase Current { get; private set; } = new(1, epoch, startSimTimeNs, 0, 1, 1, "Paused", 500_000_000);
    internal ClassroomTimebase? Pending { get; private set; }
    internal bool Started { get; private set; }

    internal ClassroomTimeline Fork() => new(Current.SimAnchorNs, Current.TimebaseEpoch)
    {
        Current = Current,
        Pending = Pending,
        Started = Started
    };

    internal ClassroomTimebase Schedule(string state, long now, long publishedUntilNs, int numerator, int denominator, long delayNs)
    {
        if (Current.RunState == "Ended" || Pending is not null) { throw new InvalidOperationException("A classroom transition is unavailable or pending."); }
        if (state is not ("Running" or "Paused") || numerator is < 1 or > 16 || denominator is < 1 or > 16 ||
            delayNs is < 100_000_000 or > 3_000_000_000)
        { throw new ArgumentException("Invalid classroom timeline change."); }
        long effective = checked(now + 1_000_000_000);
        long sim = Math.Max(publishedUntilNs, Current.SimTimeAt(effective));
        long block = WaveformBlockAssembler.BlockDurationNs;
        if (Current.RunState == "Running")
        {
            sim = checked((sim + block - 1) / block * block);
            effective = checked(Current.HostAnchorNs + (long)(((Int128)(sim - Current.SimAnchorNs) * Current.RateDenominator +
                Current.RateNumerator - 1) / Current.RateNumerator));
        }
        Pending = new ClassroomTimebase(checked(Current.Revision + 1), epoch, sim, effective, numerator, denominator, state, delayNs);
        Started = true;
        return Pending;
    }

    internal long Target(long now)
    {
        if (Pending is { } change && now >= change.HostAnchorNs)
        {
            Current = change;
            Pending = null;
        }
        return Current.SimTimeAt(now);
    }

    internal void End(long now, long sim)
    {
        Pending = null;
        Current = Current with { Revision = checked(Current.Revision + 1), SimAnchorNs = sim, HostAnchorNs = now, RunState = "Ended" };
    }
}
