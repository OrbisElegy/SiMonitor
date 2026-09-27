#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Author original SpO2 pitch candidates offline; never used by live audio.

Run --check to verify assets, curve ordering, exact anchor PCM and manifests.
Candidates do not establish a clinically validated saturation/frequency mapping.
"""
import argparse
import hashlib
import html
import io
import json
import math
from pathlib import Path
import struct
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RATE = 48000
# Independent variable is saturation; event timing stays at 75 or 150 bpm.
LEVELS = [100, 97, 95, 92, 90, 85, 80, 70, 90, 97]
CURVES = {
    'A': [(70, 400), (80, 510), (85, 580), (90, 660), (92, 705), (95, 760), (97, 795), (100, 795)],
    'B': [(70, 300), (80, 400), (85, 490), (90, 580), (92, 650), (95, 730), (97, 795), (100, 795)],
}


def frequency(curve, saturation):
    if not curve[0][0] <= saturation <= curve[-1][0]:
        raise ValueError('Saturation outside audition range')
    for (low, first), (high, second) in zip(curve, curve[1:]):
        if low <= saturation <= high:
            return first + (second - first) * (saturation - low) / (high - low)
    raise ValueError('Invalid pitch curve')


def voice(hz):
    # Same original 100ms/8ms attack/20ms release as selected Heartbeat.pcm.
    # Apply current runtime full-master heartbeat gain (16384/16384, original gain, on the selected-sample path).
    length = RATE // 10
    source = [round(.055 * min(1, i / (RATE * .008), (length - 1 - i) / (RATE * .02))
                    * math.sin(2 * math.pi * hz * i / RATE) * 32767) for i in range(length)]
    assert source[0] == source[-1] == 0
    return source, source.copy()


def wav(samples):
    stream = io.BytesIO()
    with wave.open(stream, 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(struct.pack('<' + 'h' * len(samples), *samples))
    return stream.getvalue()


def track(curve, bpm):
    period = RATE * 60 // bpm
    # Each saturation lasts2.4s:3 events at75bpm or6 at150bpm.
    segment = RATE * 12 // 5
    samples = [0] * (segment * len(LEVELS))
    events = []
    for index, saturation in enumerate(LEVELS):
        hz = frequency(curve, saturation)
        _, tone = voice(hz)
        crossings = sum(a <= 0 < b for a, b in zip(tone, tone[1:]))
        assert abs(crossings * 10 - hz) <= 10, 'PCM pitch differs from requested frequency'
        for offset in range(0, segment, period):
            start = index * segment + offset
            samples[start:start + len(tone)] = tone
            events.append({'frame': start, 'saturation_percent': saturation, 'frequency_hz': hz})
    assert all(b['frame'] - a['frame'] == period for a, b in zip(events, events[1:]))
    assert len(events) == (30 if bpm == 75 else 60)
    assert max(abs(v) for v in samples) <= 1803
    return wav(samples), events


def generate(folder, check):
    anchor, _ = voice(795)
    frozen = ROOT / 'src/Monitor.Infrastructure/Audio/SelectedTones/Heartbeat.pcm'
    assert frozen.read_bytes() == struct.pack('<' + 'h' * len(anchor), *anchor), 'Selected anchor changed'
    files = {}
    manifest = {'schema': 'Monitor.BeatPitchAudition@1', 'sample_rate': RATE,
                'status': 'candidates-not-selected', 'levels_percent': LEVELS,
                'segment_seconds': 2.4, 'runtime_gain_q15': 16384, 'selected_sample_gain_denominator': 16384, 'curves': CURVES, 'tracks': {}}
    for name, curve in CURVES.items():
        values = [frequency(curve, value / 10) for value in range(700, 1001)]
        assert all(a <= b for a, b in zip(values, values[1:])), 'Non-monotonic curve'
        assert values[-1] == frequency(curve, 97) == 795
        for bpm in (75, 150):
            filename = f'{name}-{bpm}bpm.wav'
            data, events = track(curve, bpm)
            files[filename] = data
            manifest['tracks'][filename] = {'bpm': bpm, 'frames': RATE * 24, 'events': events,
                                           'sha256': hashlib.sha256(data).hexdigest()}
    files['manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    rows = ''.join(f'<tr><td>{value}%</td><td>{frequency(CURVES["A"], value):g} Hz</td>'
                   f'<td>{frequency(CURVES["B"], value):g} Hz</td></tr>' for value in LEVELS[:8])
    players = ''.join(f'<section><h2>{name}：{label}</h2>' + ''.join(
        f'<p>{bpm} bpm · 24秒</p><audio controls preload="none" src="{name}-{bpm}bpm.wav"></audio>'
        for bpm in (75, 150)) + '</section>' for name, label in [('A', '较缓降调'), ('B', '较强降调')])
    files['index.html'] = ('''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>SpO₂ 心搏音候选</title>
<style>body{font:17px system-ui;max-width:900px;margin:32px auto;padding:0 20px;color:#17232e;background:#f5f6f8}
section{background:white;border-radius:12px;padding:20px;margin:20px 0}audio{width:100%}td,th{padding:8px 24px;text-align:left}table{border-collapse:collapse}tr{border-bottom:1px solid #ccc}</style>
<h1>SpO₂ 心搏音候选试听</h1><p>两组使用相同心搏音包络和增益，只比较降调曲线。正常区97–100%均为795 Hz。
每档持续2.4秒；先下降，再以90%、97%恢复。75与150 bpm仅改变节拍，不改变同档音高。</p>
<p>当前客户端仍使用已确认的固定音高。这些是原创教学候选，不代表厂商标定。低频响度差异也请一并反馈。</p><p>播放顺序：''' + html.escape(' → '.join(f'{s}%' for s in LEVELS)) + '</p>' + players +
        '<h2>候选频率</h2><table><tr><th>SpO₂</th><th>A</th><th>B</th></tr>' + rows + '</table>' +
        '<p>建议反馈：A或B更合适；哪些血氧档位的音高变化过小、过大或音量听起来明显变小。</p>' +
        '<script>document.querySelectorAll("audio").forEach(a=>a.addEventListener("play",()=>{document.querySelectorAll("audio").forEach(b=>{if(a!==b)b.pause()})}));</script></html>').encode()
    archive = io.BytesIO()
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as output:
        for name, data in files.items():
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(info, data)
    files['beat-pitch-auditions.zip'] = archive.getvalue()
    if not check:
        folder.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        path = folder / name
        if check:
            if not path.exists() or path.read_bytes() != data:
                raise SystemExit(f'Asset mismatch: {path}')
        else:
            path.write_bytes(data)
    print(f'PASS: anchor, monotonic curves,180 timed events and{len(files)} assets: {folder}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/beat-pitch-auditions')
    args = parser.parse_args()
    generate(args.output, args.check)
