#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Freeze the selected original A/A4 voices as little-endian mono PCM16."""
import argparse
import hashlib
import json
import math
import struct

from generate_alarm_auditions import ROOT, pulse
from generate_critical_a3 import critical_voice
from generate_beat_pitch_bank import build_pitch_bank, manifest_entry


def generate(check):
    config, candidate, rate, critical = critical_voice()
    voices = {name: pulse(config, candidate, name, 0) for name in ('Info', 'Notice', 'Warning')}
    voices['Critical'] = critical
    length = round(.1 * rate)
    voices['Heartbeat'] = [
        .055 * min(1, i / (rate * .008), (length - 1 - i) / (rate * .02))
        * math.sin(2 * math.pi * 795 * i / rate) for i in range(length)]
    folder = ROOT / 'src/Monitor.Infrastructure/Audio/SelectedTones'
    folder.mkdir(parents=True, exist_ok=True)
    manifest = {'sample_rate': rate, 'format': 'signed_pcm16_le_mono', 'voices': {}}
    for name, samples in voices.items():
        assert samples[0] == samples[-1] == 0
        assert max(abs(v) for v in samples) < config['peak_limit']
        data = struct.pack('<' + 'h' * len(samples), *(round(v * 32767) for v in samples))
        path = folder / (name + '.pcm')
        if check:
            assert path.read_bytes() == data, path
        else:
            path.write_bytes(data)
        manifest['voices'][name] = {'frames': len(samples), 'sha256': hashlib.sha256(data).hexdigest()}
    bank = build_pitch_bank()
    path = folder / 'HeartbeatPitchA.pcm'
    if check:
        assert path.read_bytes() == bank, path
    else:
        path.write_bytes(bank)
    manifest['voices']['HeartbeatPitchA'] = manifest_entry(bank)
    if check:
        assert {path.stem for path in folder.glob('*.pcm')} == set(manifest['voices']), 'Unlisted PCM asset'
    path = folder / 'manifest.json'
    data = json.dumps(manifest, indent=2) + '\n'
    if check:
        assert path.read_text() == data
    else:
        path.write_text(data)
    print('PASS: selected A/A4 voices match frozen PCM16 assets')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    generate(parser.parse_args().check)
