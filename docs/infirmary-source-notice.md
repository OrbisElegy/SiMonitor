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
damping, flush, transducer faults and native client display remain unimplemented.
