# Seele's SiMonitor: English summary

Seele's SiMonitor is a teaching simulator for ECG and other patient-monitor waveforms.
It is **not a medical device** and must not be used for clinical monitoring,
diagnosis, or treatment. The repository contains a .NET desktop application,
deterministic simulation code, a native audio adapter, build tools, executable
specifications, and dependency and license records.

## Build and verification

Use Python 3.11 or newer, a stable .NET 10 SDK, CMake 3.20 or newer, and a C
compiler. Windows audio builds also need Visual Studio C++ Build Tools and a
Windows SDK. From the repository root, `python3 tools/build.py --jobs 32`
prepares locked dependencies, builds the native audio library, and builds the
Release solution. On Windows, replace `python3` with `py -3`.
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
  third-party material and describes distribution obligations and outstanding
  release-package review.

Project-authored code is licensed under **AGPL-3.0-or-later**. Third-party
material retains its own terms and attribution. Consult [LICENSE](../../LICENSE),
the [license scope](../legal/license-scope.md), [dependency ledger](../../eng/dependencies.json),
and [third-party licenses](../../eng/licenses) before redistribution.
The simulation models and engineering checks do not constitute clinical validation.
