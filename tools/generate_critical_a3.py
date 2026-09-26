#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline A3: natural A2 decay across1.5s onsets, with an optional beep mix."""
import argparse
import json
import math
from pathlib import Path

from generate_alarm_auditions import ROOT, pulse, write_wave


def generate(destination):
    config = json.loads((ROOT / 'eng/audio/alarm-audition-candidates.json').read_text())
    candidate = next(c for c in config['candidates'] if c['id'] == 'A')
    rate = config['sample_rate_hz']
    reference = pulse(config, candidate, 'Critical', 0)
    length, period, prelude, count = round(3.5 * rate), round(1.5 * rate), round(.3 * rate), 6
    attack, end_fade = round(.012 * rate), round(.2 * rate)
    voice = []
    for i in range(length):
        t = i / rate
        onset = .5 - .5 * math.cos(math.pi * i / attack) if i < attack else 1
        decay = math.exp(-max(0, i - attack) / rate / .32)
        # Truncate only below16-bit resolution, not at the former1s boundary.
        ending = .5 - .5 * math.cos(math.pi * (length - 1 - i) / end_fade) if i >= length - end_fade else 1
        carrier = sum(amplitude * math.sin(2 * math.pi * candidate['frequencies_hz'][3] * ratio * t)
                      for ratio, amplitude in candidate['partials'])
        voice.append(carrier * onset * decay * ending)
    gain = max(abs(v) for v in reference) / max(abs(v) for v in voice)
    voice = [v * gain for v in voice]
    assert max(abs(v) for v in voice[-end_fade:]) < 1 / 32767
    assert any(abs(v) > .00025 for v in voice[rate:period]), 'Tail continues across old gap'
    samples = [0.0] * (prelude + (count - 1) * period + length + rate // 2)
    for n in range(count):
        start = prelude + n * period
        for i, value in enumerate(voice):
            samples[start + i] += value
    assert max(abs(v) for v in samples) < config['peak_limit']
    # Underlying voices add linearly, retaining the previous tail at each onset.
    assert abs(samples[prelude + period + attack] - voice[period + attack] - voice[attack]) < 1e-12
    destination.mkdir(parents=True, exist_ok=True)
    main = destination / 'A3-Critical-natural-tail.wav'
    # Six attacks now form ONE connected audible region because tails overlap.
    main_sha = write_wave(main, samples, rate, 1)
    mixed = samples.copy()
    beep_length = round(.1 * rate)
    for n in range(count):
        start = prelude + n * period + round(1.15 * rate)
        for i in range(beep_length):
            edge = min(1, i / (rate * .008), (beep_length - 1 - i) / (rate * .02))
            mixed[start + i] += .055 * edge * math.sin(2 * math.pi * 795 * i / rate)
    assert max(abs(v) for v in mixed) < config['peak_limit']
    overlay = destination / 'A3-Critical-with-beep.wav'
    overlay_sha = write_wave(overlay, mixed, rate, 1)
    (destination / 'A3-Critical.json').write_text(json.dumps({
        'id': 'A3-Critical', 'status': 'offline_audition_only',
        'period_ms': 1500, 'attacks': 6, 'decay_time_constant_ms': 320,
        'voice_support_ms': 3500, 'terminal_fade_ms': 200,
        'frequency_hz': 800, 'partials': candidate['partials'],
        'level_matching': 'A2 onset and peak retained; no per-file normalization',
        'overlap': 'additive tails; former500ms gap no longer forced silent',
        'beep_overlay': 'illustrative795Hz/100ms at1150ms after each onset; not measured HR or production arbitration',
        'files': [{'file': main.name, 'sha256': main_sha}, {'file': overlay.name, 'sha256': overlay_sha}]
    }, indent=2) + '\n')
    print('PASS: natural tail, exact additive overlap, sub-LSB final fade and unclipped decoded PCM')
    print(main)
    print(overlay)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/alarm-auditions')
    generate(parser.parse_args().output.resolve())
