# Long-RR Pleth runoff investigation

The reported SpO₂ trace is the relative photoplethysmographic pulse (Pleth),
not the numeric oxygen saturation. The previous desktop source used a finite
512 ms pulse (shorter for some modes), returning exactly zero at its endpoint.
Consequently a long RR interval contained an artificially early flat segment.
This is a source limitation, not a sweep or vertical-scale fault.

## Evidence and limits

- Allen and Murray, *Effects of filtering on multi-site photoplethysmography
  pulse waveform characteristics* (Computers in Cardiology 2004),
  https://eprints.ncl.ac.uk/202434 . Their measured PPG contains a pulsatile
  component on a slowly varying baseline; high-pass filtering changes shape.
- Williamson et al., *The Hybrid Excess and Decay (HED) model* (2022),
  https://doi.org/10.12688/wellcomeopenres.17855.1 . The published version1
  describes component waves plus decay; its displayed review status was
  awaiting peer review. This provides a modelling precedent, not validation
  of our parameters or of prolonged absent ejection.

These support representing a diastolic tail and distinguishing optical display
from absolute arterial pressure. They do not establish that all monitors must
continue visibly descending throughout every pause, nor that SpO₂ must fall
with this tail. With no new effective ejection, a decaying pulsatile component
can approach baseline; quantization eventually makes it flat. DC tissue/blood
volume, filters, gain control, noise and saturation estimation remain outside
this teaching source. We do not copy the HED model or infer its fitted values.

## Implemented teaching model

`PlethSmoothRunoff@1` keeps the rounded peak and descending shoulder of
`PlethPulseIllustrationDraft@2`. At plain LUT index88/128 (352ms after arrival
for nominal512ms duration), amplitude0.44 joins
`A * (1 + u/tau) * exp(-u/tau)`, where u is time since join and tau defaults400ms.
The optional notched source joins at72/128. The zero initial tail slope matches
the smooth shoulder endpoint. Tau100ms..2s and the default400ms are explicit
author choices, not calibrated optical/vascular constants. The finite LUT is
preserved for existing callers; the physiology demo selects the new source.

Each effective mechanical event contributes separately. Prior tails remain
when a new event arrives or an event is skipped. Existing premature-beat gains
and3/4 duration scaling apply to original beat ordinals, with unchanged transit
delay and a separate tail time constant. Zero-gain ejection contributes nothing
without erasing earlier pulses. This model affects only the Pleth source; ECG,
blood-pressure sources and SpO₂ numeric logic remain independent.

## Bounded deterministic reconstruction

There is no online regression fit, accumulated numerical integration, or scan
from the simulation epoch. Reconstruction directly indexes a fixed history:
longest join time +64tau. Default support25.952s reserves at most33 events at
RR800ms. Construction rejects more than4096 possible events or a conservative
sum exceeding int16; accepted values are never clipped to hide overflow.

Decay uses a1us trapezoidal Q62 factor,27 repeated-square powers and fixed-point
fractional interpolation. At64tau decay is exactly zero in this representation.
Runtime/Fork share the immutable source; external restore revalidates plans and
pending acquisition samples. Work is bounded by configured history and minimum
RR, independent of the elapsed simulation time. Acquisition remains at125Hz
with200ms blocks and a2s processing delay.

## Verification

Four specifications cover legacy peak/shoulder parity, independent analytic
tail comparison,2:1/4:1 long RR, no-mechanics and zero-gain PVC, overlapping sums,
checkpoint/wire identity, tampered pending samples, rejection/cancellation,
atomic budget failure and late queries. Existing premature-mode native checks
compare all Pleth wire samples; AF/grouped block/stride checks now require
continued decay during missing beats rather than immediate zero.

An initial Linux run measured2000 evaluations at40s and3600s as118.3ms and119.0ms;
near the maximum timestamp266.2ms. These are observations, not timing assertions
or Windows benchmarks. All three use the same33-event bound and produce the
same steady-state phase values. The native suite also retains its wall-clock
catch-up tests. Manual Windows visual validation is not yet complete.

Other ECG, respiratory, perfusion and acquisition-artifact coverage remains
incomplete. This fix is not a complete optical
sensor model or a physiological validation of prolonged circulatory arrest.
