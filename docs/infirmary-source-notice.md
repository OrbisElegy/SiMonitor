# Infirmary Integrated source attribution

The arterial seed is adapted from Infirmary Integrated by Ibi Keller (Tanjera),
fixed commit `0e0eccd2f6e0705b77641a024c20a35cbf9b90e1`, licensed under Apache-2.0.
The project attribution in Waveform.Draw.cs reads “By Ibi Keller (Tanjera),
(c) 2017-2023”; the plots file has no separate copyright header.

- [Original ABP_Default vertices](https://github.com/tanjera/infirmary-integrated/blob/0e0eccd2f6e0705b77641a024c20a35cbf9b90e1/II%20Library/Classes/Waveform.Dictionary.Plots.cs)
- [Original drawing/reference usage](https://github.com/tanjera/infirmary-integrated/blob/0e0eccd2f6e0705b77641a024c20a35cbf9b90e1/II%20Library/Classes/Waveform.Draw.cs)
- [Original license](https://github.com/tanjera/infirmary-integrated/blob/0e0eccd2f6e0705b77641a024c20a35cbf9b90e1/License.md)

The full license text (trailing whitespace normalized) is retained in `eng/licenses/infirmary-integrated-LICENSE.md`.
The complete recursive tree for this commit contained no NOTICE file.
Adaptations are limited to the waveform vertices documented below; upstream
drawing code, timers and other assets are not included.

`eng/physiology/infirmary-arterial-pulse.json` preserves all 76 original ABP
vertices as decimal strings, source-file SHA-256, canonical vertex SHA-256,
license SHA-256 and explicit local modifications. Regeneration checks vertex and
license hashes, then emits per-table little-endian Q32 SHA-256. Files required to
redistribute this adaptation include the license and this attribution notice.

Local changes: normalize original peak 0.8 to 1; replace vertices 35/36/37 with
0.34/0.32/0.36 to create a local notch and recovery absent in the original
monotonic decline; linearly resample exact rational values into 256 Q32 points.
The final zero endpoint closes the periodic LUT without an invented jump.
The upstream 10 ms drawing grid and 360 ms systole ratio are not acquisition timing;
this implementation uses explicit pulse duration and native 125 Hz pressure sampling.
Upstream random beat modifiers and timers are not imported.

`ArterialPulsePlan` adds explicit mechanical transit, baseline and pulse height.
Its illustrative source is not a qualified arterial-site preset or a model of
catheter dynamics. Baseline persistence without mechanical events is a caller
configuration, not a simulated circulatory-arrest pressure decay. SYS/DIA/MAP,
damping, flush and transducer faults remain unimplemented.

Source inspection uses the public links pinned to the commit above.
Regeneration uses the committed manifests and license text; an upstream
checkout is not a build dependency.
The native physiology demo displays this pressure source.

## CO2 adaptation

The same pinned plots file also supplies 224 `ETCO2_Default` vertices, recorded
in `eng/physiology/infirmary-capnogram.json` with source/vertex/license hashes.
The same Apache-2.0 attribution applies.
No additional upstream code or assets were copied. Regeneration requires only
committed manifests and license text, not the local clone or network.

Changes: prepend zero and a dead-space segment; split original indices 0..14 for
rise (with zero prepended), 14..212 for plateau, 212..223 for fall; normalize by 0.7
and exactly resample to 512 Q32 entries. Table phase anchors 0/32/96/480/512 are
mapped to explicit event-relative timing. The final endpoint is zero. The original
10 ms drawing interval is not treated as a source sample rate or physiological
time constant. Peak aligns with next inspiration, and the fall continues into
that inspiration. The plateau's shape still comes from the upstream seed; it is
not a validated capnogram preset or an independently adjustable slope model.

## Pulmonary artery adaptation

The same pinned source file supplies 76 PA_Default vertices, retained in
`eng/physiology/infirmary-pulmonary-artery.json` with source/vertex/license hashes
under the same Apache-2.0 attribution. The manifest records the pinned source-file hash.
The independent PA seed is not an ABP-derived shape. No drawing timers, random
modifiers or fixed intrathoracic amplitude multipliers were copied.

Local changes: normalize by original peak 0.87, retain original indices 0..45
(including notch/recovery), replace indices 46..75 with an exact linear decay
from index 45 = 0.09 to zero, then resample 256 Q32 points. This removes unqualified
oscillation and below-baseline ringing from the source illustration; measurement
system ringing will need a separate artifact model. Original 10 ms drawing spacing
and 220 ms systole metadata are not imported as physiological timing constants.
The manifest records all changes and the generator verifies hashes. Native PA
pixels do not constitute a validated RV/PA model or measured PAP values.

## CVP component adaptation

`eng/physiology/infirmary-cvp-components.json` retains both 95-point upstream
CVP_Atrioventricular and CVP_Ventricular seeds with canonical combined hash,
pinned source-file hash and the existing Apache-2.0 license reference.
The source-file hash identifies the same pinned upstream plots file.

Local A extraction uses atrioventricular indices 0..43, removes the straight
endpoint baseline, clamps negative detrending residue to zero, normalizes the
remaining maximum and resamples 128 Q32 points. V extraction uses ventricular
indices 36..86, normalizes its 0.81 peak and resamples 128 points. These are adapted
shape portions, not a claim that the original arrays provided independently
validated a/v mechanisms. C, X/Y descent and respiratory unit envelopes are new
project-authored smoothstep shapes recorded separately in the manifest. Their
phase, magnitude and event binding are explicit source parameters. Upstream
whole-complex selection and intrathoracic amplitude multipliers are not copied.
No mean-CVP estimator, full valve physiology or clinical preset is implied.


Optional plateau-start pressure in CapnogramPlan additionally remaps the adapted
ETCO2_Default rise and C-to-D amplitudes at runtime using exact rational fixed-point
rounding. The original generated seed, timing landmarks and inspiratory fall are
retained; omitted plateau configuration reproduces the prior seed scaling.


Optional CapnogramPlan dispersion splits the adapted CO2 table into symmetric
quarter/half/quarter delayed paths, with fixed-point residue retained in the
middle path. The project-authored response is a runtime modification; the
imported seed and attribution are unchanged, and disabling it preserves prior
output. It is not an upstream or manufacturer-specified response kernel.
