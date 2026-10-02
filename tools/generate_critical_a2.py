#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""A2 Critical audition: preserve A's spectrum, 1s decaying ding + 0.5s gap.

Offline only; does not change live scheduling or mix lower-priority events.
"""
import argparse
import json
import math
from pathlib import Path
import array
import sys
import wave

from generate_alarm_auditions import ROOT, pulse, write_wave


def generate(destination):
    config = json.loads((ROOT / 'eng/audio/alarm-audition-candidates.json').read_text(encoding='utf-8'))
    candidate = next(c for c in config['candidates'] if c['id'] == 'A')
    rate = config['sample_rate_hz']
    reference = pulse(config, candidate, 'Critical', 0)
    length, period, prelude, count = rate, rate * 3 // 2, rate * 3 // 10, 6
    attack, end_fade = round(.012 * rate), round(.060 * rate)
    voice = []
    for i in range(length):
        t = i / rate
        onset = .5 - .5 * math.cos(math.pi * i / attack) if i < attack else 1
        decay = math.exp(-max(0, i - attack) / rate / .32)
        ending = .5 - .5 * math.cos(math.pi * (length - 1 - i) / end_fade) if i >= length - end_fade else 1
        carrier = sum(amplitude * math.sin(2 * math.pi * candidate['frequencies_hz'][3] * ratio * t)
                      for ratio, amplitude in candidate['partials'])
        voice.append(carrier * onset * decay * ending)
    # Match A's original peak rather than boosting a long tail to the same RMS.
    gain = max(abs(v) for v in reference) / max(abs(v) for v in voice)
    voice = [v * gain for v in voice]
    assert voice[0] == voice[-1] == 0
    assert max(abs(v) for v in voice) <= config['peak_limit']
    rms = [math.sqrt(sum(v * v for v in voice[start:start + rate // 10]) / (rate // 10))
           for start in range(rate // 10, rate, rate // 10)]
    assert all(a > b for a, b in zip(rms, rms[1:])), 'Tail must decay throughout'
    samples = [0.0] * (prelude + count * period)
    for n in range(count):
        start = prelude + n * period
        samples[start:start + length] = voice
    destination.mkdir(parents=True, exist_ok=True)
    path = destination / 'A2-Critical.wav'
    sha = write_wave(path, samples, rate, count)
    with wave.open(str(path), 'rb') as output:
        decoded = array.array('h', output.readframes(output.getnframes()))
        if sys.byteorder != 'little':
            decoded.byteswap()
    for n in range(count):
        start = prelude + n * period + length
        assert len(decoded[start:start + rate // 2]) == rate // 2
        assert all(v == 0 for v in decoded[start:start + rate // 2]), 'Full500ms insertion gap'
    metadata = {
        'id': 'A2-Critical', 'status': 'offline_audition_unqualified',
        'file': path.name, 'sha256': sha, 'sample_rate': rate, 'pulse_count': count,
        'prelude_ms': 300, 'period_ms': 1500, 'tone_support_ms': 1000, 'silent_gap_ms': 500,
        'fundamental_hz': candidate['frequencies_hz'][3], 'partials': candidate['partials'],
        'attack_ms': 12, 'decay_time_constant_ms': 320, 'terminal_fade_ms': 60,
        'level_matching': 'same peak as A Critical; no long-tail RMS boost',
        'mixing': 'gaps contain silence; no beep or lower-priority alarm mixing implemented'
    }
    (destination / 'A2-Critical.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    print('PASS: six pulses, 1500ms period, declining1s tails, exact500ms zero-PCM gaps')
    print(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/alarm-auditions')
    generate(parser.parse_args().output.resolve())


if __name__ == '__main__':
    main()
