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
