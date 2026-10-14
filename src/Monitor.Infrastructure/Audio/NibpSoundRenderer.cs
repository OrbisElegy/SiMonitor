// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Audio;

public sealed record NibpSoundRequest(NibpPhase Phase, int RemainingMilliseconds)
{
    public void Validate()
    {
        if (Phase is not (NibpPhase.Inflating or NibpPhase.Deflating) || RemainingMilliseconds is < 1 or > 15000)
        { throw new ArgumentException("NibpSound.InvalidRequest"); }
    }
}

// Original synthesized pump motor and air-valve sounds. Same 48 kHz producer
// as monitor audio; no separate device, files or allocation in Mix.
public sealed class NibpSoundRenderer
{
    private NibpSoundRequest? _request;
    private int _remainingFrames;
    private long _frame;
    private uint _noise = 0x73A9B125;
    private float _filteredNoise;
    private float _airflowNoise;
    private int _envelopeFrames;

    public void Update(NibpSoundRequest? request)
    {
        request?.Validate();
        if (request == _request) { return; }
        if (request?.Phase != _request?.Phase) { _frame = 0; }
        _request = request;
        _remainingFrames = request?.RemainingMilliseconds * 48 ?? 0;
    }

    public void Mix(Span<float> destination)
    {
        if (_remainingFrames == 0 && _envelopeFrames == 0) { return; }
        foreach (ref float sample in destination)
        {
            if (_remainingFrames == 0 && _envelopeFrames == 0) { break; }
            bool active = _request is not null && _remainingFrames > 0;
            _envelopeFrames = active ? Math.Min(480, _envelopeFrames + 1) : Math.Max(0, _envelopeFrames - 1);
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            float noise = (_noise & 65535) / 32768f - 1;
            _filteredNoise += .12f * (noise - _filteredNoise);
            _airflowNoise += .18f * (noise - _airflowNoise);
            double seconds = _frame / 48000d;
            float cuff;
            if (_request?.Phase == NibpPhase.Inflating)
            {
                double motor = Math.Sin(2 * Math.PI * 96 * seconds) + .4 * Math.Sin(2 * Math.PI * 192 * seconds);
                double pulse = .7 + .3 * Math.Sin(2 * Math.PI * 23 * seconds);
                cuff = (float)(.055 * motor * pulse) + .025f * _filteredNoise;
            }
            else
            {
                // Follow the measurement clock so resuming midway also restores the valve stage.
                int deflationFrames = Math.Max(0, 9000 * 48 - _remainingFrames);
                double deflationSeconds = deflationFrames / 48000d;
                double valve;
                if (deflationSeconds < 6)
                {
                    // Brief, rounded releases separated by actual closed-valve silence.
                    int cycleFrames = deflationFrames % 36000;
                    valve = cycleFrames < 14400 ? Math.Pow(Math.Sin(Math.PI * cycleFrames / 14400), 2) : 0;
                }
                else
                {
                    // Final continuous release softens as the remaining cuff pressure falls.
                    double releaseSeconds = deflationSeconds - 6;
                    double opening = Math.Min(1, releaseSeconds / .08);
                    double closing = Math.Clamp(_remainingFrames / (48000d * .12), 0, 1);
                    valve = opening * closing * (1 - .35 * Math.Min(1, releaseSeconds / 3));
                }
                cuff = .05f * _airflowNoise * (float)valve;
            }
            sample = Math.Clamp(sample + cuff * (_envelopeFrames / 480f), -1, 1);
            if (_remainingFrames > 0) { _remainingFrames--; }
            _frame++;
        }
    }
}
