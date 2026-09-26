#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline A4: independent heartbeat timing over the unchanged A3 Critical track."""
import argparse
import json
import math
from pathlib import Path

from generate_alarm_auditions import ROOT, write_wave
from generate_critical_a3 import critical_track


def generate(destination):
    config, _, rate, critical = critical_track()
    heartbeat = [0.0] * len(critical)
    prelude, period, length = round(.3 * rate), round(.8 * rate), round(.1 * rate)
    # A separate 75-bpm clock: no alarm-dependent gap, delay or suppression.
    starts = [prelude + n * period for n in range(12)]
    for start in starts:
        for i in range(length):
            edge = min(1, i / (rate * .008), (length - 1 - i) / (rate * .02))
            heartbeat[start + i] = .055 * edge * math.sin(2 * math.pi * 795 * i / rate)
    mixed = [a + b for a, b in zip(critical, heartbeat)]
    assert max(abs(v) for v in mixed) < config['peak_limit']
    assert all(b - a == period for a, b in zip(starts, starts[1:]))
    alarm_period = round(1.5 * rate)
    phases = {(start - prelude) % alarm_period for start in starts}
    assert 0 in phases and any(0 < p <= length for p in phases)
    assert any(p >= rate for p in phases), 'Cover both onset and former quiet interval'
    assert all(abs(m - a - b) < 1e-12 for m, a, b in zip(mixed, critical, heartbeat))
    destination.mkdir(parents=True, exist_ok=True)
    files = []
    for name, samples, regions in [
        ('A4-heartbeat-reference.wav', heartbeat, 12),
        ('A4-Critical-independent-heartbeat.wav', mixed, 1),
    ]:
        path = destination / name
        files.append({'file': name, 'sha256': write_wave(path, samples, rate, regions)})
        print(path)
    (destination / 'A4-Critical.json').write_text(json.dumps({
        'id': 'A4-Critical', 'status': 'offline_timing_hypothesis_only',
        'critical': 'unchanged A3 natural tail; six onsets at1500ms spacing',
        'heartbeat_bpm': 75, 'heartbeat_onsets_ms': [s * 1000 // rate for s in starts],
        'heartbeat': 'A3 illustrative795Hz/100ms beep; twelve beats then tail-only ending',
        'mix': 'linear sum; no ducking, gap scheduling or per-file normalization',
        'scope': 'heartbeat plus highest alarm only; not concurrent alarm priorities',
        'files': files,
    }, indent=2) + '\n')
    print('PASS: independent clock, onset/tail overlap, exact mixing and decoded PCM checks')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/alarm-auditions')
    generate(parser.parse_args().output.resolve())
