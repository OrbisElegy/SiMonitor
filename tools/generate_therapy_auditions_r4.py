#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Audition native English synthesis and the selected AISHELL-3 Mandarin voice.

Offline dependencies: cached R2 clauses, Matcha models, numpy/scipy/sherpa-onnx.
No runtime assets are changed. Selected Mandarin WAVs are copied verbatim.
"""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import zipfile

import numpy as np
from scipy.signal import resample_poly

from generate_therapy_auditions import Audition, RATE, ROOT, read_pcm, silence
from generate_therapy_auditions_r2 import digest, montage, preserve
from selected_therapy_voice import selected_voice_path


def level(samples):
    """Constant gain only; same active-RMS target for each audition."""
    x = np.asarray(samples, dtype=float)
    active = np.abs(x) > .002
    rms = np.sqrt(np.mean(x[active] ** 2))
    assert rms > 0 and np.isfinite(x).all()
    x *= min(10 ** (-26 / 20) / rms, .55 / np.max(np.abs(x)))
    return x.tolist()


def native_vits(models, text, version):
    en = models / 'vits-ljs'
    settings = dict(model=str(en / 'vits-ljs.onnx'), tokens=str(en / 'tokens.txt'),
                    lexicon=str(models / 'aed-english-lexicon.txt'), noise_scale=.35, noise_scale_w=.3)
    result = []
    for part in [s.strip() + '.' for s in re.split(r'[.!?]', text) if s.strip()]:
        identity = dict(text=part, settings=settings, version=version)
        key = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
        path = models / 'r2-clauses' / (key + '.wav')
        if result:
            result += silence(.16)
        result += read_pcm(path)
    return level(result)


def matcha(models, language):
    import sherpa_onnx
    if language != 'en':
        raise ValueError('Selected Mandarin recordings must be loaded from project assets')
    directory = models / 'r4/matcha-icefall-en_US-ljspeech'
    options = dict(acoustic_model=str(directory / 'model-steps-3.onnx'),
                   vocoder=str(models / 'r4/vocos-22khz-univ.onnx'), tokens=str(directory / 'tokens.txt'),
                   data_dir=str(directory / 'espeak-ng-data'))
    config = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
        matcha=sherpa_onnx.OfflineTtsMatchaModelConfig(**options), num_threads=4), max_num_sentences=1)
    assert config.validate()
    return sherpa_onnx.OfflineTts(config), {k: digest(Path(v)) for k, v in options.items() if Path(v).is_file()}


def render(tts, text, models, hashes, speed):
    import sherpa_onnx
    result = []
    for part in [s.strip() for s in re.split(r'[，。.!?]', text) if s.strip()]:
        # Expand only the spoken acronym; frozen display text remains unchanged.
        spoken = part.replace('CPR', 'C P R') + ('。' if any('\u4e00' <= c <= '\u9fff' for c in part) else '.')
        identity = dict(text=spoken, hashes=hashes, speed=speed, engine=sherpa_onnx.__version__)
        key = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
        cache = models / 'r4/rendered' / (key + '.npy')
        cache.parent.mkdir(parents=True, exist_ok=True)
        if cache.exists():
            samples = np.load(cache)
        else:
            audio = tts.generate(spoken, sid=0, speed=speed)
            assert audio.sample_rate == 22050
            samples = resample_poly(np.asarray(audio.samples, dtype=float), 320, 147)
            np.save(cache, samples)
        if result:
            result += silence(.16)
        result += samples.tolist()
    return level(result)


def build(pack, models, prompts):
    import sherpa_onnx
    r2 = ROOT / 'artifacts/therapy-auditions-r2'
    for source, name in [('P7-single', 'selected-pacer'), ('C2-single', 'selected-cpr'),
                         ('selected-charge', 'selected-charge'), ('selected-ready', 'selected-ready'),
                         ('selected-heartbeat-single', 'selected-heartbeat'), ('P7-60', 'pacer-preview'),
                         ('C2-110', 'cpr-preview')]:
        preserve(pack, r2 / (source + '.wav'), name, name, 'selected', accepted_timbre=True)
    focus = pack.config['focus_prompts']
    for p in prompts:
        key = p['prompt_id']
        preserve(pack, r2 / f'voice/zh-CN/A/{key}.wav', f'voice/zh-CN/fallback/{key}', p['zh_CN'],
                 'fallback', prompt_id=key, text=p['zh_CN'], temporary_fallback=True)
        pack.save(f'voice/en/E1/{key}', native_vits(models, p['en'], sherpa_onnx.__version__),
                  'E1 · ' + p['en'], 'E1', prompt_id=key, text=p['en'], human_approved=False,
                  processing='Native cached VITS clauses; constant gain only; no WORLD or dynamic filters')
    en, en_hashes = matcha(models, 'en')
    for p in prompts:
        key = p['prompt_id']
        pack.save(f'voice/en/E2/{key}', render(en, p['en'], models, en_hashes, 1.0), 'E2 · ' + p['en'],
                  'E2', prompt_id=key, text=p['en'], human_approved=False,
                  processing='Native Matcha LJS + Vocos; resampling and constant gain only')
        print('E2', key, flush=True)
        if key in focus:
            # Archived rejected output is a control, never a selected voice.
            for variant, source in [('R2', r2 / f'voice/en/A/{key}.wav'),
                                    ('R3', ROOT / f'artifacts/therapy-auditions-r3/voice/en/A-clean/{key}.wav')]:
                pack.save(f'compare/{key}-{variant}', level(read_pcm(source)), variant + ' · ' + p['en'],
                          'compare', source_sha256=digest(source), selected=False)
            montage(pack, [f'voice/en/E1/{key}', f'voice/en/E2/{key}'], f'focus/{key}',
                    p['en'] + '（E1 → E2）', 'focus')
    del en
    chinese_directory = ROOT / 'eng/audio/voices/zh-CN/F1'
    chinese_provenance = json.loads((chinese_directory / 'generation-provenance.json').read_text(encoding='utf-8'))
    zh_hashes = chinese_provenance['hashes']
    for p in prompts:
        key = p['prompt_id']
        preserve(pack, selected_voice_path(key, p['zh_CN']), f'voice/zh-CN/F1/{key}',
                 'F1 / FastSpeech2-A · ' + p['zh_CN'], 'F1', prompt_id=key,
                 text=p['zh_CN'], human_approved=True,
                 license_scope=chinese_provenance['license_status'])
        print('F1', key, flush=True)
    montage(pack, [f'voice/zh-CN/F1/{key}' for key in focus], 'chinese-female-reference',
            'F1 普通话女声 · 参考试听', 'focus')
    montage(pack, [f'voice/zh-CN/F1/{p["prompt_id"]}' for p in prompts],
            'chinese-female-full', 'F1 普通话女声 · 完整 22 句', 'focus')
    provenance = dict(engine=sherpa_onnx.__version__, english=en_hashes, chinese=zh_hashes,
                      script_sha256=digest(Path(__file__)), runtime_changed=False,
                      processing='No WORLD, pitch shift, dynamic band processing, limiting or denoising',
                      listening_status='User review pending; numeric checks do not establish that popping is fixed')
    (pack.output / 'provenance.json').write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for name in ['NOTICE.txt', 'UPSTREAM-SOURCES.txt', 'PaddleSpeech-LICENSE.txt']:
        shutil.copyfile(chinese_directory / name, pack.output / ('F1-' + name))


def finish(pack, source):
    out = pack.output
    shutil.copyfile(source, out / 'selections.json')
    (out / 'manifest.json').write_text(json.dumps(dict(schema='Monitor.TherapyAuditionFiles@4', files=pack.files),
                                                ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    labels = {'focus': '关键句：E1 原声 / E2 新模型', 'F1': '已选普通话女声：FastSpeech2-A',
              'fallback': '中文暂用：第二轮 A', 'E1': 'E1 全部英文', 'E2': 'E2 全部英文',
              'selected': '已选提示音', 'compare': '历史对照：R2 / 已否决 R3'}
    sections = []
    for section, label in labels.items():
        cards = []
        for f in pack.files:
            if f['section'] == section:
                path = html.escape(f['file'], quote=True)
                cards.append(f'<article><p>{html.escape(f["label"])}</p><audio controls preload="none" src="{path}"></audio></article>')
        sections.append(f'<h2>{label}</h2><div class="grid">{"".join(cards)}</div>')
    (out / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>治疗音频 · 第四轮</title><style>body{font:16px system-ui;max-width:1100px;margin:32px auto;padding:0 20px;background:#f5f7f9;color:#183344}p{line-height:1.7}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:14px}article{background:white;padding:16px}audio{width:100%}</style>
<h1>第四轮 · 英文原生合成对照 / 中文 FastSpeech2-A</h1>
<p>第三轮英文处理与 Z1–Z3 中文撤出候选。英文喷麦原因尚未确定，本轮不宣称已修复。
E1 为第二轮变调前的原生 VITS；E2 为 Matcha + Vocos，使用同一英文语料的另一套合成模型。
两者仅作采样率转换、常量音量调整及句间停顿，不做音高重合成或动态滤波。关键句顺序均为 E1 → E2，间隔 0.7 秒。</p>
<p>F1 使用已选定的 FastSpeech2-A / AISHELL-3 SSB0534，原文件逐字节保留；第二轮 A 仅作历史对照。来源和许可说明见 F1-NOTICE.txt。已选起搏、CPR、充电、就绪和心搏音保留。</p>
<p>历史英文也以相同活跃段 RMS 目标重新调整常量音量供对照；这不保证主观响度完全一致。教学模拟试听，未接入运行时。</p>
<p><a href="therapy-auditions-r4.zip">下载完整包</a></p>''' + ''.join(sections) + '''<script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})))</script></html>''', encoding='utf-8')
    (out / 'README.txt').write_text('第四轮试听。打开 index.html。F1 为已选 FastSpeech2-A / AISHELL-3 SSB0534，原文件逐字节保留。\n来源和许可见 F1-NOTICE.txt。全部音频 PCM16/48kHz/mono，未集成运行时。\n', encoding='utf-8')
    with zipfile.ZipFile(out / 'therapy-auditions-r4.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(out.rglob('*')):
            if path.is_file() and path.suffix != '.zip':
                archive.write(path, path.relative_to(out))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/therapy-auditions-r4')
    parser.add_argument('--tts-models', type=Path, default=ROOT / '.cache/therapy-tts')
    args = parser.parse_args()
    source = ROOT / 'eng/audio/therapy-audition-r4.json'
    config = json.loads(source.read_text(encoding='utf-8'))
    prompts = json.loads((ROOT / 'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))['prompts']
    pack = Audition(args.output.resolve(), config)
    build(pack, args.tts_models.resolve(), prompts)
    finish(pack, source)


if __name__ == '__main__':
    main()
