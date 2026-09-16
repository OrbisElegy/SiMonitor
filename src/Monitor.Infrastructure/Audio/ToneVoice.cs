// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Infrastructure.Audio;

public sealed record TonePreset(string Id, int FrequencyMilliHz, int AttackFrames, int HoldFrames, int ReleaseFrames, int GainQ15)
{
    // The audition uses a project-authored 795 Hz tone. Envelope and
    // gain are explicit engineering audition choices, not an approved profile.
    public static TonePreset BeatAudition { get; } = new("BeatAuditionDraft@1", 795_000, 240, 4800, 720, 8192);
    public int TotalFrames => checked(AttackFrames + HoldFrames + ReleaseFrames);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128 || FrequencyMilliHz is < 20_000 or > 20_000_000 ||
            AttackFrames < 1 || HoldFrames < 0 || ReleaseFrames < 1 ||
            (long)AttackFrames + HoldFrames + ReleaseFrames > 480_000 || GainQ15 is < 0 or > 16384)
        { throw new ArgumentException("AudioTone.InvalidPreset", nameof(TonePreset)); }
    }
}

public sealed record ToneVoiceState(TonePreset Preset, int Frame, int? CancelFrame);

// Single-owner mono voice. Render does no I/O, allocation or locking and is
// independent of UI, wall clocks and device sample rate. Output is float32.
public sealed class ToneVoice
{
    public const int SampleRate = 48_000;
    private const long PhaseDenominator = SampleRate * 1000L;
    private readonly TonePreset _preset;
    private int _frame;
    private int? _cancelFrame;
    private int EndFrame => _cancelFrame is { } start ? (start == 0 ? 0 : Math.Min(_preset.TotalFrames, start + 240)) : _preset.TotalFrames;

    public ToneVoice(TonePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.Validate(); _preset = preset;
        _ = SineTable.Quarter[0]; // Prepare table storage outside Render/callback.
    }

    public bool Finished => _frame == EndFrame;
    public void Cancel() => _cancelFrame ??= _frame;
    public ToneVoiceState CaptureState() => new(_preset, _frame, _cancelFrame);
    // For deterministic offline continuation, not permission to replay a stale
    // event after reconnect. Cue expiry/deduplication belong to AudioDirector.
    public static ToneVoice Restore(ToneVoiceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var voice = new ToneVoice(state.Preset);
        if (state.Frame < 0 || state.Frame > state.Preset.TotalFrames) { throw new ArgumentException("AudioTone.InvalidFrame", nameof(state)); }
        voice._frame = state.Frame; voice._cancelFrame = state.CancelFrame;
        if (state.CancelFrame is { } cancelled && (cancelled < 0 || cancelled > state.Frame || state.Frame > voice.EndFrame))
        { throw new ArgumentException("AudioTone.InvalidCancellation", nameof(state)); }
        return voice;
    }

    public int Render(Span<float> destination)
    {
        int count = Math.Min(destination.Length, EndFrame - _frame);
        for (int i = 0; i < count; i++, _frame++)
        {
            long phase = (long)_frame * _preset.FrequencyMilliHz % PhaseDenominator * 1024;
            int index = (int)(phase / PhaseDenominator);
            long fraction = phase % PhaseDenominator;
            long first = Lookup(index), next = Lookup((index + 1) & 1023);
            long sine = first + (long)FixedPointMath.RoundDivideTiesToEven((Int128)(next - first) * fraction, PhaseDenominator);
            long numerator = 1, denominator = 1;
            int envelopeFrame = _cancelFrame ?? _frame;
            if (envelopeFrame < _preset.AttackFrames) { numerator = envelopeFrame; denominator = _preset.AttackFrames; }
            else if (envelopeFrame >= _preset.AttackFrames + _preset.HoldFrames)
            { numerator = _preset.TotalFrames - 1 - envelopeFrame; denominator = _preset.ReleaseFrames; }
            long q31 = (long)FixedPointMath.RoundDivideTiesToEven((Int128)sine * _preset.GainQ15 * numerator, 32768L * denominator);
            if (_cancelFrame is { } cancelled)
            { q31 = (long)FixedPointMath.RoundDivideTiesToEven((Int128)q31 * (EndFrame - 1 - _frame), EndFrame - cancelled); }
            destination[i] = q31 / 2147483648f;
        }
        destination[count..].Clear();
        return count;
    }

    private static long Lookup(int index)
    {
        int offset = index & 255;
        return (index >> 8) switch
        {
            0 => SineTable.Quarter[offset],
            1 => SineTable.Quarter[256 - offset],
            2 => -(long)SineTable.Quarter[offset],
            _ => -(long)SineTable.Quarter[256 - offset],
        };
    }
}
