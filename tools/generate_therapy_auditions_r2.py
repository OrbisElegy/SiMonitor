#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Second therapy audition: mechanical opening, separate CPR, declarative speech.

Offline dependencies: numpy 2.2.6, scipy 1.15.3, pyworld 0.3.5,
sherpa-onnx 1.12.26 and ffmpeg. Models are the R1 cached MeloTTS and VITS LJS.
Does not change runtime event mappings or production PCM assets.
"""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import subprocess
import wave
import zipfile

import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import butter, sosfilt, resample_poly

from generate_therapy_auditions import Audition, RATE, ROOT, pattern, read_pcm, silence, tone


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def scale(values, db=-27):
    values = np.asarray(values, dtype=np.float64)
    values -= np.mean(values)
    fade = min(144, len(values) // 4)
    values[:24] *= np.linspace(0, 1, 24)
    values[-fade:] *= np.linspace(1, 0, fade)
    values *= 10 ** (db / 20) / np.sqrt(np.mean(values ** 2))
    assert np.max(abs(values)) < .7
    return values.tolist()


def mechanism(spec, seed):
    n = round(spec['duration_ms'] * RATE / 1000)
    t = np.arange(n) / RATE
    rng = np.random.default_rng(seed)
    noise = sosfilt(butter(2, spec['band_hz'], btype='bandpass', fs=RATE, output='sos'), rng.normal(size=n))
    noise /= np.sqrt(np.mean(noise ** 2))
    delay = spec['release_ms'] / 1000
    # Impact followed by a brief spring/valve release within one audible event.
    attack = (1 - np.exp(-t / .00035)) * np.exp(-t * 1000 / spec['decay_ms'])
    release_t = np.maximum(0, t - delay)
    release = (t >= delay) * (1 - np.exp(-release_t / .0005)) * np.exp(-release_t / .008)
    body = np.sin(2 * np.pi * spec['body_hz'] * t) + .35 * np.sin(2 * np.pi * spec['body_hz'] * 2.37 * t)
    value = (spec['noise_mix'] * noise + (1 - spec['noise_mix']) * body) * attack
    value += spec['release_gain'] * (noise * .7 + np.sin(2 * np.pi * spec['body_hz'] * .71 * release_t) * .3) * release
    return scale(value)


def cpr_voice(spec):
    if spec['kind'] == 'beep':
        return tone(spec['hz'], spec['duration_ms'] / 1000, -27)
    n = round(spec['duration_ms'] * RATE / 1000)
    t = np.arange(n) / RATE
    noise = np.random.default_rng(884).normal(size=n)
    noise = sosfilt(butter(2, [700, 4000], btype='bandpass', fs=RATE, output='sos'), noise)
    body = np.sin(2 * np.pi * spec['hz'] * t) + .2 * np.sin(2 * np.pi * spec['hz'] * 1.62 * t)
    values = (noise if spec['kind'] == 'click' else body + .15 * noise) * np.exp(-t / .007)
    return scale(values)


def preserve(pack, source, name, label, section, **metadata):
    values = read_pcm(source)
    pack.save(name, values, label, section, **metadata)
    target = pack.output / (name + '.wav')
    shutil.copyfile(source, target)
    pack.files[-1]['sha256'] = digest(target)
    pack.files[-1]['source_sha256'] = digest(source)
    pack.files[-1]['preserved_bytes'] = True


def montage(pack, keys, name, label, section):
    values, chapters = [], []
    for key in keys:
        chapters.append({'file': key + '.wav', 'start_s': len(values) / RATE})
        values += pack.samples[key] + silence(.7)
    pack.save(name, values, label, section, chapters=chapters)


def build_sounds(pack, r1):
    c = pack.config
    preserve(pack, r1 / 'charge-continuous.wav', 'selected-charge', '已选 · 连续充电', 'selected', accepted=True)
    preserve(pack, r1 / 'ready-double.wav', 'selected-ready', '已选 · 870 Hz 双声', 'selected', accepted=True, pulse_count=2)
    pcm = ROOT / 'src/Monitor.Infrastructure/Audio/SelectedTones/Heartbeat.pcm'
    data = pcm.read_bytes()
    values = (np.frombuffer(data, dtype='<i2').astype(float) / 32768).tolist()
    path = pack.output / 'selected-heartbeat-single.wav'
    pack.save('selected-heartbeat-single', values, '已选 · 当前监护仪心搏音原样', 'selected', accepted=True)
    with wave.open(str(path), 'wb') as w:
        w.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        w.writeframes(data)
    pack.files[-1].update(sha256=digest(path), source_pcm_sha256=digest(pcm), preserved_pcm=True)
    pack.save('selected-heartbeat-sequence', pattern([values] * 6, [.2 + i * .8 for i in range(6)]),
              '监护仪心搏音 · 75 次/分', 'selected', pulse_count=6)
    keys = []
    for i, spec in enumerate(c['pacer_candidates']):
        key = spec['id']
        voice = mechanism(spec, 2800 + i)
        pack.save(key + '-single', voice, f'{key} · {spec["label"]} · 单声', 'pacer-single',
                  cue_id='CANDIDATE-PACER-MECHANICAL-OPEN', runtime_enabled=False, seed=2800 + i)
        for rate in [60, 90]:
            onsets_seconds = [.2 + i * 60 / rate for i in range(6)]
            pack.save(f'{key}-{rate}', pattern([voice] * 6, onsets_seconds), f'{key} · {spec["label"]} · {rate} PPM', 'pacer',
                      event_source='PacingStimulusIssued (proposed audition mapping only)', pulse_count=6,
                      onsets_samples=[round(t * RATE) for t in onsets_seconds], pacing_rate_ppm=rate,
                      capture_inferred=False)
        # Three repetitions per candidate keep the opening comparison concise.
        pack.save(key + '-short', pattern([voice] * 3, [.2, .95, 1.7]), f'{key} · {spec["label"]}', 'pacer-short', pulse_count=3)
        keys.append(key + '-short')
    montage(pack, keys, 'pacer-overview', '机械开启 P1→P8 · 每种三次', 'overview')
    keys = []
    for spec in c['cpr_candidates']:
        key = spec['id']
        voice = cpr_voice(spec)
        pack.save(key + '-single', voice, f'{key} · {spec["label"]} · 单声', 'cpr-single', cue_id='DEF-CPR-METRONOME-TA')
        for rate in c['cpr_rates_bpm']:
            onsets_seconds = [.2 + i * 60 / rate for i in range(12)]
            pack.save(f'{key}-{rate}', pattern([voice] * 12, onsets_seconds), f'{key} · {spec["label"]} · {rate} BPM', 'cpr',
                      rate_bpm=rate, pulse_count=12, event_source='cpr_compression_tick',
                      onsets_samples=[round(t * RATE) for t in onsets_seconds])
        keys.append(key + '-110')
    montage(pack, keys, 'cpr-overview', 'CPR C1→C4 · 统一 110 BPM', 'overview')
    montage(pack, [f'C1-{rate}' for rate in c['cpr_rates_bpm']], 'cpr-rates', '同音色速度对比 · 100→105→110→120 BPM', 'overview')


def speech_analysis(samples):
    import pyworld as pw
    # WORLD at 24 kHz is sufficient for speech; delivery remains 48 kHz.
    x = np.ascontiguousarray(resample_poly(np.asarray(samples), 1, 2), dtype=np.float64)
    f0, times = pw.dio(x, 24000, f0_floor=65, f0_ceil=600, frame_period=5)
    f0 = pw.stonemask(x, f0, times, 24000)
    sp = pw.cheaptrick(x, f0, times, 24000)
    ap = pw.d4c(x, f0, times, 24000)
    assert np.count_nonzero(f0) > 5
    return f0, sp, ap


def restrain(analysis, lang, variant):
    import pyworld as pw
    f0, sp, ap = analysis
    mask = f0 > 0
    ix = np.flatnonzero(mask)
    median = np.median(f0[mask])
    semitones = np.interp(np.arange(len(f0)), ix, 12 * np.log2(f0[mask] / median))
    base = gaussian_filter1d(semitones, .22 / .005)
    local = semitones - base
    suffix = 'zh' if lang == 'zh-CN' else 'en'
    # Do not flatten Mandarin lexical pitch to a constant.
    revised = variant['phrase_gain_' + suffix] * base + variant['local_gain_' + suffix] * local
    # A shallow statement baseline, not a forced falling final lexical tone.
    revised -= np.linspace(0, .45, len(f0))
    target = f0.copy()
    target[mask] = median * 2 ** (revised[mask] / 12)
    values = pw.synthesize(np.ascontiguousarray(target), sp, ap, 24000, frame_period=5)
    values = resample_poly(values, 2, 1)
    return values, dict(original_pitch_sd_semitones=float(np.std(semitones[mask])),
                        target_pitch_sd_semitones=float(np.std(revised[mask])),
                        phrase_gain=variant['phrase_gain_' + suffix],
                        local_contour_gain=variant['local_gain_' + suffix],
                        voiced_frames=int(np.count_nonzero(mask)))


def speech_level(values):
    x = np.asarray(values, dtype=float)
    active = x[abs(x) > .002]
    assert len(active) > 1000
    gain = min(10 ** (-23 / 20) / np.sqrt(np.mean(active ** 2)), .5 / max(abs(x)))
    x *= gain
    # Fade only the padded signal edges; no terminal phoneme is cut.
    x[:96] *= np.linspace(0, 1, 96)
    x[-240:] *= np.linspace(1, 0, 240)
    return silence(.08) + x.tolist() + silence(.12)


def build_voices(pack, models, r1, prompts):
    import sherpa_onnx
    c = pack.config
    zh, en = models / 'vits-melo-tts-zh_en', models / 'vits-ljs'
    configs = {
        'zh-CN': dict(model=str(zh / 'model.onnx'), lexicon=str(zh / 'lexicon.txt'),
                      tokens=str(zh / 'tokens.txt'), dict_dir=str(zh / 'dict')),
        'en': dict(model=str(en / 'vits-ljs.onnx'), tokens=str(en / 'tokens.txt'),
                   lexicon=str(models / 'aed-english-lexicon.txt')),
    }
    comparisons = ['AED_ANALYZING', 'AED_READY', 'AED_CPR_START']
    for lang, params in configs.items():
        settings = dict(**params, noise_scale=.35, noise_scale_w=.3)
        model = sherpa_onnx.OfflineTtsModelConfig(vits=sherpa_onnx.OfflineTtsVitsModelConfig(**settings), num_threads=4)
        config = sherpa_onnx.OfflineTtsConfig(model=model, max_num_sentences=1)
        assert config.validate()
        tts = sherpa_onnx.OfflineTts(config)
        for prompt in prompts:
            text = prompt['zh_CN' if lang == 'zh-CN' else 'en']
            clauses = [s.strip() for s in re.split(r'[，。.!?]', text) if s.strip()]
            assembled = {v['id']: [] for v in c['voice_variants']}
            plain = []
            reports = {v['id']: [] for v in c['voice_variants']}
            for part, clause in enumerate(clauses):
                spoken = clause + ('。' if lang == 'zh-CN' else '.')
                cache_id = hashlib.sha256(json.dumps(dict(text=spoken, settings=settings, version=sherpa_onnx.__version__),
                                                     sort_keys=True).encode()).hexdigest()
                cache = models / 'r2-clauses' / (cache_id + '.wav')
                cache.parent.mkdir(parents=True, exist_ok=True)
                if not cache.exists():
                    rendered = tts.generate(spoken, sid=0, speed=1.0)
                    raw = cache.with_suffix('.raw.wav')
                    sherpa_onnx.write_wave(str(raw), rendered.samples, rendered.sample_rate)
                    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(raw), '-ar', str(RATE),
                                    '-ac', '1', '-c:a', 'pcm_s16le', str(cache)], check=True)
                    raw.unlink()
                samples = read_pcm(cache)
                analysis_file = cache.with_suffix('.npz')
                if analysis_file.exists():
                    with np.load(analysis_file) as saved:
                        analysis = saved['f0'], saved['sp'], saved['ap']
                else:
                    analysis = speech_analysis(samples)
                    np.savez_compressed(analysis_file, f0=analysis[0], sp=analysis[1], ap=analysis[2])
                if part:
                    plain += silence(.16)
                plain += samples
                for variant in c['voice_variants']:
                    key = variant['id']
                    processed, report = restrain(analysis, lang, variant)
                    if part:
                        assembled[key] += silence(.16)
                    assembled[key] += processed.tolist()
                    reports[key].append(dict(clause=spoken, **report))
            for variant in c['voice_variants']:
                key = variant['id']
                pack.save(f'voice/{lang}/{key}/{prompt["prompt_id"]}', speech_level(assembled[key]),
                          f'{key} · {text}', f'voice-{lang}', prompt_id=prompt['prompt_id'], language=lang,
                          text=text, spoken_clauses=clauses, priority=prompt['priority'], voice_candidate=key,
                          processing=reports[key], human_approved=False, production_enabled=False)
            if prompt['prompt_id'] in comparisons:
                name = f'compare/{lang}/{prompt["prompt_id"]}'
                preserve(pack, r1 / f'voice/{lang}/A/{prompt["prompt_id"]}.wav', name + '-r1',
                         f'首轮原版 · {text}', 'comparison')
                pack.save(name + '-plain', speech_level(plain), f'仅陈述句重合成 · {text}', 'comparison')
                keys = [name + '-r1', name + '-plain'] + [f'voice/{lang}/{v}/{prompt["prompt_id"]}' for v in ['A', 'B']]
                montage(pack, keys, name + '-all', f'{lang} {prompt["prompt_id"]} · 原版→陈述句→A→B', 'comparison')
            print(f'R2 speech {lang}: {prompt["prompt_id"]}', flush=True)
        montage(pack, [f'voice/{lang}/A/{p}' for p in comparisons], f'voice-preview-{lang}',
                f'{lang} · A 平叙语音三句', 'overview')


def build_flows(pack):
    for lang in ['zh-CN', 'en']:
        def voice(name):
            return pack.samples[f'voice/{lang}/A/{name}']
        tick = pack.samples['C1-single']
        for count in [30, 15]:
            result = voice('AED_CPR_START') + silence(.25)
            start_seconds = len(result) / RATE
            segment = silence(count * 60 / 110)
            onsets_frames = []
            for i in range(count):
                onset = round(i * 60 / 110 * RATE)
                segment[onset:onset + len(tick)] = tick
                onsets_frames.append(round(start_seconds * RATE) + onset)
            result += segment
            breaths = voice('AED_CPR_BREATHS')
            result += breaths + silence(max(0, 5 - len(breaths) / RATE))
            restart_seconds = len(result) / RATE
            result += pattern([tick] * 3, [i * 60 / 110 for i in range(3)])
            onsets_frames += [round((restart_seconds + i * 60 / 110) * RATE) for i in range(3)]
            pack.save(f'cpr-{count}-2-{lang}', result, f'{lang} · {count}:2 · C1 / 110 BPM / A 平叙', 'flow',
                      compression_count=count, onsets_samples=onsets_frames, ventilation_window_candidate_s=5,
                      reserved=count == 15)
        result = []
        for part in [voice('AED_ANALYZING'), silence(.3), voice('AED_SHOCKABLE'),
                     pack.samples['selected-charge'], voice('AED_READY'), pack.samples['selected-ready'],
                     silence(.5), voice('AED_SHOCK_RECORDED'), silence(.25), voice('AED_CPR_START'),
                     silence(.25), pack.samples['C1-110']]:
            result += part
        pack.save(f'aed-flow-{lang}', result, f'{lang} · 已选充电／就绪 + 平叙语音 + C1 CPR', 'flow',
                  runtime_validation=False, time_compressed=True)


def finish(pack, source, models):
    out = pack.output
    config = pack.config
    source1 = ROOT / 'eng/audio/therapy-audition-candidates.json'
    manifest = dict(schema='Monitor.TherapyAuditionFiles@2', status='CandidateOnly',
                    source_r1_config_sha256=digest(source1), source_r2_config_sha256=digest(source),
                    runtime_changed=False, files=pack.files)
    (out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    shutil.copyfile(source, out / 'candidates.json')
    for name in ['MeloTTS-LICENSE.txt', 'English-LICENSE.txt', 'voice-provenance.json']:
        shutil.copyfile(ROOT / 'artifacts/therapy-auditions-r1' / name, out / name)
    provenance = json.loads((out / 'voice-provenance.json').read_text(encoding='utf-8'))
    provenance.update(round=2, processing=config['voice_processing'],
                      speech_input='Split clauses as declarative sentences; on-screen semantic text unchanged',
                      processing_source_sha256=digest(Path(__file__)),
                      lexical_tones='Local contour retained in A; retained at 90 percent in B; human validation pending')
    (out / 'voice-provenance.json').write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    sources = ''.join(f'<li><a href="{html.escape(s["url"], quote=True)}">{html.escape(s["id"])}</a> — {html.escape(s["finding"])}</li>' for s in config['sources'])
    research = '''<h2>调研结论与本轮选择</h2><p>D3 §6.7.1 的 CPR 节拍为 110/min，默认 30:2；ZOLL M2 A-10 为可配置的 105/min。
成人指南范围为 100–120/min。这些资料没有规定通用节拍音色；本轮 C1–C4 均为原创，105 档只表示设备差异。</p>
<p>厂家资料把感知、起搏输出和捕获分开，也区分逐脉冲视觉标记与起搏故障报警。
尚未核实“机械开启 ta”为某款设备的固定起搏音。按本轮反馈，P1–P8 单列为起搏候选，
采用碰撞后极短机械释放的方向；不再将其默认绑定 Sync 或 CPR，不把听见一声解释成捕获成功。</p>
<p>旧需求的 V1 Pacer 无专用声音仍保留作历史基线；此次用户反馈授权新的起搏试听方向，尚未修改运行时映射。
心搏音直接封装当前监护仪 Heartbeat.pcm；连续充电和 870 Hz 双声保持首轮文件字节一致。</p>
<p>普通话需要字调区分词义。本轮先按陈述句和固定停顿重合成，再抑制较慢的句调起伏。
A 保留中文局部音高轮廓；B 稍收窄作对比。英文同时收窄重音起伏。这个分离是工程近似，不能证明辨识度已改善。
未切除末字；提供原版、仅陈述句重合成、A、B 的逐句对照。</p><ul>''' + sources + '</ul>'
    (out / 'research.html').write_text('<!doctype html><meta charset="utf-8"><title>治疗声音调研</title>' + research, encoding='utf-8')
    labels = {'overview':'快速试听', 'selected':'已选声音', 'pacer-short':'机械开启 P1–P8', 'pacer':'起搏候选 · 60/90 PPM',
              'cpr':'CPR · 音色与速度', 'comparison':'语音原版 / 陈述句 / A / B 对照',
              'voice-zh-CN':'完整普通话 · A/B', 'voice-en':'完整英语 · A/B', 'flow':'组合流程'}
    sections = []
    for section, label in labels.items():
        cards = []
        for item in pack.files:
            if item['section'] != section:
                continue
            path = html.escape(item['file'], quote=True)
            cards.append(f'<article><p>{html.escape(item["label"])}</p><audio controls preload="none" src="{path}"></audio>'
                         f'<small>{item["duration_s"]:.2f} 秒</small> <a download href="{path}">WAV</a></article>')
        sections.append(f'<section id="{section}"><h2>{label}</h2><div class="grid">{"".join(cards)}</div></section>')
    nav = ' · '.join(f'<a href="#{key}">{label}</a>' for key, label in labels.items())
    (out / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>治疗声音 · 第二轮</title>
<style>body{font:16px system-ui,sans-serif;background:#f4f6f8;color:#183347;max-width:1200px;margin:28px auto;padding:0 22px}
p{line-height:1.65}nav{line-height:2.1}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(295px,1fr));gap:14px}
article,details{background:white;padding:18px;border-radius:12px}audio{width:100%}a{color:#076976}section{margin-top:34px}
button{padding:9px 18px}small{color:#547080}</style>
<h1>治疗声音 · 第二轮试听</h1><p>机械开启感 P1–P8；CPR 独立音色 C1–C4；中英文平叙 A/B。
已保留连续充电、870 Hz 双声与现用监护心搏音。教学模拟 · 候选试听 · 无自动播放。</p>
<p>建议先听机械开启总览，再听同速 CPR，最后比较语音。A/B 是平叙处理程度，不是语速差异。
保持系统音量一致；所有主观听感和语音辨识仍待试听确认。</p>
<button onclick="document.querySelectorAll('audio').forEach(a=>{a.pause();a.currentTime=0})">停止全部</button>
<p><a href="therapy-auditions-r2.zip" download>下载完整包</a> · <a href="research.html">调研与来源</a></p>
<nav>''' + nav + '</nav><details><summary>展开调研结论</summary>' + research + '</details>' + ''.join(sections) + '''
<script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>{
document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})}))</script></html>''', encoding='utf-8')
    (out / 'README.txt').write_text('''第二轮治疗声音试听
浏览器打开 index.html。调研与原始来源见 research.html；参数/反馈决定见 candidates.json。
P1–P8：机械开启方向，作为起搏候选而非已验证厂商声音，未改变运行时映射。
C1–C4：独立 CPR 音色；100/105/110/120 BPM 分别比较。110 为 D3 主参考。
语音：22 条中文及英文各有 A/B 平叙版本；六组对照保留首轮与仅陈述句重合成。
语音 A 中文保留局部字调，B 略收窄；英文同步收敛。人工辨识验证仍未通过。
充电/就绪 WAV 原样复制首轮选项；心搏单声 WAV 数据直接来自已选 Heartbeat.pcm。
CPR 15:2 保留为扩展试听；通气窗口 5 秒仅为候选。整段流程为缩短的编排试听。
全部 48 kHz/mono/PCM16。哈希与声源映射见 manifest.json。原始模型许可另附。
源码工具 tools/generate_therapy_auditions_r2.py；不依赖在线 TTS，不接入生产音频。
''', encoding='utf-8')
    with zipfile.ZipFile(out / 'therapy-auditions-r2.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        names = [f['file'] for f in pack.files] + ['manifest.json', 'candidates.json', 'index.html', 'research.html',
                 'README.txt', 'voice-provenance.json', 'MeloTTS-LICENSE.txt', 'English-LICENSE.txt']
        for name in names:
            archive.write(out / name, name)
    print(f'R2 complete: {len(pack.files)} WAVs in {out}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/therapy-auditions-r2')
    parser.add_argument('--tts-models', type=Path, default=ROOT / '.cache/therapy-tts')
    args = parser.parse_args()
    source = ROOT / 'eng/audio/therapy-audition-r2.json'
    config = json.loads(source.read_text(encoding='utf-8'))
    prompts = json.loads((ROOT / 'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))['prompts']
    r1 = ROOT / 'artifacts/therapy-auditions-r1'
    pack = Audition(args.output.resolve(), config)
    build_sounds(pack, r1)
    build_voices(pack, args.tts_models.resolve(), r1, prompts)
    build_flows(pack)
    finish(pack, source, args.tts_models.resolve())


if __name__ == '__main__':
    main()
