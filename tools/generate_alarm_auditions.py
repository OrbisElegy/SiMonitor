#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Original offline audition assets; never called by live/authority audio code.

Uses only the Python standard library. Float synthesis is deliberately offline;
the selected design will need integration with the deterministic runtime voice.
"""
import argparse
import array
import hashlib
import html
import json
import math
from pathlib import Path
import sys
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[1]
if not (ROOT / 'eng/audio/alarm-audition-candidates.json').exists():
    ROOT = Path(__file__).resolve().parent
LEVELS = ('Info', 'Notice', 'Warning', 'Critical')


def pulse(config, candidate, level, ordinal):
    index = LEVELS.index(level)
    rate = config['sample_rate_hz']
    length = round(candidate['durations_ms'][index] * rate / 1000)
    attack = round(candidate['attack_ms'] * rate / 1000)
    release = round(candidate['release_ms'] * rate / 1000)
    multiplier = candidate['pitch_multipliers'].get(level, [1] * 10)[ordinal]
    frequency = candidate['frequencies_hz'][index] * multiplier
    assert 0 < attack + release < length
    assert max(ratio * frequency for ratio, _ in candidate['partials']) < rate / 2
    result = []
    for i in range(length):
        # Cosine edges remove steps/clicks, including the terminal sample.
        envelope = 1.0
        if i < attack:
            envelope = .5 - .5 * math.cos(math.pi * i / attack)
        if i >= length - release:
            envelope *= .5 - .5 * math.cos(math.pi * (length - 1 - i) / release)
        t = i / rate
        value = 0.0
        for ratio, amplitude in candidate['partials']:
            decay = math.exp(-t * (9 + 2 * ratio)) if candidate['decay'] else 1.0
            value += amplitude * decay * math.sin(2 * math.pi * frequency * ratio * t)
        result.append(value * envelope)
    rms = math.sqrt(sum(v * v for v in result) / length)
    gain = 10 ** (config['active_rms_dbfs'] / 20) / rms
    result = [v * gain for v in result]
    assert max(abs(v) for v in result) <= config['peak_limit'], 'Refuse clipping/unequal limiting'
    assert result[0] == result[-1] == 0
    return result


def write_wave(path, samples, rate, expected_pulses):
    pcm = array.array('h', (round(v * 32767) for v in samples))
    if sys.byteorder != 'little':
        pcm.byteswap()
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm.tobytes())
    # Read back the deliverable, not just synthesis inputs.
    with wave.open(str(path), 'rb') as output:
        assert output.getparams()[:4] == (1, 2, rate, len(samples))
        data = array.array('h', output.readframes(len(samples)))
        if sys.byteorder != 'little':
            data.byteswap()
        assert max(abs(v) for v in data) < 32767
        # Detect actual PCM bursts with5ms bins, not only requested onsets.
        active = [any(abs(v) >= 8 for v in data[i:i + rate // 200])
                  for i in range(0, len(data), rate // 200)]
        count = sum(on and (i == 0 or not active[i - 1]) for i, on in enumerate(active))
        assert count == expected_pulses, (path, count, expected_pulses)
        assert data[0] == data[-1] == 0
    return hashlib.sha256(path.read_bytes()).hexdigest()


def generate(destination):
    source = ROOT / 'eng/audio/alarm-audition-candidates.json'
    if not source.exists():
        source = ROOT / 'candidates.json'
    config = json.loads(source.read_text(encoding='utf-8'))
    rate = config['sample_rate_hz']
    destination.mkdir(parents=True, exist_ok=True)
    manifest = {'schema': 'Monitor.AlarmAuditionFiles@1', 'files': []}
    cards = []
    archive_files = []
    for candidate in config['candidates']:
        cells = []
        montage = []
        for level in LEVELS:
            onsets = config['levels'][level]['onsets_ms']
            duration = candidate['durations_ms'][LEVELS.index(level)]
            samples = [0.0] * round((300 + onsets[-1] + duration + 700) * rate / 1000)
            spans = []
            for ordinal, onset in enumerate(onsets):
                voice = pulse(config, candidate, level, ordinal)
                start = round((300 + onset) * rate / 1000)
                end = start + len(voice)
                assert not spans or start > spans[-1][1], 'Overlapping pulses change grouping'
                samples[start:end] = voice
                spans.append((start, end))
            assert len(spans) == {'Info': 1, 'Notice': 3, 'Warning': 10, 'Critical': 8}[level]
            name = f'{candidate["id"]}-{level}.wav'
            sha = write_wave(destination / name, samples, rate, len(spans))
            archive_files.append(name)
            manifest['files'].append({'file': name, 'sha256': sha, 'seconds': len(samples) / rate,
                                      'pulse_count': len(spans), 'onsets_ms': [300 + v for v in onsets]})
            cells.append(f'<td><b>{level}</b><audio controls preload="none" src="{name}"></audio>'
                         f'<a download href="{name}">下载 WAV</a></td>')
            montage.extend(samples)
            montage.extend([0.0] * rate)
        name = f'{candidate["id"]}-all-levels.wav'
        sha = write_wave(destination / name, montage, rate, 22)
        archive_files.append(name)
        manifest['files'].append({'file': name, 'sha256': sha, 'seconds': len(montage) / rate})
        cards.append(f'<section><h2>{candidate["id"]} · {html.escape(candidate["label"])}</h2>'
                     f'<p>{html.escape(candidate["description"])}</p>'
                     f'<p>整组试听：Info → Notice → Warning → Critical（中间静音分隔）</p>'
                     f'<audio controls preload="none" src="{name}"></audio>'
                     f'<table><tr>{"".join(cells)}</tr></table></section>')
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (destination / 'candidates.json').write_bytes(source.read_bytes())
    (destination / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>监护报警音试听 A/B/C</title>
<style>body{font:16px system-ui,sans-serif;background:#f4f6f8;color:#172b3a;max-width:1180px;margin:32px auto;padding:0 20px}
section{background:white;padding:24px;border-radius:12px;margin:24px 0}audio{width:100%;max-width:360px;display:block;margin:12px 0}
table{width:100%;table-layout:fixed}td{padding:12px;vertical-align:top}a{color:#145fa5}p{line-height:1.6}
@media(max-width:800px){table,tr,td{display:block;width:auto}}</style>
<h1>监护报警音 · 三组候选</h1>
<p>本地原创合成样本，尚未替换正式音色。每声有效段RMS统一为 −24 dBFS；这不是声压或听觉响度标定。
请保持播放器/系统音量一致比较。音色、音高与起落包络不同，声数和组内起点保持相同。</p>
<p>Info 为一次单声；Notice 为三声；Warning 为（3+2）×2；Critical 为每0.5秒一声、共八声。
为便于比较，省略Info的30秒和Notice的10秒长等待，未修改产品重复周期。没有自动播放。</p>
''' + ''.join(cards) + '''<p>可按级别混选，例如 Notice A、Warning B、Critical C。
请反馈：更接近哪组、偏尖或偏闷、尾音太长或太短、节奏需要多快。</p>
<script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>{
document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})}))</script></html>''', encoding='utf-8')
    readme = '''监护报警音 A/B/C 候选包

解压全部文件，浏览器打开 index.html 可分别或整组试听。也可用系统播放器打开 WAV。
每组依次为 Info、Notice、Warning、Critical；可按级别混选。
本包为原创合成、未替换正式运行音色。候选频谱/包络参数见 candidates.json。
全部为48kHz、16位、单声道；每声有效段RMS=-24dBFS，不代表声压/等响校准。
Info/Notice/Warning各展示一组，省略长重复等待；Critical展示八声。
无商业录音；不宣称厂家复刻或IEC合规。许可随项目 AGPL-3.0-or-later。
可用 Python 3 在解压目录执行：python generate_alarm_auditions.py --output rebuilt
'''
    (destination / 'README.txt').write_text(readme, encoding='utf-8')
    (destination / 'LICENSE').write_bytes((ROOT / 'LICENSE').read_bytes())
    (destination / 'generate_alarm_auditions.py').write_bytes(Path(__file__).read_bytes())
    archive_files += ['index.html', 'README.txt', 'manifest.json', 'candidates.json', 'LICENSE', 'generate_alarm_auditions.py']
    with zipfile.ZipFile(destination / 'alarm-auditions.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in archive_files:
            archive.write(destination / name, name)
    print(f'PASS: {len(manifest["files"])} WAV files; bounded peaks, pulse counts, edges and decoded formats verified')
    print(destination / 'index.html')
    print(destination / 'alarm-auditions.zip')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/alarm-auditions')
    generate(parser.parse_args().output.resolve())


if __name__ == '__main__':
    main()
