#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Rebuild ready/hold tones, the C2 CPR click and selected P7 relay."""
import hashlib
import json
import math
from pathlib import Path
import wave


def ready_hold_tone(hz):
    from generate_therapy_auditions import RATE, envelope, normalize

    frame_count = round(.3 * RATE)
    samples = []
    for i in range(frame_count):
        phase = 2 * math.pi * hz * i / RATE
        # Brighter than the nearly sinusoidal completion double, at the same
        # stored-energy RMS. Short smooth edges keep the alternating loop clean.
        value = math.sin(phase) + .18 * math.sin(2 * phase) + .12 * math.sin(3 * phase)
        samples.append(value * envelope(i, frame_count, 144, 384))
    return normalize(samples, -34)


def main():
    import numpy as np
    from generate_therapy_auditions import tone, pattern
    from generate_therapy_auditions_r2 import cpr_voice, mechanism

    root = Path(__file__).resolve().parents[1]
    configuration = json.loads((root / 'eng/audio/therapy-audition-r2.json').read_text(encoding='utf-8'))
    spec = next(item for item in configuration['cpr_candidates'] if item['id'] == 'C2')
    relay_index, relay = next((i, item) for i, item in enumerate(configuration['pacer_candidates']) if item['id'] == 'P7')
    output = root / 'src/Monitor.Infrastructure/Audio/SelectedTherapySounds'
    output.mkdir(parents=True, exist_ok=True)
    files = []
    sounds = [('Ready', pattern([tone(870, .14, -22)] * 2, [0, .3])),
              ('ReadyHold', ready_hold_tone(740) + ready_hold_tone(1040)),
              ('Cpr', cpr_voice(spec)), ('Relay', mechanism(relay, 2800 + relay_index))]
    for name, samples in sounds:
        path = output / (name + '.wav')
        with wave.open(str(path), 'wb') as stream:
            stream.setparams((1, 2, 48000, 0, 'NONE', 'not compressed'))
            stream.writeframes(np.rint(np.asarray(samples) * 32768).astype('<i2').tobytes())
        files.append(dict(file=path.name, frames=len(samples), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    manifest = dict(sample_rate_hz=48000, format='PCM16 mono', generator='tools/generate_therapy_runtime_tones.py',
                    selections='eng/audio/therapy-audition-r4.json', files=files)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
