#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Render selected E1/F1 AED workflow auditions; no runtime state changes."""
import hashlib
import html
import json
from pathlib import Path
import shutil
import wave
import zipfile

import numpy as np

from selected_therapy_voice import selected_voice_path

ROOT = Path(__file__).resolve().parents[1]
RATE = 48000


def read(path):
    with wave.open(str(path)) as stream:
        assert (stream.getnchannels(), stream.getsampwidth(), stream.getframerate()) == (1, 2, RATE)
        return np.frombuffer(stream.readframes(stream.getnframes()), dtype='<i2').astype(float) / 32768


def save(path, values):
    assert np.isfinite(values).all() and np.max(np.abs(values)) < .9
    with wave.open(str(path), 'wb') as stream:
        stream.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        stream.writeframes(np.rint(values * 32768).astype('<i2').tobytes())


class Timeline:
    def __init__(self):
        self.parts = []
        self.events = []
        self.frames = 0

    def add(self, samples, label):
        self.events.append(dict(label=label, start_s=self.frames / RATE, duration_s=len(samples) / RATE))
        self.parts.append(samples)
        self.frames += len(samples)

    def pause(self, seconds, label='试听场景等待'):
        self.add(np.zeros(round(seconds * RATE)), label)

    def audio(self):
        return np.concatenate(self.parts)


def build(language, voices, sounds):
    timeline = Timeline()
    for key in ['AED_MODE_ADULT', 'AED_CONNECT_CABLE', 'AED_APPLY_PADS']:
        timeline.add(voices[key], key)
        timeline.pause(.6)
    timeline.add(voices['AED_ANALYZING'], 'AED_ANALYZING')
    timeline.pause(2, '分析等待（试听设定 2 秒，不代表算法时长）')
    # Ramp begins with Charging; constant -12 dB duck during the spoken prompt.
    speech = voices['AED_SHOCKABLE']
    ramp = sounds['charge']
    length = max(len(speech), len(ramp))
    charging = np.zeros(length)
    charging[:len(ramp)] = ramp
    charging[:len(speech)] *= 10 ** (-12 / 20)
    charging[:len(speech)] += speech
    timeline.add(charging, 'Charging：AED_SHOCKABLE + 连续充电（语音期间 -12 dB）')
    timeline.add(voices['AED_READY'], 'Ready：先播放 AED_READY')
    timeline.add(sounds['ready'], 'Ready Entry：870 Hz 双声，Hold Off')
    timeline.pause(1.2, '等待模拟按键（试听设定 1.2 秒）')
    timeline.add(voices['AED_SHOCK_RECORDED'], 'ShockDelivered：AED_SHOCK_RECORDED，无同义确认 beep')
    # CPR timer starts on state entry, including the initial voice duration.
    cpr_start = timeline.frames
    cpr = np.zeros(120 * RATE)
    start_voice = voices['AED_CPR_START']
    cpr[:len(start_voice)] = start_voice
    offset = len(start_voice)
    ticks = []
    breaths = []
    step = RATE * 60 / 110
    while offset < len(cpr):
        for i in range(30):
            onset = offset + round(i * step)
            if onset >= len(cpr):
                break
            count = min(len(sounds['cpr']), len(cpr) - onset)
            cpr[onset:onset + count] += sounds['cpr'][:count]
            ticks.append((cpr_start + onset) / RATE)
        offset += round(30 * step)
        breath = voices['AED_CPR_BREATHS']
        if offset + len(breath) > len(cpr):
            break
        cpr[offset:offset + len(breath)] += breath
        breaths.append(dict(start_s=(cpr_start + offset) / RATE, duration_s=len(breath) / RATE))
        offset += max(5 * RATE, len(breath))
    timeline.add(cpr, 'CPR：120 秒 / 110 BPM / 30:2 / C2；通气窗 5 秒为试听设定')
    timeline.add(voices['AED_CPR_STOP'], '先停节拍，再播放 AED_CPR_STOP')
    timeline.add(voices['AED_ANALYZING'], '返回 Analyzing；本次流程试听结束')
    return timeline, dict(cpr_start_s=cpr_start / RATE, cpr_duration_s=120, tick_onsets_s=ticks,
                          ventilation=breaths, ventilation_window_s=5, language=language)


