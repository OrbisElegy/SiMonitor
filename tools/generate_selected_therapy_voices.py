#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Resynthesize selected fixed prompts into an audition folder after cache cleanup.

Approved repository WAVs remain canonical. Stochastic model inference may not
reproduce their bytes; newly generated files require a fresh listening review.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

from fetch_tts_models import environment_python, prepare

ROOT = Path(__file__).resolve().parents[1]


def load_prompts():
    prompts = json.loads((ROOT / 'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))['prompts']
    pad = json.loads((ROOT / 'eng/audio/therapy-pad-placement.json').read_text(encoding='utf-8'))
    return [*prompts, dict(prompt_id=pad['prompt_id'], zh_CN=pad['zh_CN'], en=pad['en'])]


def synthesize(cache, output, prompts, language):
    import numpy as np
    import onnxruntime as ort
    import sherpa_onnx
    import soundfile as sf
    from scipy.signal import resample_poly

    output.mkdir(parents=True, exist_ok=True)
    engines = {}
    if language in ('both', 'zh-CN'):
        options = ort.SessionOptions()
        options.intra_op_num_threads = 4
        options.inter_op_num_threads = 1
        ort.set_seed(1986)
        engines['acoustic'] = ort.InferenceSession(str(cache / 'fastspeech2_aishell3_onnx_1.1.0/fastspeech2_aishell3.onnx'), options, providers=['CPUExecutionProvider'])
        engines['vocoder'] = ort.InferenceSession(str(cache / 'pwgan_aishell3_onnx_1.1.0/pwgan_aishell3.onnx'), options, providers=['CPUExecutionProvider'])
        phones = json.loads((ROOT / 'eng/audio/voices/zh-CN/F1/phonemes.json').read_text(encoding='utf-8'))
    if language in ('both', 'en'):
        config = json.loads((ROOT / 'eng/audio/therapy-audition-candidates.json').read_text(encoding='utf-8'))
        required = set(re.findall(r"[a-z]+(?:'[a-z]+)?", ' '.join(p['en'].lower() for p in prompts)))
        lexicon = {line.split()[0]: line.split()[1:] for line in (cache / 'vits-ljs/lexicon.txt').read_text(encoding='utf-8').splitlines() if line.strip()}
        lexicon.update({word: value.split() for word, value in config['english_pronunciation_candidates'].items()})
        tokens = {line.split()[0] for line in (cache / 'vits-ljs/tokens.txt').read_text(encoding='utf-8').splitlines() if line.strip()}
        if required - lexicon.keys() or any(set(lexicon[word]) - tokens for word in required):
            raise ValueError('English pronunciation dictionary is incomplete')
        lexicon_path = output / 'english-lexicon.txt'
        lexicon_path.write_text(''.join(word + ' ' + ' '.join(lexicon[word]) + '\n' for word in sorted(required)), encoding='utf-8')
        config = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
            vits=sherpa_onnx.OfflineTtsVitsModelConfig(model=str(cache / 'vits-ljs/vits-ljs.onnx'),
                tokens=str(cache / 'vits-ljs/tokens.txt'), lexicon=str(lexicon_path), noise_scale=.35, noise_scale_w=.3),
            num_threads=4), max_num_sentences=1)
        if not config.validate():
            raise ValueError('English TTS configuration is invalid')
        engines['en'] = sherpa_onnx.OfflineTts(config)
    files = []
    for lang in (['zh-CN', 'en'] if language == 'both' else [language]):
        folder = output / lang
        folder.mkdir(exist_ok=True)
        for prompt in prompts:
            key = prompt['prompt_id']
            if lang == 'zh-CN':
                entry = phones[key]
                if entry['text'] != prompt['zh_CN']:
                    raise ValueError(f'Phoneme text differs for {key}; review pronunciation inputs first')
                mel = engines['acoustic'].run(None, {'text': np.asarray(entry['ids'], dtype=np.int64), 'spk_id': np.asarray([57], dtype=np.int64)})[0]
                samples = engines['vocoder'].run(None, {'logmel': mel})[0].reshape(-1)
                samples = resample_poly(samples.astype(np.float64), 2, 1)
            else:
                parts = []
                for clause in re.split(r'[.!?]', prompt['en']):
                    if not clause.strip():
                        continue
                    if parts:
                        parts.append(np.zeros(round(.16 * 48000)))
                    audio = engines['en'].generate(clause.strip() + '.', sid=0, speed=1.0)
                    divisor = int(np.gcd(48000, audio.sample_rate))
                    parts.append(resample_poly(np.asarray(audio.samples, dtype=np.float64), 48000 // divisor, audio.sample_rate // divisor))
                samples = np.concatenate(parts)
            active = samples[np.abs(samples) > .002]
            if not np.isfinite(samples).all() or not len(active) or not .25 < len(samples) / 48000 < 30:
                raise ValueError(f'Invalid generated audio: {lang}/{key}')
            samples *= min(10 ** (-26 / 20) / np.sqrt(np.mean(active ** 2)), .55 / np.max(np.abs(samples)))
            path = folder / (key + '.wav')
            sf.write(path, samples, 48000, subtype='PCM_16')
            files.append(dict(file=path.relative_to(output).as_posix(), prompt_id=key,
                              text=prompt['zh_CN' if lang == 'zh-CN' else 'en'],
                              sha256=hashlib.sha256(path.read_bytes()).hexdigest(), duration_s=len(samples) / 48000))
            print('generated:', lang, key, flush=True)
    manifest = dict(models={'zh-CN': 'FastSpeech2 AISHELL-3 SSB0534 + PWGAN', 'en': 'VITS LJS E1'},
                    source_manifest_sha256=hashlib.sha256((ROOT / 'eng/audio/tts-models.json').read_bytes()).hexdigest(),
                    human_approved=False, replaces_approved_assets=False, files=files)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tts-cache-dir', type=Path, default=ROOT / '.cache/tts')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/therapy-selected-resynthesis')
    parser.add_argument('--language', choices=['both', 'zh-CN', 'en'], default='both')
    parser.add_argument('--prompt-id', help='Generate one fixed prompt for a smoke check')
    parser.add_argument('--offline', action='store_true')
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        if output.is_relative_to(ROOT / 'eng') or output.is_relative_to(ROOT / 'src'):
            raise ValueError('Generate audition files outside approved asset/source directories')
        prompts = load_prompts()
        if args.prompt_id:
            prompts = [p for p in prompts if p['prompt_id'] == args.prompt_id]
            if not prompts:
                raise ValueError('Unknown prompt ID')
        cache = prepare(cache=args.tts_cache_dir, offline=args.offline)
        python = environment_python(cache)
        if Path(sys.prefix).resolve() != python.parent.parent.resolve():
            return subprocess.call([str(python), str(Path(__file__).resolve()), *sys.argv[1:], '--offline'])
        synthesize(cache, output, prompts, args.language)
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f'TTS generation failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
