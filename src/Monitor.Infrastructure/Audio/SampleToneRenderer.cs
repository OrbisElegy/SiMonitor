// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

public enum ToneScheduleResult { Accepted, Expired, Duplicate, Full }

// Single scheduler-thread owner. Commands are already resolved by the future
// AudioDirector; this layer has no patient/alarm priority or wall-clock policy.
// Position is the next unrendered 48kHz MONO frame, not the device play cursor.
public sealed class SampleToneRenderer
{
    private const int VoiceLimit = 8;
    private readonly Slot?[] _slots = new Slot[VoiceLimit];

    public SampleToneRenderer(long initialFrame = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialFrame);
        Position = initialFrame;
    }

    public long Position { get; private set; }

    // expiryFrame is an exclusive latest-start deadline, not a sound cutoff.
    // A valid late command starts at the unrendered frontier. Cue-specific
    // expiry (e.g. beat <=250ms), global dedup and epoch checks belong upstream.
    public ToneScheduleResult Schedule(long cancellationKey, TonePreset preset, long targetFrame, long expiryFrame)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(targetFrame);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiryFrame, targetFrame);
        if (expiryFrame <= Position) { return ToneScheduleResult.Expired; }
        int free = -1;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } slot)
            {
                if (slot.Key == cancellationKey) { return ToneScheduleResult.Duplicate; }
            }
            else if (free < 0) { free = i; }
        }
        if (free < 0) { return ToneScheduleResult.Full; }
        _slots[free] = new(cancellationKey, Math.Max(Position, targetFrame), new ToneVoice(preset));
        return ToneScheduleResult.Accepted;
    }

    // Pending cues disappear; started cues use ToneVoice's <=5ms fade from
    // the next unrendered frame. Already submitted device PCM is not recalled.
    public void Cancel(long cancellationKey)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not { } slot || slot.Key != cancellationKey) { continue; }
            if (slot.Start >= Position) { _slots[i] = null; }
            else { slot.Voice.Cancel(); }
        }
    }

    // On a forward clock discontinuity drop all old commands, including future
    // ones. Caller must also retire queued PCM; this cannot flush a device.
    public void DiscardForDiscontinuity(long nextFrame)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextFrame, Position);
        Array.Clear(_slots);
        Position = nextFrame;
    }

    public void Render(Span<float> destination)
    {
        long end = checked(Position + destination.Length);
        destination.Clear();
        Span<float> scratch = stackalloc float[256];
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not { } slot || slot.Start >= end) { continue; }
            int offset = (int)Math.Max(0, slot.Start - Position);
            while (offset < destination.Length && !slot.Voice.Finished)
            {
                int count = Math.Min(scratch.Length, destination.Length - offset);
                int rendered = slot.Voice.Render(scratch[..count]);
                for (int j = 0; j < rendered; j++) { destination[offset + j] += scratch[j]; }
                offset += count;
            }
            if (slot.Voice.Finished) { _slots[i] = null; }
        }
        // Saturate once after summing, rather than clipping each contribution.
        for (int i = 0; i < destination.Length; i++) { destination[i] = Math.Clamp(destination[i], -1, 1); }
        Position = end;
    }

    private sealed record Slot(long Key, long Start, ToneVoice Voice);
}