def main():
    out = ROOT / 'artifacts/therapy-selected-flow'
    out.mkdir(parents=True, exist_ok=True)
    r4 = ROOT / 'artifacts/therapy-auditions-r4'
    prompts = json.loads((ROOT / 'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))['prompts']
    sounds = {key: read(r4 / name) for key, name in dict(charge='selected-charge.wav', ready='selected-ready.wav',
              cpr='selected-cpr.wav', pacer='selected-pacer.wav', heartbeat='selected-heartbeat.wav').items()}
    manifest = dict(voices={'en': 'E1', 'zh-CN': 'F1'}, runtime_validation=False, shock_series=1,
                    initial_cpr=False, ready_hold=False, scenarios={}, sources={})
    audios = []
    for language, variant in [('zh-CN', 'F1'), ('en', 'E1')]:
        directory = out / 'voice' / language
        directory.mkdir(parents=True, exist_ok=True)
        voices = {}
        for prompt in prompts:
            key = prompt['prompt_id']
            src = (selected_voice_path(key, prompt['zh_CN']) if language == 'zh-CN'
                   else r4 / f'voice/{language}/{variant}/{key}.wav')
            shutil.copyfile(src, directory / src.name)
            manifest['sources'][f'{language}/{key}'] = hashlib.sha256(src.read_bytes()).hexdigest()
            voices[key] = read(src)
        timeline, timing = build(language, voices, sounds)
        audio = timeline.audio()
        save(out / f'aed-full-{language}.wav', audio)
        audios.append(audio)
        manifest['scenarios'][language] = dict(events=timeline.events, timing=timing, duration_s=len(audio) / RATE)
    save(out / 'aed-full-bilingual.wav', np.concatenate([audios[0], np.zeros(3 * RATE), audios[1]]))
    tones = Timeline()
    for key in ['charge', 'ready', 'cpr', 'pacer', 'heartbeat']:
        tones.add(sounds[key], key)
        tones.pause(1)
    save(out / 'selected-tones.wav', tones.audio())
    manifest['tone_chapters'] = tones.events
    (out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for name in ['NOTICE.txt', 'UPSTREAM-SOURCES.txt', 'PaddleSpeech-LICENSE.txt']:
        shutil.copyfile(ROOT / 'eng/audio/voices/zh-CN/F1' / name, out / ('F1-' + name))
    shutil.copyfile(ROOT / 'artifacts/therapy-auditions-r2/English-LICENSE.txt', out / 'English-LICENSE.txt')
    cards = []
    for language in ['zh-CN', 'en']:
        events = manifest['scenarios'][language]['events']
        chapters = ''.join(f'<li>{e["start_s"]:.1f}s — {html.escape(e["label"])}</li>' for e in events)
        cards.append(f'<h2>{language} 完整流程</h2><audio controls preload="metadata" src="aed-full-{language}.wav"></audio><ol>{chapters}</ol>')
    library = ''.join(f'<p>{p["prompt_id"]} · {html.escape(p["zh_CN"])} / {html.escape(p["en"])}</p>' + ''.join(
        f'<audio controls preload="none" src="voice/{language}/{p["prompt_id"]}.wav"></audio>' for language in ['zh-CN', 'en']) for p in prompts)
    (out / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>选定声线 · AED 完整流程</title><style>body{font:16px system-ui;max-width:960px;margin:32px auto;padding:20px;line-height:1.6}audio{display:block;width:100%;margin:12px 0}li{margin:5px 0}</style><h1>E1 / F1 · AED 完整流程试听</h1><p>成人单次模拟电击 → 120 秒 CPR（110 BPM、30:2）→ 再分析。声音采用连续充电、870 Hz 双声、C2 干短点击。主流程不叠加心搏与起搏音，不使用放电爆响。每种语言独立播放；双语串联仅供评估。</p><p>分析等待 2 秒、按键等待 1.2 秒、通气窗 5 秒和充电期间语音压低量为试听编排，未冻结运行时参数。充电采用既有完整候选时长。音频为离线时间线，不证明实际状态机或播放仲裁已验证。</p><h2>中文 → 英文完整连播（间隔 3 秒）</h2><audio controls preload="metadata" src="aed-full-bilingual.wav"></audio>''' + ''.join(cards) + '''<h2>已选提示音</h2><p>连续充电 → 就绪双声 → CPR 点击 → 起搏继电器 → 监护心搏；每项后间隔 1 秒。</p><audio controls preload="none" src="selected-tones.wav"></audio><h2>完整语音库（含异常及其他分支）</h2>''' + library + '''<p>F1 上游说明见 F1-NOTICE.txt；音频不自动适用项目代码许可证。</p><script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})))</script></html>''', encoding='utf-8')
    with zipfile.ZipFile(out / 'therapy-selected-flow.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(out.rglob('*')):
            if path.is_file() and path.suffix != '.zip':
                archive.write(path, path.relative_to(out))
    print(json.dumps({k: v['duration_s'] for k, v in manifest['scenarios'].items()}))


if __name__ == '__main__':
    main()
