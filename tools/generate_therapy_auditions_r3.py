#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Preserve selected tones, de-pop English A, audition new Mandarin speakers.

Offline: numpy 2.2.6, scipy 1.15.3, sherpa-onnx 1.12.26; cached Kokoro v1.0.
Requires R2 exports. No production assets or runtime mappings are changed.
"""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import wave
import zipfile

import numpy as np
from scipy.ndimage import uniform_filter1d
from scipy.signal import butter, sosfiltfilt, resample_poly

from generate_therapy_auditions import Audition, RATE, ROOT, pattern, read_pcm, silence
from generate_therapy_auditions_r2 import digest, montage, preserve, speech_level


def depop(samples, config, stronger=False):
    x = np.asarray(samples, dtype=float)
    x = sosfiltfilt(butter(3, config['highpass_hz'], btype='highpass', fs=RATE, output='sos'), x)
    low = sosfiltfilt(butter(3, config['low_band_hz'], fs=RATE, output='sos'), x)
    high = sosfiltfilt(butter(3, config['high_band_hz'], btype='highpass', fs=RATE, output='sos'), x)
    middle = x - low - high
    def gain(band, threshold, floor):
        env = np.sqrt(np.maximum(0, uniform_filter1d(band * band, round(RATE * .01))))
        reference = uniform_filter1d(env, round(RATE * .2))
        ratio = env / np.maximum(reference, .0003)
        gains = np.clip((np.maximum(ratio, threshold) / threshold) ** -1.2, floor, 1)
        return uniform_filter1d(gains, round(RATE * .016))
    low_gain = gain(low, 1.35, config['low_floor_gain'])
    high_gain = gain(high, 1.15 if stronger else 1.35, .35 if stronger else config['high_floor_gain'])
    y = middle + low * low_gain + high * high_gain
    # Keep consonants: no gates, cuts, time compression, pitch shifts or clipping.
    n = min(240, len(y) // 4)
    y[:n] *= np.linspace(0, 1, n)
    y[-n:] *= np.linspace(1, 0, n)
    assert max(abs(y)) < .8
    stats = {'min_low_gain':float(min(low_gain)), 'min_high_gain':float(min(high_gain)),
             'low_energy_ratio':float(np.sum((low * low_gain) ** 2) / max(np.sum(low ** 2), 1e-12)),
             'high_energy_ratio':float(np.sum((high * high_gain) ** 2) / max(np.sum(high ** 2), 1e-12)),
             'frames_unchanged':len(y) == len(samples), 'stronger':stronger}
    return y.tolist(), stats


def build(pack, models, prompts):
    import sherpa_onnx
    r2 = ROOT / 'artifacts/therapy-auditions-r2'
    selected = [('P7-single', 'selected-pacer', '已选起搏 · P7 微型继电器开启'),
                ('C2-single', 'selected-cpr', '已选 CPR · C2 干短点击'),
                ('selected-charge', 'selected-charge', '已选 · 连续充电'),
                ('selected-ready', 'selected-ready', '已选 · 870 Hz 双声'),
                ('selected-heartbeat-single', 'selected-heartbeat', '已选 · 现用监护心搏音')]
    for source, name, label in selected:
        preserve(pack, r2 / (source + '.wav'), name, label, 'selected', accepted=True)
    preserve(pack, r2 / 'P7-60.wav', 'pacer-preview', '起搏 P7 · 60 PPM', 'overview', accepted_timbre=True)
    preserve(pack, r2 / 'C2-110.wav', 'cpr-preview', 'CPR C2 · 110 BPM', 'overview', accepted_timbre=True)
    compare_ids = ['AED_CPR_STOP', 'AED_READY', 'AED_APPLY_PADS', 'AED_CPR_START']
    for p in prompts:
        key = p['prompt_id']
        src = r2 / f'voice/en/A/{key}.wav'
        samples = read_pcm(src)
        cleaned, stats = depop(samples, pack.config['depop'])
        pack.save(f'voice/en/A-clean/{key}', cleaned, p['en'], 'english', prompt_id=key, text=p['en'],
                  language='en', variant='A-clean', source_sha256=digest(src), processing=stats,
                  human_approved=False, production_enabled=False)
        if key in compare_ids:
            preserve(pack, src, f'compare/en/{key}-before', '修复前 · ' + p['en'], 'compare')
            stronger, extra = depop(samples, pack.config['depop'], True)
            pack.save(f'compare/en/{key}-stronger', stronger, '增强处理 · ' + p['en'], 'compare', processing=extra)
            montage(pack, [f'compare/en/{key}-before', f'voice/en/A-clean/{key}', f'compare/en/{key}-stronger'],
                    f'compare/en/{key}-all', key + ' · 原版→修复→增强', 'compare')
    montage(pack, [f'voice/en/A-clean/{p}' for p in compare_ids], 'english-preview', '英文平叙 A · 去气流瞬态', 'overview')
    montage(pack, ['compare/en/AED_CPR_STOP-before', 'voice/en/A-clean/AED_CPR_STOP',
                  'compare/en/AED_CPR_STOP-stronger'], 'english-stop-focus',
            '重点：Stop CPR… · 原版→修复→增强', 'overview')
    model_dir = models / 'r3/kokoro-multi-lang-v1_0'
    config = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
        kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(model=str(model_dir / 'model.onnx'),
            voices=str(model_dir / 'voices.bin'), tokens=str(model_dir / 'tokens.txt'),
            data_dir=str(model_dir / 'espeak-ng-data'), dict_dir=str(model_dir / 'dict'),
            lexicon=str(model_dir / 'lexicon-zh.txt') + ',' + str(model_dir / 'lexicon-us-en.txt')),
        num_threads=4), max_num_sentences=1)
    assert config.validate()
    tts = sherpa_onnx.OfflineTts(config)
    model_hashes = {name:digest(model_dir / name) for name in ['model.onnx','voices.bin','tokens.txt','lexicon-zh.txt','lexicon-us-en.txt']}
    for candidate in pack.config['chinese_candidates']:
        for p in prompts:
            parts = [s.strip() + '。' for s in re.split('[，。]', p['zh_CN']) if s.strip()]
            result = []
            for i, text in enumerate(parts):
                identity = dict(text=text, speaker=candidate, model_hashes=model_hashes, engine=sherpa_onnx.__version__)
                key = hashlib.sha256(json.dumps(identity, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
                cache = models / 'r3/rendered' / (key + '.npy')
                cache.parent.mkdir(parents=True, exist_ok=True)
                if cache.exists():
                    values = np.load(cache)
                else:
                    rendered = tts.generate(text, sid=candidate['sid'], speed=candidate['speed'])
                    assert rendered.sample_rate == 24000
                    values = resample_poly(np.asarray(rendered.samples, dtype=float), 2, 1)
                    np.save(cache, values)
                # The exported model has a measurable DC component. Remove it
                # per clause before adding silence to avoid boundary thumps.
                values = sosfiltfilt(butter(3, 50, btype='highpass', fs=RATE, output='sos'), values)
                values[:240] *= np.linspace(0, 1, 240)
                values[-240:] *= np.linspace(1, 0, 240)
                if i:
                    result += silence(.15)
                result += values.tolist()
            pack.save(f'voice/zh-CN/{candidate["id"]}/{p["prompt_id"]}', speech_level(result), p['zh_CN'],
                      candidate['id'], prompt_id=p['prompt_id'], language='zh-CN', variant=candidate['id'],
                      text=p['zh_CN'], spoken_clauses=parts, voice=candidate['voice'], sid=candidate['sid'],
                      human_approved=False, production_enabled=False)
            print(candidate['id'], p['prompt_id'], flush=True)
        montage(pack, [f'voice/zh-CN/{candidate["id"]}/{p}' for p in compare_ids],
                'chinese-' + candidate['id'], candidate['id'] + ' · ' + candidate['label'], 'overview')
    for p in compare_ids:
        preserve(pack, r2 / f'voice/zh-CN/A/{p}.wav', f'compare/zh/{p}-old', '旧中文 · ' + p, 'compare')
    # Only the agreed CPR timbre is used in new flows.
    for lang, candidate in [('en','A-clean'),('zh-CN','Z1'),('zh-CN','Z2'),('zh-CN','Z3')]:
        def voice(key):
            return pack.samples[f'voice/{lang}/{candidate}/{key}']
        result = []
        for part in [voice('AED_ANALYZING'), silence(.25), voice('AED_SHOCKABLE'), pack.samples['selected-charge'],
                     voice('AED_READY'), pack.samples['selected-ready'], silence(.5), voice('AED_SHOCK_RECORDED'),
                     silence(.25), voice('AED_CPR_START'), silence(.25), pack.samples['cpr-preview']]:
            result += part
        pack.save(f'flow-{lang}-{candidate}', result, f'{lang}/{candidate} · 已选音色组合', 'flows', runtime_validation=False)
    shutil.copyfile(model_dir / 'LICENSE', pack.output / 'Kokoro-LICENSE.txt')
    shutil.copyfile(r2 / 'English-LICENSE.txt', pack.output / 'English-LICENSE.txt')
    provenance = dict(engine=sherpa_onnx.__version__, chinese_model='Kokoro multilingual v1.0',
                      hashes=model_hashes, source=pack.config['chinese_source'],
                      english_source='R2 VITS LJS A fixed WAV; no resynthesis, no pitch/duration changes',
                      chinese_postprocessing='50 Hz highpass removes measured model DC; 5 ms clause-edge fades',
                      license_scope='Upstream model license retained; generated voice release approval remains pending',
                      source_script_sha256=digest(Path(__file__)))
    (pack.output / 'provenance.json').write_text(json.dumps(provenance, ensure_ascii=False, indent=2)+'\n',encoding='utf-8')


def finish(pack, source):
    out = pack.output
    shutil.copyfile(source, out / 'selections.json')
    (out / 'manifest.json').write_text(json.dumps(dict(schema='Monitor.TherapyAuditionFiles@3', files=pack.files,
          status='Audition; timbre selections recorded, English repair and Mandarin voices pending listening'), ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    labels={'overview':'先听这里','selected':'已选音色','compare':'修复前后对照','english':'英文 A · 全部修复版',
            'Z1':'中文 Z1 · 沉稳男声方向','Z2':'中文 Z2 · 平实男声方向','Z3':'中文 Z3 · 沉静女声方向','flows':'组合流程'}
    sections=[]
    for key,label in labels.items():
        cards=[]
        for f in pack.files:
            if f['section']!=key:
                continue
            path=html.escape(f['file'],quote=True)
            cards.append(f'<article><p>{html.escape(f["label"])}</p><audio controls preload="none" src="{path}"></audio><a download href="{path}">WAV</a></article>')
        sections.append(f'<section id="{key}"><h2>{label}</h2><div class="grid">{"".join(cards)}</div></section>')
    nav=' · '.join(f'<a href="#{k}">{v}</a>' for k,v in labels.items())
    (out/'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>治疗声音 · 第三轮</title><style>body{font:16px system-ui;max-width:1180px;margin:30px auto;padding:0 22px;background:#f3f6f8;color:#183344}
p{line-height:1.65}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:14px}article{background:white;padding:18px;border-radius:12px}
audio{width:100%}a{color:#006b78}nav{line-height:2.2}section{margin-top:35px}</style>
<h1>治疗声音 · 第三轮</h1><p>起搏锁定 P7 微型继电器开启，CPR 锁定 C2 干短点击。连续充电、870 Hz 双声及现用心搏音保留。
本轮英文保留平叙 A，仅处理气流瞬态；中文改用三位新发音人，平叙句式与语速一致。</p>
<p>英文没有数字削波。本次使用动态分频衰减，保留清辅音、不剪掉词头或词尾，不变调、不改变时长；
仍需试听确认“喷麦”是否消除。对照顺序：原版→修复→增强。中文 Z1/Z2/Z3 是不同声线，不是简单降低旧音高。</p>
<p>教学模拟 · 固定离线资产 · 发音和听感待审核。请保持系统音量一致。默认无自动播放。</p>
<button onclick="document.querySelectorAll('audio').forEach(a=>{a.pause();a.currentTime=0})">停止全部</button>
<p><a href="therapy-auditions-r3.zip" download>下载完整包</a></p><nav>'''+nav+'</nav>'+''.join(sections)+'''
<script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>{document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})}))</script></html>''',encoding='utf-8')
    (out/'README.txt').write_text('''第三轮：P7 起搏、C2 CPR 已选；英文平叙 A 去气流瞬态；中文更换三位候选发音人。
打开 index.html 试听。英文有原版/修复/增强对照；中文各含 22 条固定提示。
全部 PCM16/48kHz/mono。选定的非语音原样复制，源文件哈希见 manifest.json。
selections.json 保存用户选择；provenance.json 保存声线和模型来源。
本轮不修改正式播放映射，中文和英文修复效果仍待人工试听。
完整工具 tools/generate_therapy_auditions_r3.py，依赖与模型见其开头说明。
''',encoding='utf-8')
    with zipfile.ZipFile(out/'therapy-auditions-r3.zip','w',zipfile.ZIP_DEFLATED) as z:
        for name in [f['file'] for f in pack.files]+['index.html','README.txt','manifest.json','selections.json','provenance.json','Kokoro-LICENSE.txt','English-LICENSE.txt']:
            z.write(out/name,name)
    print(f'R3 complete: {len(pack.files)} WAVs, {out}',flush=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/therapy-auditions-r3')
    parser.add_argument('--tts-models',type=Path,default=ROOT/'.cache/therapy-tts')
    args=parser.parse_args()
    source=ROOT/'eng/audio/therapy-audition-r3.json'
    config=json.loads(source.read_text(encoding='utf-8'))
    prompts=json.loads((ROOT/'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))['prompts']
    pack=Audition(args.output.resolve(),config)
    build(pack,args.tts_models.resolve(),prompts)
    finish(pack,source)


if __name__=='__main__':
    main()
