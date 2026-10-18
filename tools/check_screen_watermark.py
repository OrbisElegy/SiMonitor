#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check real Avalonia watermark fixtures, transformations, negatives and H.264 frames.

First build Release and run the desktop checks. Requires NumPy, Pillow and ffmpeg.
Outputs are written only under artifacts/watermark, which is ignored by Git.
"""
import argparse
import io
from pathlib import Path
import subprocess

import numpy as np
from PIL import Image, ImageDraw

from screen_watermark import detect


FIXTURES = ('generic', 'bedside', 'd3', 'dpi-150', 'dpi-200', 'ecg', 'ecg-print', 'live-print')


def jpeg(image, quality):
    buffer = io.BytesIO()
    image.save(buffer, format='JPEG', quality=quality)
    with Image.open(buffer) as decoded:
        return decoded.convert('RGB')


def variants(image):
    yield 'png', image
    for quality in (90, 75):
        yield f'jpeg-{quality}', jpeg(image, quality)
    for scale in (.5, .75, 1.25):
        resized = image.resize(tuple(round(side * scale) for side in image.size), Image.Resampling.LANCZOS)
        yield f'resize-{scale}', resized
    crop = image.crop((123, 87, image.width - 51, image.height - 91))
    yield 'crop', crop
    yield 'crop-jpeg-75', jpeg(crop, 75)
    resized = crop.resize(tuple(round(side * .75) for side in crop.size), Image.Resampling.LANCZOS)
    yield 'crop-resize-jpeg-75', jpeg(resized, 75)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixtures', type=Path, default=Path(__file__).resolve().parent.parent / 'artifacts/watermark')
    args = parser.parse_args()
    failures = []
    checked = 0

    def check(label, image, expected):
        nonlocal checked
        result = detect(image)
        checked += 1
        if result['status'] != expected:
            failures.append(label)
            print(f'FAIL {label}: {result}', flush=True)

    required = [args.fixtures / f'{name}-{kind}.png' for name in FIXTURES for kind in ('marked', 'clean')]
    required.extend(args.fixtures / f'video-{frame:02}.png' for frame in range(12))
    for path in required:
        if not path.is_file():
            parser.error(f'Missing {path}; run current desktop checks first')
    for name in FIXTURES:
        for kind, expected in (('marked', 'marker_detected'), ('clean', 'not_detected')):
            with Image.open(args.fixtures / f'{name}-{kind}.png') as image:
                for transform, transformed in variants(image.convert('RGB')):
                    check(f'{name}-{kind}/{transform}', transformed, expected)
        print(f'checked {name}', flush=True)

    # Repetitive paper grids, flat fields and random imagery exercise false matches.
    for shade in (0, 16, 128, 255):
        check(f'flat-{shade}', Image.new('RGB', (800, 600), (shade,) * 3), 'not_detected')
    for spacing in range(4, 81):
        image = Image.new('RGB', (1120, 800), 'white')
        draw = ImageDraw.Draw(image)
        for x in range(0, image.width, spacing):
            draw.line((x, 0, x, image.height), fill='red')
        for y in range(0, image.height, spacing):
            draw.line((0, y, image.width, y), fill='red')
        check(f'grid-{spacing}', image, 'not_detected')
    random = np.random.default_rng(20261018)
    for index in range(8):
        image = Image.fromarray(random.integers(0, 256, (600, 800, 3), dtype=np.uint8))
        check(f'noise-{index}', image, 'not_detected')
    check('small', Image.new('RGB', (128, 128)), 'insufficient_size')
    check('narrow', Image.new('RGB', (4096, 256)), 'insufficient_size')

    # Encode changing, actually rendered monitor frames and inspect decoded frames.
    video = args.fixtures / 'recording.mp4'
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', '10', '-i',
                    str(args.fixtures / 'video-%02d.png'), '-frames:v', '12',
                    '-c:v', 'libx264', '-crf', '23', '-pix_fmt', 'yuv420p', str(video)], check=True)
    decoded = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(video), '-f', 'rawvideo',
                              '-pix_fmt', 'rgb24', '-'], check=True, stdout=subprocess.PIPE).stdout
    frame_size = 1120 * 800 * 3
    if len(decoded) != 12 * frame_size:
        raise RuntimeError('Unexpected H.264 decoded frame count or dimensions')
    for frame in (0, 5, 11):
        image = Image.frombytes('RGB', (1120, 800), decoded[frame * frame_size:(frame + 1) * frame_size])
        check(f'h264-frame-{frame}', image, 'marker_detected')
    print(f'{checked} checks; {len(failures)} failures')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
