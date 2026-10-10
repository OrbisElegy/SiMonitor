// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Same producer and 48 kHz timeline as monitor audio. No I/O/allocation in Mix.
// New state cancels old speech. Device reconnects may resume a bed/metronome,
// but must not replay consumed state-entry announcements.
public sealed class TherapySoundRenderer
{
    private sealed record Clip(ReadOnlyMemory<short> Samples, long StartFrame, bool Speech);
    private TherapySoundRequest? _request;
    private Clip[] _clips = [];
    private long _frame;
    private long _cprFrame;
    private long _cprIntroFrames;
    private ReadOnlyMemory<short> _click;
    private ReadOnlyMemory<short> _breath;
    private readonly ReadOnlyMemory<short> _readyHold = SelectedTherapySounds.Get("ReadyHold");
    private readonly ReadOnlyMemory<short> _relay = SelectedTherapySounds.Get("Relay");
    private long _readyHoldStartFrame;
    private int _relayFrame = int.MaxValue;
    private double _chargePhase;
    private double _chargeFrequency = 390;
    private float _lastTherapySample;
    private float _cancelSample;
    private int _cancelFrames;
    private float _readyBackgroundGain = 1;
    private const float ReadyBackgroundGain = .063095735f; // -24 dB relative to configured monitor volume.
    private const int Rate = 48000;
    private const long CompressionsFrames = 30L * Rate * 60 / 110;
    private const long CycleFrames = CompressionsFrames + 5 * Rate;
    private static readonly double ChargeSmoothing = 1 - Math.Exp(-1d / (Rate * .012));

    // Actual delivery/stimulus events only; never infer a relay from a phase change.
    public void UpdateRelay(bool enabled, bool triggered = false)
    {
        if (!enabled) { _relayFrame = int.MaxValue; }
        else if (triggered) { _relayFrame = 0; }
    }

    public void Update(TherapySoundRequest? request, bool announce = true)
    {
        request?.Validate();
        if (request == _request) { return; }
        if (request?.Revision != _request?.Revision)
        {
            _cancelSample = _lastTherapySample;
            _cancelFrames = 240;
            _frame = 0;
            // Anchor once on entry/reconnection. UI simulation snapshots and the
            // device's continuous PCM clock are not sample-synchronous; seeking
            // on each snapshot repeats/skips speech and compression clicks.
            _cprFrame = request?.CprElapsedMilliseconds * 48L ?? 0;
            _chargePhase = 0;
            _chargeFrequency = 390;
            _clips = [];
            if (request is not null)
            {
                var ids = new List<string>();
                switch (request.Phase)
                {
                    case TherapySoundPhase.Analyzing:
                        if (request.EnteringAed) { ids.Add("AED_MODE_ADULT"); }
                        if (request.AfterCpr) { ids.Add("AED_CPR_STOP"); }
                        ids.Add(request.RhythmChanged ? "AED_RHYTHM_CHANGED_ANALYZING" :
                            request.Reanalyzing ? "AED_AUTO_DISARM_ANALYZING" : "AED_ANALYZING");
                        break;
                    case TherapySoundPhase.Charging:
                        if (request.Automated) { ids.Add("AED_SHOCKABLE"); }
                        break;
                    case TherapySoundPhase.Ready:
                        if (request.Automated) { ids.Add("AED_READY"); }
                        ids.Add("Ready");
                        break;
                    case TherapySoundPhase.CprShock:
                        ids.Add("AED_SHOCK_RECORDED"); ids.Add("AED_CPR_START");
                        break;
                    case TherapySoundPhase.CprNoShock:
                        ids.Add("AED_NSA_CPR"); ids.Add("AED_CPR_START");
                        break;
                    case TherapySoundPhase.Cancelled:
                        ids.Add("AED_AUTO_DISARM");
                        break;
                }
                long at = 0;
                var clips = new List<Clip>();
                foreach (string id in ids)
                {
                    var samples = SelectedTherapySounds.Get(id, request.Locale);
                    clips.Add(new(samples, at, id != "Ready"));
                    at += samples.Length + 7680;
                }
                _cprIntroFrames = at;
                _readyHoldStartFrame = announce && request.Announce ? at : 0;
                if (announce && request.Announce) { _clips = clips.ToArray(); }
                // Force both continuous assets into memory outside Mix.
                _click = SelectedTherapySounds.Get("Cpr");
                _breath = SelectedTherapySounds.Get("AED_CPR_BREATHS", request.Locale);
            }
        }
        _request = request;
    }

