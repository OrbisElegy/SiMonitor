#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Generate E1/F1 swapped chest-pad placement prompt and correction audition."""
import hashlib
import json
import shutil
import zipfile

from generate_therapy_selected_flow import ROOT, Timeline, read, save
from selected_therapy_voice import selected_voice_path


def main():
    config_path = ROOT / 'eng/audio/therapy-pad-placement.json'
    config = json.loads(config_path.read_text(encoding='utf-8'))
    out = ROOT / 'artifacts/therapy-pad-placement'
    out.mkdir(parents=True, exist_ok=True)
    chinese_path = selected_voice_path(config['prompt_id'], config['zh_CN'])
    english_path = ROOT / 'eng/audio/voices/en/E1' / (config['prompt_id'] + '.wav')
    english_entry = json.loads((english_path.parent / 'pad-placement.json').read_text(encoding='utf-8'))
    assert english_entry['text'] == config['en']
    assert hashlib.sha256(english_path.read_bytes()).hexdigest() == english_entry['sha256']
    chinese, english = read(chinese_path), read(english_path)
    hashes = {language: hashlib.sha256(path.read_bytes()).hexdigest()
              for language, path in [('zh-CN', chinese_path), ('en', english_path)]}
    metadata = dict(requirement=config, source_hashes=hashes, flows={})
    for language, variant, samples in [('zh-CN', 'F1', chinese), ('en', 'E1', english)]:
        key = config['prompt_id']
        shutil.copyfile(chinese_path if language == 'zh-CN' else english_path,
                        out / f'{key}-{language}.wav')
        timeline = Timeline()
        source = (ROOT / 'eng/audio/voices/zh-CN/F1' if language == 'zh-CN'
                  else ROOT / f'artifacts/therapy-auditions-r4/voice/{language}/{variant}')
        timeline.add(read(source / 'AED_APPLY_PADS.wav'), 'AED_APPLY_PADS')
        timeline.pause(1, '模拟贴片：位置互换')
        timeline.add(samples, key)
        timeline.pause(3, '试听设定：纠正位置 3 秒；此时无充电、就绪或 CPR 声')
        timeline.add(read(source / 'AED_ANALYZING.wav'), '位置纠正、治疗路径有效后重新分析')
        save(out / f'placement-correction-{language}.wav', timeline.audio())
        metadata['flows'][language] = timeline.events
    (out / 'manifest.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    shutil.copyfile(ROOT / 'eng/audio/voices/zh-CN/F1/NOTICE.txt', out / 'F1-NOTICE.txt')
    shutil.copyfile(ROOT / 'artifacts/therapy-auditions-r2/English-LICENSE.txt', out / 'English-LICENSE.txt')
    shutil.copyfile(ROOT / 'eng/audio/voices/zh-CN/F1/PaddleSpeech-LICENSE.txt', out / 'F1-PaddleSpeech-LICENSE.txt')
    (out / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>电极片位置互换提示</title><h1>电极片位置互换 · E1/F1</h1><p>触发条件为模拟器明确判定两片电极贴放位置互换，不表示实物 AED 必然可检测该状态。该提示不替代接触不良提示。</p><p>中文：电极片位置错误，请按照图示重新贴放。<br>English: Incorrect pad placement. Reposition the pads as shown.</p><h2>中文单句</h2><audio controls src="AED_PAD_POSITION-zh-CN.wav"></audio><h2>英文单句</h2><audio controls src="AED_PAD_POSITION-en.wav"></audio><h2>中文纠正流程</h2><audio controls src="placement-correction-zh-CN.wav"></audio><h2>英文纠正流程</h2><audio controls src="placement-correction-en.wav"></audio><p>贴片提示 → 位置错误 → 3 秒纠正等待 → 重新分析。等待时长仅为试听编排，未接入运行时。</p><script>document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})))</script></html>''', encoding='utf-8')
    with zipfile.ZipFile(out / 'pad-placement.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(out.iterdir()):
            if path.is_file() and path.suffix != '.zip':
                archive.write(path, path.name)


if __name__ == '__main__':
    main()
