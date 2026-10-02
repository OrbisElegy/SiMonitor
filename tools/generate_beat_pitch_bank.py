#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Freeze user-selected A curve as integer-percent original PCM voices."""
import argparse
import hashlib
import json
import struct
from generate_beat_pitch_auditions import ROOT, CURVES, frequency, voice


def build_pitch_bank():
    return b''.join(struct.pack('<4800h', *voice(frequency(CURVES['A'], value))[0])
                    for value in range(70, 98))


def manifest_entry(data):
    return {'frames': len(data) // 2, 'sha256': hashlib.sha256(data).hexdigest()}


def generate(check=False):
    data = build_pitch_bank()
    path = ROOT / 'src/Monitor.Infrastructure/Audio/SelectedTones/HeartbeatPitchA.pcm'
    manifest_path = path.with_name('manifest.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    entry = manifest_entry(data)
    if check:
        if path.read_bytes() != data:
            raise SystemExit('A pitch bank differs from selected offline authoring')
        if manifest['voices'].get('HeartbeatPitchA') != entry:
            raise SystemExit('A pitch bank manifest entry is missing or differs from the PCM asset')
    else:
        path.write_bytes(data)
        manifest['voices']['HeartbeatPitchA'] = entry
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('PASS: selected A pitch bank,28 voices')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    generate(parser.parse_args().check)


if __name__ == '__main__':
    main()