    public void Mix(Span<float> samples)
    {
        var request = _request;
        bool cpr = request?.Phase is TherapySoundPhase.CprShock or TherapySoundPhase.CprNoShock;
        bool cprExpired = cpr && request!.CprElapsedMilliseconds >= 120000;
        foreach (ref float sample in samples)
        {
            float speech = 0, effect = 0;
            bool speaking = false;
            long cursor = cpr ? _cprFrame : _frame;
            if (request is not null && !cprExpired)
            {
                foreach (var clip in _clips)
                {
                    var pcm = clip.Samples.Span;
                    long index = cursor - clip.StartFrame;
                    if (index < 0 || index >= pcm.Length) { continue; }
                    if (!clip.Speech) { effect += pcm[(int)index] / 32768f; }
                    else { speaking = true; speech += pcm[(int)index] / 32768f; }
                }
                if (request.Phase == TherapySoundPhase.Charging)
                {
                    double target = 390 + 370 * request.ChargeProgressPermille / 1000d;
                    _chargeFrequency += (target - _chargeFrequency) * ChargeSmoothing;
                    _chargePhase = (_chargePhase + 2 * Math.PI * _chargeFrequency / Rate) % (2 * Math.PI);
                    effect += (float)((Math.Sin(_chargePhase) + .12 * Math.Sin(2 * _chargePhase)) * .0395 * Math.Min(1, _frame / 960d));
                }
                if (request.Phase == TherapySoundPhase.Ready && _frame >= _readyHoldStartFrame)
                {
                    var hold = _readyHold.Span;
                    effect += hold[(int)((_frame - _readyHoldStartFrame) % hold.Length)] / 32768f;
                }
                if (cpr && _cprFrame >= _cprIntroFrames && _cprFrame < 120L * Rate)
                {
                    long local = (_cprFrame - _cprIntroFrames) % CycleFrames;
                    if (local < CompressionsFrames)
                    {
                        long beat = local * 110 / (Rate * 60);
                        long index = local - beat * Rate * 60 / 110;
                        var click = _click.Span;
                        if (index < click.Length) { effect += click[(int)index] / 32768f; }
                    }
                    else
                    {
                        long index = local - CompressionsFrames;
                        var breath = _breath.Span;
                        if (index < breath.Length) { speaking = true; speech += breath[(int)index] / 32768f; }
                    }
                }
            }
            float tail = _cancelFrames > 0 ? _cancelSample * --_cancelFrames / 240f : 0;
            if (_relayFrame < _relay.Length) { effect += _relay.Span[_relayFrame++] / 32768f; }
            _lastTherapySample = speech + effect * (speaking ? .2512f : 1);
            // Stored-energy warnings retain priority throughout Ready, including
            // gaps and reconnects. Smooth attack/release avoids gain discontinuities.
            _readyBackgroundGain = request?.Phase == TherapySoundPhase.Ready
                ? Math.Max(ReadyBackgroundGain, _readyBackgroundGain - (1 - ReadyBackgroundGain) / 960)
                : Math.Min(1, _readyBackgroundGain + (1 - ReadyBackgroundGain) / 4800);
            float backgroundGain = Math.Min(_readyBackgroundGain, speaking ? .1259f : 1);
            sample = Math.Clamp(sample * backgroundGain + _lastTherapySample + tail, -1, 1);
            _frame++;
            _cprFrame++;
        }
    }
}
