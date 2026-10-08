#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Render offline therapy audition candidates, never used by the live audio path.

Tone-only: python3 tools/generate_therapy_auditions.py
With cached models: PYTHONPATH=.cache/therapy-tts/packages python3
tools/generate_therapy_auditions.py --tts-models .cache/therapy-tts
Requires ffmpeg and sherpa-onnx 1.12.26 for speech; no network access at generation.
"""
import argparse
import array
import hashlib
import html
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import sys
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RATE = 48000


def silence(seconds):
    return [0.0] * round(seconds * RATE)


def normalize(samples, db):
    rms = math.sqrt(sum(v * v for v in samples) / len(samples))
    gain = 10 ** (db / 20) / rms
    # Refuse to silently limit individual tones: comparisons preserve RMS.
    assert max(abs(v * gain) for v in samples) < 0.8
    return [v * gain for v in samples]


def envelope(frame_index, frame_count, attack_frames, release_frames):
    return min(1.0, .5 - .5 * math.cos(math.pi * min(frame_index / attack_frames, 1))) * (
        .5 - .5 * math.cos(math.pi * min((frame_count - 1 - frame_index) / release_frames, 1)))


def tone(hz, seconds, db=-25):
    n = round(seconds * RATE)
    return normalize([(math.sin(2 * math.pi * hz * i / RATE)
                       + .08 * math.sin(4 * math.pi * hz * i / RATE))
                      * envelope(i, n, 240, 480) for i in range(n)], db)


def transient(spec, db):
    n = round(spec['duration_ms'] * RATE / 1000)
    samples = []
    for i in range(n):
        t = i / RATE
        value = sum(a * math.sin(2 * math.pi * f * t)
                    for f, a in spec['partials'])
        value *= math.exp(-t * 1000 / spec['decay_ms'])
        value *= envelope(i, n, spec['attack_ms'] * RATE / 1000, 144)
        samples.append(value)
    return normalize(samples, db)


def place(samples, voice, start_seconds):
    offset = round(start_seconds * RATE)
    if offset + len(voice) > len(samples):
        samples.extend([0.0] * (offset + len(voice) - len(samples)))
    for i, value in enumerate(voice):
        samples[offset + i] += value


def pattern(voices, onsets_seconds, tail_seconds=.3):
    result = silence(max(t + len(v) / RATE for t, v in zip(onsets_seconds, voices)) + tail_seconds)
    for t, v in zip(onsets_seconds, voices):
        place(result, v, t)
    return result


def ramp(config, stepped=False, stalled=False):
    duration = 6 if stalled else 4
    n = round(duration * RATE)
    low, high = config['ramp_hz']
    phase = 0.0
    values = []
    frequencies = []
    frequency = low
    for i in range(n):
        t = i / RATE
        progress = (t / 4 if t < 2 else .5 if t < 4 else (t - 2) / 4) if stalled else t / 4
        progress = min(progress, 1)
        if stepped:
            progress = min(4, int(progress * 5)) / 4
        target = low + (high - low) * progress
        # 12 ms smoothing keeps phase continuous across the five authored levels.
        frequency += (target - frequency) * (1 - math.exp(-1 / (RATE * .012)))
        phase += 2 * math.pi * frequency / RATE
        values.append((math.sin(phase) + .12 * math.sin(2 * phase))
                      * envelope(i, n, 960, 480))
        if i % 480 == 0:
            frequencies.append([round(t, 2), round(frequency, 4)])
    return normalize(values, config['charge_active_rms_dbfs']), frequencies


def read_pcm(path):
    with wave.open(str(path), 'rb') as source:
        assert source.getnchannels() == 1 and source.getsampwidth() == 2
        assert source.getframerate() == RATE
        pcm = array.array('h', source.readframes(source.getnframes()))
    if sys.byteorder != 'little':
        pcm.byteswap()
    return [v / 32768 for v in pcm]


class Audition:
    def __init__(self, output, config):
        self.output = output
        self.config = config
        self.files = []
        self.samples = {}
        output.mkdir(parents=True, exist_ok=True)

    def save(self, name, samples, label, section, **metadata):
        assert samples and max(abs(v) for v in samples) < .9, name
        path = self.output / f'{name}.wav'
        path.parent.mkdir(parents=True, exist_ok=True)
        pcm = array.array('h', (round(v * 32767) for v in samples))
        if sys.byteorder != 'little':
            pcm.byteswap()
        with wave.open(str(path), 'wb') as out:
            out.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
            out.writeframes(pcm.tobytes())
        actual = read_pcm(path)
        assert len(actual) == len(samples)
        assert max(abs(v) for v in actual) < .9
        # Counts are checked from decoded PCM, not only authoring metadata.
        if 'pulse_count' in metadata:
            bin_size = 240
            active = [max(abs(v) for v in actual[i:i + bin_size]) > .0005
                      for i in range(0, len(actual), bin_size)]
            count = sum(v and (i == 0 or not active[i - 1]) for i, v in enumerate(active))
            assert count == metadata['pulse_count'], (name, count, metadata['pulse_count'])
        self.files.append(dict(file=f'{name}.wav', label=label, section=section,
                               duration_s=len(actual) / RATE, sample_rate_hz=RATE,
                               channels=1, format='PCM16', sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                               peak_dbfs=20 * math.log10(max(abs(v) for v in actual)), **metadata))
        self.samples[name] = samples
        return samples


def build_tones(pack):
    c = pack.config
    for spec in c['ta_candidates']:
        key = spec['id']
        v = transient(spec, c['ta_active_rms_dbfs'])
        pack.save(f'ta-{key}-single', v, f'ta {key} · {spec["label"]} · 单声', 'ta',
                  cue_ids=['DEF-SYNC-TA', 'DEF-CPR-METRONOME-TA'], pulse_count=1)
        pack.save(f'ta-{key}-sync', pattern([v] * 6, [.2 + i * .8 for i in range(6)]),
                  f'ta {key} · 75 次/分同步标记', 'ta', pulse_count=6, beat_source='valid_qrs_sync_pulse')
        pack.save(f'ta-{key}-cpr', pattern([v] * 12, [.2 + i * 60 / 110 for i in range(12)]),
                  f'ta {key} · 110 BPM CPR', 'ta', pulse_count=12, beat_source='cpr_compression_tick',
                  onsets_samples=[round((.2 + i * 60 / 110) * RATE) for i in range(12)])
    for hz in [650, 795]:
        pack.save(f'beat-{hz}', pattern([tone(hz, .042, -26)] * 5, [.2 + i * .8 for i in range(5)]),
                  f'{hz} Hz 心搏音对照（原创）', 'reference', pulse_count=5)
    for key, stepped, stalled in [('continuous', False, False), ('steps', True, False), ('stall', False, True)]:
        v, trajectory = ramp(c, stepped, stalled)
        pack.save(f'charge-{key}', v, {'continuous':'连续充电', 'steps':'五级阶梯充电', 'stall':'进度暂停 2 秒再继续'}[key],
                  'charge', cue_ids=['DEF-CHARGE-START', 'DEF-CHARGE-RAMP'],
                  frequency_trajectory_hz=trajectory, progress_pause_s=[2, 4] if stalled else None)
    pack.save('charge-periodic', pattern([tone(440, .22, -31)] * 6, [i * .65 for i in range(6)]),
              '周期充电', 'charge', cue_ids=['DEF-CHARGE-START', 'DEF-CHARGE-PERIODIC'], pulse_count=6)
    pack.save('charge-fixed', tone(390, 4, -31), '固定低音充电', 'charge',
              cue_ids=['DEF-CHARGE-START', 'DEF-CHARGE-FIXED-LOW'])
    for spec in c['ready_candidates']:
        onsets_seconds = [v / 1000 for v in c['ready_onsets_ms']]
        pack.save(f'ready-{spec["id"]}', pattern([tone(f, c['ready_note_ms'] / 1000,
                  c['ready_active_rms_dbfs']) for f in spec['hz']], onsets_seconds),
                  f'2×3 {spec["id"]} · {spec["label"]}', 'ready', cue_ids=['DEF-READY-ENTRY-2X3'],
                  pulse_count=6, onsets_samples=[round(v * RATE) for v in onsets_seconds])
    pack.save('ready-double', pattern([tone(870, .14, -22)] * 2, [0, .3]),
              '870 Hz 双声候选（原创）', 'ready', cue_ids=['DEF-READY-ENTRY-DOUBLE'], pulse_count=2)
    pack.save('hold-continuous', tone(1080, 3, -34), '低响度持续就绪', 'ready', cue_ids=['DEF-READY-HOLD'])
    pack.save('hold-periodic', pattern([tone(1080, .13, -34)] * 4, [0, .8, 1.6, 2.4]),
              '周期就绪', 'ready', cue_ids=['DEF-READY-HOLD'], pulse_count=4)
    feedback = [
        ('sync-enter', '进入同步', 'DEF-SYNC-ENTER', [520, 680], .06, .10),
        ('sync-lost', '同步源丢失', 'DEF-SYNC-LOST', [600, 420], .14, .23),
        ('disarm', '解除充电', 'DEF-DISARM', [540, 360], .07, .11),
        ('shock-recorded', '模拟放电已记录', 'DEF-SHOCK-RECORDED', [470], .09, .12),
        ('therapy-error', '操作无效／治疗错误', 'DEF-THERAPY-ERROR', [330, 330], .085, .16),
    ]
    for key, label, cue, notes, duration, interval in feedback:
        pack.save(key, pattern([tone(f, duration) for f in notes], [i * interval for i in range(len(notes))]),
                  label, 'feedback', cue_ids=[cue], pulse_count=len(notes))
    # Continuous excerpts have shortened durations for audition, not treatment timing.
    for key, charge, ready, hold in [('d3', 'charge-continuous', 'ready-double', None),
                                    ('familiar', 'charge-continuous', 'ready-A', 'hold-continuous'),
                                    ('fixed', 'charge-fixed', None, 'hold-continuous'),
                                    ('periodic', 'charge-periodic', None, 'hold-continuous')]:
        pieces = [pack.samples[charge]]
        if ready:
            pieces.append(pack.samples[ready])
        if hold:
            pieces.append(pack.samples[hold])
        pieces.append(pack.samples['disarm'])
        pack.save(f'flow-{key}', sum(pieces, []), f'{key} 充电→就绪→解除（缩短试听）', 'flows',
                  runtime_validation=False, hold_mode='Off' if hold is None else 'Continuous')
    mix = list(pack.samples['charge-continuous'])
    ta = pack.samples['ta-A-single']
    for onset in [.2, 1, 1.8, 2.6, 3.4]:
        # Brief -9 dB dip around ta, with 5 ms attack and 25 ms release.
        start, end = round((onset - .005) * RATE), round((onset + .06) * RATE)
        for i in range(start, end):
            t = i / RATE - onset
            weight = min(1, max(0, (t + .005) / .005), max(0, (.06 - t) / .025))
            mix[i] *= 1 - weight * (1 - 10 ** (-9 / 20))
        place(mix, ta, onset)
    pack.save('sync-over-charge', mix, '同步 ta 穿插充电（短暂压低充电声）', 'flows', ducking_db=-9)
    montage = []
    chapters = []
    for key in ['ta-A-sync', 'ta-B-sync', 'ta-C-sync', 'charge-continuous', 'charge-steps',
                'ready-A', 'ready-B', 'ready-C', 'ready-double', 'ta-A-cpr']:
        chapters.append(dict(file=key + '.wav', start_s=len(montage) / RATE))
        montage += pack.samples[key] + silence(.8)
    pack.save('quick-listen', montage, '快速总览 · ta A/B/C→充电两种→2×3 A/B/C→双声→CPR', 'overview', chapters=chapters)


def build_speech(pack, models):
    import sherpa_onnx

    c = pack.config
    zh = models / 'vits-melo-tts-zh_en'
    en = models / 'vits-ljs'
    # Explicit authored pronunciations avoid silent omission of medical terms.
    lexicon_text = (en / 'lexicon.txt').read_text(encoding='utf-8')
    extra = c['english_pronunciation_candidates']
    required = set(re.findall(r"[a-z]+(?:'[a-z]+)?", ' '.join(p['en'].lower() for p in c['prompts'])))
    # The upstream lexicon has unrelated entries with unsupported IPA tokens.
    # Freeze only the vocabulary needed by these fixed prompts.
    selected = [line for line in lexicon_text.splitlines()
                if line.strip() and line.split()[0] in required - extra.keys()]
    selected += [f'{k} {v}' for k, v in extra.items()]
    tokens = {line.split()[0] for line in (en / 'tokens.txt').read_text(encoding='utf-8').splitlines() if line.strip()}
    assert all(set(line.split()[1:]) <= tokens for line in selected), 'Unsupported pronunciation token'
    lexicon = models / 'aed-english-lexicon.txt'
    lexicon.write_text('\n'.join(selected) + '\n', encoding='utf-8')
    words = {line.split()[0] for line in lexicon.read_text(encoding='utf-8').splitlines() if line.strip()}
    assert not required - words, f'Missing English pronunciations: {required - words}'
    voices = {
        'zh-CN': dict(model=str(zh / 'model.onnx'), lexicon=str(zh / 'lexicon.txt'),
                      tokens=str(zh / 'tokens.txt'), dict_dir=str(zh / 'dict')),
        'en': dict(model=str(en / 'vits-ljs.onnx'), tokens=str(en / 'tokens.txt'),
                   lexicon=str(lexicon)),
    }
    provenance = {'engine': 'sherpa-onnx', 'engine_version': sherpa_onnx.__version__, 'models': {},
                  'status': 'Audition only; pronunciation, human approval and release rights review pending',
                  'model_sources': ['https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models',
                                    'https://huggingface.co/myshell-ai/MeloTTS-Chinese',
                                    'https://keithito.com/LJ-Speech-Dataset/',
                                    'https://github.com/jaywalnut310/vits'],
                  'wave_license': 'No new license assigned to generated assets; retain source notices; not release approved'}
    for lang, params in voices.items():
        provenance['models'][lang] = {Path(v).name: hashlib.sha256(Path(v).read_bytes()).hexdigest()
                                      for v in params.values() if Path(v).is_file()}
        model = sherpa_onnx.OfflineTtsModelConfig(vits=sherpa_onnx.OfflineTtsVitsModelConfig(**params),
                                                num_threads=4, provider='cpu')
        config = sherpa_onnx.OfflineTtsConfig(model=model, max_num_sentences=1)
        assert config.validate()
        tts = sherpa_onnx.OfflineTts(config)
        for prompt in c['prompts']:
            text = prompt['zh_CN' if lang == 'zh-CN' else 'en']
            for variant in c['voice_variants']:
                key = f'voice/{lang}/{variant["id"]}/{prompt["prompt_id"]}'
                cached = models / 'rendered' / f'{lang}-{variant["id"]}-{prompt["prompt_id"]}.wav'
                cache_meta = cached.with_suffix('.json')
                identity = dict(text=text, speed=variant['speed'], model=provenance['models'][lang],
                                engine=provenance['engine_version'])
                if not (cached.exists() and cache_meta.exists()
                        and json.loads(cache_meta.read_text(encoding='utf-8')) == identity):
                    cached.parent.mkdir(parents=True, exist_ok=True)
                    rendered = tts.generate(text, sid=0, speed=variant['speed'])
                    assert len(rendered.samples) > rendered.sample_rate / 5
                    raw = cached.with_suffix('.raw.wav')
                    sherpa_onnx.write_wave(str(raw), rendered.samples, rendered.sample_rate)
                    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(raw), '-ar', str(RATE),
                                    '-ac', '1', '-c:a', 'pcm_s16le', str(cached)], check=True)
                    raw.unlink()
                    cache_meta.write_text(json.dumps(identity, ensure_ascii=False), encoding='utf-8')
                samples = read_pcm(cached)
                # Normalize active speech RMS to -23 dBFS, bounded by -6 dBFS peak.
                active = [v for v in samples if abs(v) > .003]
                assert active, key
                rms = math.sqrt(sum(v * v for v in active) / len(active))
                gain = min(10 ** (-23 / 20) / rms, .5 / max(abs(v) for v in samples))
                samples = [v * gain for v in samples]
                samples = silence(.08) + samples + silence(.12)
                pack.save(key, samples, f'{prompt["prompt_id"]} · {variant["id"]} · {text}', f'voice-{lang}',
                          prompt_id=prompt['prompt_id'], language=lang, text=text,
                          priority=prompt['priority'], speed=variant['speed'], voice_candidate=variant['id'],
                          human_approved=False, production_enabled=False,
                          reserved=prompt['prompt_id'] == 'AED_MODE_PED')
            print(f'Speech: {lang} {prompt["prompt_id"]}', flush=True)
    for source, name in [(zh / 'LICENSE', 'MeloTTS-LICENSE.txt'), (en / 'LICENSE', 'English-LICENSE.txt')]:
        shutil.copyfile(source, pack.output / name)
    (pack.output / 'voice-provenance.json').write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for lang in voices:
        def prompt(name):
            return pack.samples[f'voice/{lang}/A/{name}']
        # A VS0 excerpt illustrates voice foregrounding without resetting Ramp.
        foreground = prompt('AED_MOTION')
        mixed = list(pack.samples['charge-stall'])
        start_s = .4
        end_s = start_s + len(foreground) / RATE
        for i in range(len(mixed)):
            t = i / RATE
            weight = min(1, max(0, (t - start_s + .04) / .04), max(0, (end_s + .12 - t) / .12))
            mixed[i] *= 1 - weight * (1 - 10 ** (-12 / 20))
        place(mixed, foreground, start_s)
        pack.save(f'voice-over-charge-{lang}', mixed, f'{lang} · VS0 语音压低充电声，随后恢复当前进度',
                  'flows', ducking_db=-12, runtime_validation=False)
        # CPR ratio demos use separate absolute-phase timing, no QRS events.
        for count in [30, 15]:
            tick = pack.samples['ta-A-single']
            begin = prompt('AED_CPR_START') + silence(.25)
            first = len(begin) / RATE
            onset_samples = [round((first + i * 60 / 110) * RATE) for i in range(count)]
            segment = silence(count * 60 / 110)
            for i in range(count):
                place(segment, tick, i * 60 / 110)
            breaths = prompt('AED_CPR_BREATHS')
            gap = max(c['ventilation_gap_s_candidate'], len(breaths) / RATE + .25)
            result = begin + segment + breaths + silence(gap - len(breaths) / RATE)
            resume = len(result) / RATE
            result += pattern([tick] * 3, [i * 60 / 110 for i in range(3)])
            pack.save(f'cpr-{count}-2-{lang}', result, f'{lang} · {count}:2 一轮与下一轮前三拍', 'flows',
                      cpr_rate_bpm=110, compression_count=count, ventilation_gap_s=gap,
                      tick_onsets_samples=onset_samples + [round((resume + i * 60 / 110) * RATE) for i in range(3)],
                      reserved=count == 15, ventilation_gap_approved=False)
        sequence = ['AED_ANALYZING', 'AED_SHOCKABLE']
        result = []
        chapters = []
        for name in sequence:
            chapters.append(dict(prompt_id=name, start_s=len(result) / RATE))
            result += prompt(name) + silence(.25)
        result += pack.samples['charge-continuous']
        for part in [prompt('AED_READY'), pack.samples['ready-double'], silence(.8),
                     prompt('AED_SHOCK_RECORDED'), silence(.25), prompt('AED_CPR_START'), silence(.25),
                     pack.samples['ta-A-cpr']]:
            result += part
        pack.save(f'aed-flow-{lang}', result, f'AED {lang} · 分析→充电→离开患者→双声→模拟电击记录→CPR', 'flows',
                  time_compressed=True, runtime_validation=False, chapters=chapters)


def finish(pack, source):
    output = pack.output
    has_speech = any('prompt_id' in item for item in pack.files)
    manifest = dict(schema='Monitor.TherapyAuditionFiles@1', status='CandidateOnly',
                    pacer_dedicated_sound=False, speech_included=has_speech, files=pack.files)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    shutil.copyfile(source, output / 'candidates.json')
    sections = []
    labels = {'overview':'从这里开始', 'ta':'机械 ta · A/B/C', 'charge':'充电声', 'ready':'就绪声',
              'feedback':'操作反馈', 'reference':'心搏音对照', 'flows':'组合流程',
              'voice-zh-CN':'AED 普通话 · A 自然 / B 稍慢', 'voice-en':'AED 英语 · A 自然 / B 稍慢'}
    for key in labels:
        items = [f for f in pack.files if f['section'] == key]
        if not items:
            continue
        cards = []
        for item in items:
            label = html.escape(item['label'])
            path = html.escape(item['file'], quote=True)
            cards.append(f'<article><p>{label}</p><audio controls preload="none" src="{path}"></audio>'
                         f'<small>{item["duration_s"]:.2f} 秒</small> <a download href="{path}">WAV</a></article>')
        sections.append(f'<section id="{key}"><h2>{labels[key]}</h2><div class="grid">{"".join(cards)}</div></section>')
    nav = ' · '.join(f'<a href="#{k}">{v}</a>' for k, v in labels.items())
    (output / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>治疗声音 · 首轮候选</title>
<style>body{font:16px system-ui,sans-serif;max-width:1180px;margin:30px auto;padding:0 24px;background:#f3f5f8;color:#182e41}
h1{font-size:30px}p{line-height:1.6}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:14px}
article{background:white;padding:16px;border-radius:12px}audio{width:100%}a{color:#146a83}small{color:#576879}
nav{line-height:2}section{margin:36px 0}button{padding:8px 16px}</style>
<h1>除颤 / AED / CPR · 首轮候选</h1>
<p>教学模拟。按已冻结的声音范围制作；音色、语速、相对响度仍待试听。V1 起搏无专用声音。
请选择单项播放，保持系统音量一致；本页不自动播放。A/B/C 是候选编号，可分项混选。</p>
<p>ta 三组有效段 RMS 相同；充电与保持声刻意较轻。数字响度不等于扬声器声压标定。
2×3 为型号待考证的熟悉型候选；所有非语音从零合成。组合音频只展示编排，不证明状态机或时延通过。</p>
<p>语音 A/B 为同一发音人的两种语速，不是两位发音人。文案来自需求 §8.3，尚待真人发音与术语审核。
儿童提示和 15:2 仅供扩展试听，V1 内建课程未启用；5 秒通气窗口为候选。</p>
<button onclick="document.querySelectorAll('audio').forEach(a=>{a.pause();a.currentTime=0})">停止全部</button>
<p><a href="therapy-auditions-r1.zip" download>下载完整候选包</a></p><nav>''' + nav + '</nav>' + ''.join(sections) + '''
<script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>{
document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})}))</script></html>''', encoding='utf-8')
    (output / 'README.txt').write_text('''治疗声音首轮候选（未批准用于发布）
解压后用浏览器打开 index.html，或直接播放 WAV。全部为 48 kHz / mono / PCM16。
先听 quick-listen.wav，选 ta A/B/C、连续/阶梯充电、2×3 A/B/C 或双声，再审中英文语音。
完整参数和需求 SHA-256 见 candidates.json；资产哈希、文本和事件映射见 manifest.json。
原始声线、引擎/模型哈希与来源见 voice-provenance.json 和随附原始许可/模型卡。
语音 A/B 是同一声线的 1.0 / 0.9 语速；全部文案原样取自需求，不代表已通过真人审校。
V1 Pacer 无专用声音。儿童模式/15:2 仅保留试听。无额外充电开始 beep，无庆祝放电音。
非语音 WAV 仅为参数化合成试听导出，正式运行仍走共享 AudioDirector；不能用整段 WAV 驱动治疗流程。
充电片段长 4 秒，暂停样例为 6 秒；组合流程缩短等待，不冻结实际充电/自动解除时间。
CPR 110 BPM，30:2/15:2 通气窗口 5 秒为候选，下一轮前三拍用于检查衔接。
声音可懂度、混淆率、硬件声压/时延、真人试听和发布资产权利审批尚未通过。
工具和参数位于源码 tools/generate_therapy_auditions.py 与 eng/audio/therapy-audition-candidates.json。
用 --tts-models 指向离线模型缓存可重新生成完整包；不传时只生成非语音候选。
''', encoding='utf-8')
    with zipfile.ZipFile(output / 'therapy-auditions-r1.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        names = [f['file'] for f in pack.files] + ['index.html', 'manifest.json', 'candidates.json', 'README.txt']
        if has_speech:
            names += ['voice-provenance.json', 'MeloTTS-LICENSE.txt', 'English-LICENSE.txt']
        for name in names:
            archive.write(output / name, name)
    print(f'Generated and decoded {len(pack.files)} WAV candidates: {output}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/therapy-auditions-r1')
    parser.add_argument('--tts-models', type=Path)
    args = parser.parse_args()
    source = ROOT / 'eng/audio/therapy-audition-candidates.json'
    config = json.loads(source.read_text(encoding='utf-8'))
    pack = Audition(args.output.resolve(), config)
    build_tones(pack)
    if args.tts_models:
        build_speech(pack, args.tts_models.resolve())
    finish(pack, source)


if __name__ == '__main__':
    main()
