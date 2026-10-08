#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Validate and locate the approved Mandarin therapy recordings."""
import hashlib
import json
from pathlib import Path
import wave

ROOT = Path(__file__).resolve().parents[1]
DIRECTORY = ROOT / 'eng/audio/voices/zh-CN/F1'


def selected_voice_path(prompt_id, text):
    manifest = json.loads((DIRECTORY / 'manifest.json').read_text(encoding='utf-8'))
    entries = {entry['prompt_id']: entry for entry in manifest['files']}
    if prompt_id not in entries and (DIRECTORY / 'pad-placement.json').exists():
        entry = json.loads((DIRECTORY / 'pad-placement.json').read_text(encoding='utf-8'))
        entries[entry['prompt_id']] = entry
    entry = entries[prompt_id]
    if entry['text'] != text:
        raise ValueError(f'Approved voice text differs for {prompt_id}')
    path = DIRECTORY / entry['file']
    if hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
        raise ValueError(f'Approved voice hash differs for {prompt_id}')
    with wave.open(str(path)) as stream:
        if (stream.getnchannels(), stream.getsampwidth(), stream.getframerate()) != (1, 2, 48000):
            raise ValueError(f'Unexpected voice format for {prompt_id}')
    return path


def main():
    manifest = json.loads((DIRECTORY / 'manifest.json').read_text(encoding='utf-8'))
    entries = list(manifest['files'])
    if (DIRECTORY / 'pad-placement.json').exists():
        entries.append(json.loads((DIRECTORY / 'pad-placement.json').read_text(encoding='utf-8')))
    for entry in entries:
        selected_voice_path(entry['prompt_id'], entry['text'])
    print(f'Validated {len(entries)} selected Mandarin recordings')


if __name__ == '__main__':
    main()
