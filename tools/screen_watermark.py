#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Detect SiMonitor's public screen marker in a screenshot or extracted video frame.

Requires NumPy and Pillow. A positive result is a marker match, not proof of
provenance; a negative result does not establish a real acquisition.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter, ImageOps


PROFILE_PATH = Path(__file__).resolve().parent.parent / 'src/Monitor.Desktop/Assets/Watermarks/screen-v1.json'
MINIMUM_SIDE = 256
MAXIMUM_ANALYSIS_SIDE = 1600
MINIMUM_SCALE = 0.5
MAXIMUM_SCALE = 4.0
MINIMUM_SCORE = 4.5
MINIMUM_MATCHES = 9
MINIMUM_PHASE_CONSISTENCY = 0.88
MINIMUM_CARRIER_AMPLITUDE = 0.005
INTERPRETATION = ('A match indicates the public synthetic-screen marker, not authenticated provenance. '
                  'No match does not establish a real acquisition.')


def detect(image):
    """Blind magnitude detection: translation/cropping changes phase, not carrier frequency.

    Scale is screenshot pixels per original UI DIP, including display DPI and
    any subsequent uniform resize. Scores are spectral ratios, not probabilities.
    Thresholds are engineering heuristics, not a calibrated false-positive rate.
    """
    profile = json.loads(PROFILE_PATH.read_text(encoding='utf-8'))
    image = ImageOps.exif_transpose(image).convert('RGB')
    result = {'marker': profile['marker'], 'status': 'not_detected',
              'spectral_score': 0.0, 'matched_carriers': 0, 'total_carriers': len(profile['carriers']),
              'scale': None, 'phase_consistency': 0.0, 'aligned_carriers': 0, 'interpretation': INTERPRETATION}
    if min(image.size) < MINIMUM_SIDE:
        return result | {'status': 'insufficient_size'}
    reduction = min(1.0, MAXIMUM_ANALYSIS_SIDE / max(image.size))
    if reduction < 1:
        image = image.resize(tuple(round(side * reduction) for side in image.size), Image.Resampling.LANCZOS)
    if min(image.size) < MINIMUM_SIDE:
        return result | {'status': 'insufficient_size'}
    gray = image.convert('L')
    # Limit strong UI edges before the FFT so a faint repeated signal is not
    # overwhelmed by the ECG grid, text and trace. Do not need the original image.
    residual = np.asarray(gray, dtype=np.float64) - np.asarray(gray.filter(ImageFilter.GaussianBlur(4)), dtype=np.float64)
    residual = np.clip(residual, -3, 3)
    height, width = residual.shape
    residual -= residual.mean()
    windowed = residual * np.hanning(height)[:, None] * np.hanning(width)[None, :]
    spectrum = np.abs(np.fft.rfft2(windowed))
    # A fine scale grid keeps the strongest carrier within the 3x3 peak window.
    scales = np.linspace(MINIMUM_SCALE, MAXIMUM_SCALE, 1401)
    frequencies = np.asarray(profile['carriers'], dtype=float)
    centers = np.rint(frequencies[None, :, :] * [width, height] /
                     (profile['period'] * reduction * scales[:, None, None])).astype(int)
    offsets_y, offsets_x = np.mgrid[-7:8, -7:8]
    noise_mask = (np.abs(offsets_y) > 3) | (np.abs(offsets_x) > 3)
    peak_mask = (np.abs(offsets_y) <= 1) & (np.abs(offsets_x) <= 1)
    valid = ((centers[:, :, 0] >= 7) & (centers[:, :, 0] < spectrum.shape[1] - 7) &
             (np.abs(centers[:, :, 1]) >= 7) & (np.abs(centers[:, :, 1]) < height // 2 - 7)).all(axis=1)
    centers = centers[valid]
    scales = scales[valid]
    if not len(scales):
        return result | {'status': 'insufficient_size'}
    centers[:, :, 1] %= height
    local = spectrum[(centers[:, :, 1, None, None] + offsets_y) % height,
                     centers[:, :, 0, None, None] + offsets_x]
    peaks = local[:, :, peak_mask].max(axis=2)
    noise = np.median(local[:, :, noise_mask], axis=2)
    ratios = peaks / np.maximum(noise, 1e-6)
    scores = np.median(ratios, axis=1)
    matches = (ratios >= 4).sum(axis=1)
    # This is candidate selection only; exact-frequency alignment and the phase
    # code make the decision. DPI interpolation can weaken several coarse peaks.
    window_weight = np.hanning(height).sum() * np.hanning(width).sum()
    significant = (2 * peaks / window_weight >= MINIMUM_CARRIER_AMPLITUDE).sum(axis=1)
    eligible = (scores >= MINIMUM_SCORE) & (matches >= 7) & (significant >= MINIMUM_MATCHES)
    best = int(np.argmax(scores))
    consistency = 0.0
    aligned = 0
    found = False
    # A paper grid can produce very strong peaks at many carrier positions.
    # Verify the phase code too, allowing one common unknown translation. A
    # magnitude-only detector incorrectly accepts some unmarked regular grids.
    candidates = np.flatnonzero(eligible)
    candidates = candidates[np.argsort(scores[candidates])[::-1]][:24]
    for candidate in candidates:
        phase, aligned_count = phase_consistency(windowed, profile, scales[candidate] * reduction,
                                                ratios[candidate], peaks[candidate])
        if phase > consistency:
            consistency, aligned, best = phase, aligned_count, int(candidate)
        if phase >= MINIMUM_PHASE_CONSISTENCY and aligned_count >= MINIMUM_MATCHES:
            consistency, aligned, best = phase, aligned_count, int(candidate)
            found = True
            break
    best_scale = float(scales[best])
    if not found and consistency >= MINIMUM_PHASE_CONSISTENCY:
        # Marginal compressed images may clear the broad-peak threshold only
        # just beside the true scale. Refine there before rejecting alignment.
        for offset in (-.0025, .0025, -.005, .005, -.0075, .0075):
            refined = best_scale + offset
            if not MINIMUM_SCALE <= refined <= MAXIMUM_SCALE:
                continue
            phase, aligned_count = phase_consistency(windowed, profile, refined * reduction,
                                                    ratios[best], peaks[best])
            if phase >= MINIMUM_PHASE_CONSISTENCY and aligned_count >= MINIMUM_MATCHES:
                consistency, aligned, best_scale, found = phase, aligned_count, refined, True
                break
    return result | {'status': 'marker_detected' if found else 'not_detected',
                     'spectral_score': round(float(scores[best]), 3),
                     'matched_carriers': int(matches[best]), 'scale': round(best_scale, 4),
                     'phase_consistency': round(consistency, 4), 'aligned_carriers': aligned}


def phase_consistency(windowed, profile, scale, ratios, peaks):
    height, width = windowed.shape
    period = profile['period']
    frequencies = np.asarray(profile['carriers'])
    horizontal = np.exp(-2j * np.pi * frequencies[:, 0, None] * np.arange(width) / (period * scale))
    vertical = np.exp(-2j * np.pi * frequencies[:, 1, None] * np.arange(height) / (period * scale))
    coefficients = np.einsum('cy,yx,cx->c', vertical, windowed, horizontal, optimize=True)
    expected = 2 * np.pi * np.arange(len(frequencies)) ** 2 * profile['phaseStep']
    weights = np.minimum(ratios, 10)
    phases = np.exp(1j * (np.angle(coefficients) - expected)) * weights
    correlation_spectrum = np.zeros((period, period), dtype=complex)
    for (fx, fy), value in zip(frequencies, phases):
        correlation_spectrum[fy % period, fx % period] = value
        correlation_spectrum[-fy % period, -fx % period] = value.conjugate()
    correlation = np.fft.ifft2(correlation_spectrum).real
    # The broad FFT peak search tolerates fractional bins. Require the actual
    # frequencies at ONE scale to contain the energy too: otherwise nearby
    # unrelated grid harmonics can independently occupy those search windows.
    amplitude = 2 * np.abs(coefficients) / (np.hanning(height).sum() * np.hanning(width).sum())
    # Near-zero leakage divided by an even smaller noise floor is not evidence.
    aligned = int(((np.abs(coefficients) >= .65 * peaks) & (amplitude >= MINIMUM_CARRIER_AMPLITUDE)).sum())
    return float(correlation.max() * period * period / (2 * weights.sum())), aligned


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', type=Path, help='Screenshot or extracted video frame; never uploaded')
    args = parser.parse_args()
    try:
        with Image.open(args.image) as image:
            result = detect(image)
    except (OSError, ValueError, Image.DecompressionBombError) as error:
        parser.exit(2, f'Cannot inspect image: {error}\n')
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result['status'] == 'marker_detected' else 1


if __name__ == '__main__':
    raise SystemExit(main())
