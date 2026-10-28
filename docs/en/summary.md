# Seele's SiMonitor: English summary

Seele's SiMonitor is a teaching simulator for ECG and other patient-monitor waveforms.
It is **not a medical device** and must not be used for clinical monitoring,
diagnosis, or treatment. The repository contains a .NET desktop application,
deterministic simulation code, a native audio adapter, build tools, executable
specifications, and dependency and license records.

Product windows carry an experimental low-strength screen marker; screenshots and video frames can be
checked with the [offline detector](../interfaces/desktop-tools.md#屏幕模拟来源水印).
Detection is a provenance clue, not authentication, and absence does not establish authenticity.
PDF and paper output retain the explicit teaching declaration.

## Build and verification

Use Python 3.11 or newer, a .NET 10 SDK, CMake 3.20 or newer, and a C
compiler. Windows audio builds also need Visual Studio C++ Build Tools and a
Windows SDK. From the repository root, `python3 tools/build.py --jobs 32`
prepares locked dependencies, builds the native audio library, and builds the
Release solution. On Windows, use either `python3` or `py -3`.
Run the desktop application with
`dotnet run --project src/Monitor.Desktop --no-build --configuration Release`.
For a local release candidate, run `python3 tools/build_release.py --jobs 32`;
its output is under `artifacts/release/`.

After dependency preparation, the main verification commands are
`python3 tools/verify_dependency_ledger.py`,
`dotnet build Monitor.slnx --no-restore --configuration Release`, and
`dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release`.
See the [full English README](README.md), [Chinese README](../../README.md),
and [contribution guide](../../CONTRIBUTING.md) for the complete workflow.
A successful build or specification run does not
establish Windows device behavior or release acceptance.

## Technical documentation

- [CPU dispatch and computation scope](../research/performance/cpu-kernels.md) explains feature-based SIMD selection, compile-time x64/ARM64 separation, exact batched waveform evaluation, and the production performance harness. Processor models do not select special tuning paths.
- [Patient timeline](../interfaces/desktop-tools.md#患者时间线) describes initial patient states, session-relative events, continuous vital targets, waveform template changes, switching between direct SpO₂ targets and modeled ventilation, treatment permissions, treatment-relative branches that preserve patient history, complete timeline file import/export, and disabling execution until a fresh restart.
- [Interface reference](../interfaces/README.md) (Chinese) covers current module
  contracts, data formats, native audio ABI, command-line entry points, and
  complete C# public declarations, with links to their implementation.
- [Vascular pressure runoff](../research/physiology/vascular-pressure-runoff-research.md) describes
  the implemented deterministic RC reservoir for arterial and pulmonary-artery
  pressure, its empirical pulse-shape layer, evidence, acceptance conditions,
  and remaining physiology and measurement work. Stopping mechanical ejection
  allows residual pressure to decay; renewed ejection starts from that pressure.
- [Long-RR Pleth runoff](../research/physiology/pleth-runoff-research.md) explains a bounded pulse tail
  that avoids premature zero output during long RR intervals. It does not model
  a complete optical sensor or determine numeric SpO₂.
- [Pressure, PLETH, capnogram and RESP templates](../research/physiology/non-ecg-waveform-templates.md)
  documents waveform evidence, corrected terminology and the distinction between
  gas measurements, chest movement and pressure/optical artifacts.
- [SVT perfusion](../research/physiology/svt-perfusion.md) documents the author-selected filling
  saturation factor for the fixed 200 bpm SVT presets. It corrects excessive
  pressure from an unchanged per-beat input; it is not a patient-calibrated model.
- [Cheyne–Stokes breathing and exhaled CO₂](../research/physiology/cheyne-stokes-co2-coupling.md)
  documents one-way coupling of a prescribed breathing-depth sequence to an
  illustrative CO₂ reservoir. Its constants and phase behavior are author
  choices, not clinical calibration.
- [Infirmary Integrated source notice](../legal/infirmary-source-notice.md) records the
  pinned upstream commit, Apache-2.0 attribution, source and license hashes,
  and exact local waveform adaptations for ABP, CO₂, PA, and CVP components.
- [License scope](../legal/license-scope.md) distinguishes project-authored code from
  third-party material and describes distribution obligations and the scope
  of bundled resources and therapy recordings.

The reviewed research reports identify GPT-6-Astra as their research author.
Jason Zou, who has a medical education background, reviewed readability and
medical terminology. This review does not establish that models implemented
from those reports have undergone peer review or experimental validation
against real physiology.

Project-authored code is licensed under **AGPL-3.0-or-later**. Third-party
material retains its own terms and attribution. Consult [LICENSE](../../LICENSE),
the [license scope](../legal/license-scope.md), [dependency ledger](../../eng/dependencies.json),
and [third-party licenses](../../eng/licenses) before redistribution.
The simulation models and engineering checks do not constitute clinical validation.

Therapy audio uses selected Mandarin F1 and English E1 recordings. The application
embeds fixed WAV files and includes their source notices and licenses under
`therapy-licenses/`; voice models are not bundled. Embedding the recordings does
not relicense them under the project's AGPL license.

Pacing includes fifteen fixed examples covering chamber/site choices, implant routes and output/capture/sensing failures. The independent defibrillation current source supports monophasic and biphasic waveforms. The desktop applies confirmed shocks through an adapter that adds ECG artifacts and schedules eligible rhythm conversions. See the [research and limitations](../research/physiology/pacing-defibrillation.md).

Per-template shock response, independent monophasic/biphasic thresholds and skin integration are documented in the [electrical conversion contract](../interfaces/electrical-conversion.md).

The default Generic skin uses four waveform rows (ECG, PLETH, RESP, CO₂), a white NIBP tile and two custom parameter slots. Therapy and toolbar buttons use live host commands. The D3 inspired enclosure includes a mode selector, three context softkeys and hardware controls, with a fixed three-row screen and bundled button materials. The in-process desktop composition port is described in [desktop interfaces](../interfaces/desktop-tools.md#皮肤装配接口与单文件皮肤).

D3 enclosure behavior and model limits are documented in the [desktop interfaces](../interfaces/desktop-tools.md#通用整机面板).
