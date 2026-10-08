# Seele's SiMonitor

<img src="../../src/Monitor.Desktop/Assets/app-icon.png" alt="Temporary SiMonitor icon" width="128" height="128" />

[简体中文](../../README.md) | **English**

Seele's SiMonitor is a teaching simulator for ECG and other patient-monitor waveforms. **It is not a medical device and must not be used for clinical monitoring, diagnosis, or treatment.**

The repository contains a .NET desktop application, a deterministic simulation library, a native audio adapter, build tools, executable specifications, locked dependencies, and license records. 

For a shorter overview, see the [English summary](summary.md). Read the [contribution guide](../../CONTRIBUTING.md) before contributing.

See the [documentation index](../README.md) for the organized technical references.

See the [interface reference](../interfaces/README.md) (Chinese) for current module contracts, data formats, native audio ABI, command-line entry points, and complete C# public declarations.

ECG event integration and capability limits: [enhanced monitoring interface](../interfaces/alarms/ecg-monitoring.md) and [reference notes](../research/physiology/ecg-monitoring.md).

## Screenshots

**Main monitor view**

![Main monitor view showing ECG and other vital-sign waveforms](../assets/screenshots/main-monitor.png)

**Low pulse-rate alarm**

![Monitor view showing a low pulse-rate alarm](../assets/screenshots/low-pulse-rate.png)

**12-lead ECG**

![12-lead ECG snapshot](../assets/screenshots/ecg-12-lead.png)

**Waveform presets**

![Settings view showing waveform presets and preview cards](../assets/screenshots/waveform-presets.png)

## Requirements

- Python 3.11 or newer.
- A stable .NET 10 SDK; see [`global.json`](../../global.json) for the exact version and roll-forward rules.
- CMake 3.20 or newer and a C compiler for the native audio module.
- On Windows, Visual Studio C++ Build Tools and the Windows SDK.

Run all commands below from the repository root. Use `python3` on Linux and macOS; on Windows, replace it with `py -3`. The build scripts restore locked dependencies and verify the downloaded native source against its SHA-256 hash.

## Build and run

Build a development version for the current platform:

```sh
python3 tools/build.py --jobs 32
```

This prepares dependencies, builds the production native audio library, and builds the complete solution in Release configuration. The build output is in `src/Monitor.Desktop/bin/Release/net10.0/`. Run the desktop application with:

```sh
dotnet run --project src/Monitor.Desktop --no-build --configuration Release
```

You can also prepare dependencies first and then build from the local cache:

```sh
python3 tools/fetch_dependencies.py
python3 tools/build.py --offline --jobs 32
```

Create a local product candidate:

```sh
python3 tools/build_release.py --jobs 32
```

Product output is in `artifacts/release/`. Sound uses the native WASAPI audio library by default on Windows; Linux plays directly through the system ALSA library (which also reaches PipeWire and PulseAudio) without the native library; macOS has no sound output yet. Builds or checks on one platform do not replace validation on another platform's device.

Build scripts create the generated files under `artifacts/`, which Git ignores. The project's native audio wrapper source is in `native/sim_audio_native/`. The dependency script downloads a pinned `vendor/miniaudio.h` into that directory and verifies its SHA-256 hash before the build uses it. Run dependency preparation before reviewing that upstream header. See the [native audio guide](../../native/sim_audio_native/README.md) for source locations, output layout, and native diagnostics.

## Verification

After preparing dependencies, run the dependency ledger check, Release build, and executable specifications:

```sh
python3 tools/fetch_dependencies.py
python3 tools/verify_dependency_ledger.py
dotnet build Monitor.slnx --no-restore --configuration Release
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release
```

The `--no-restore` option assumes that the first command has already prepared dependencies. Run additional focused checks according to the changed modules and their documentation. A successful build or specification run does not establish Windows device behavior, audio hardware behavior, or release acceptance.

## Clean local generated files

Remove build caches, test output, and generated files under `artifacts/`:

```sh
python3 tools/clean.py clean
```

Also remove `.cache/` and the `native/sim_audio_native/vendor/miniaudio.h` downloaded by the dependency script:

```sh
python3 tools/clean.py dirclean
```

Both modes accept `--dry-run` to preview what would be deleted, for example `python3 tools/clean.py clean --dry-run`. **Cleaning deletes all of `artifacts/`, including release candidates and any files you placed there yourself.** Move anything you want to keep out of that directory first.

To resynthesize the selected voices after clearing caches:

```sh
python3 tools/fetch_dependencies.py --tts-only
python3 tools/fetch_dependencies.py --tts-only --check
python3 tools/generate_selected_therapy_voices.py --offline
```

`--tts-only` restores FastSpeech2-A (AISHELL-3 SSB0534), its PWGAN vocoder, English E1 (VITS LJS), dictionaries, source notices, and an isolated CPU Python environment under `.cache/tts/`. It requires Python 3.11+ and working pip/venv support; it does not install Torch/CUDA. Download URLs, size limits, and SHA-256 hashes are recorded in [`eng/audio/tts-models.json`](../../eng/audio/tts-models.json); direct Python dependencies are pinned in [`eng/audio/tts-requirements.txt`](../../eng/audio/tts-requirements.txt). Downloads and extracted files are hash-checked, temporary archives are removed, and valid resources are reused.

Use `--tts-cache-dir PATH` on both commands to select another cache. `--check` only verifies; `--offline` fails when resources are missing. Normal builds do not require TTS models. Use `--tts` to prepare both build and TTS dependencies. Historical candidates such as Qwen, Kokoro, and CosyVoice, and all caches/intermediate outputs needed by the old R1–R4 audition workflows, are outside this restore operation.

The generator writes 23 prompts per language to `artifacts/therapy-selected-resynthesis/`; `--language zh-CN` selects only Chinese. Inference is stochastic, so new audio requires listening review and may differ from approved WAVs. The generator preserves approved repository audio, and the cleaning commands preserve version-controlled resources under `eng/audio/voices/`.
The script handles only generated files inside this repository. It does not uninstall system toolchains or clear the global NuGet cache shared with other projects.

## License and attribution

Project-authored code is licensed under AGPL-3.0-or-later; see [`LICENSE`](../../LICENSE) and the [license scope](../legal/license-scope.md). Third-party code and resources retain their own licenses. Dependency sources and hashes are recorded in [`eng/dependencies.json`](../../eng/dependencies.json); see [`eng/licenses/`](../../eng/licenses) and the [Infirmary source notice](../legal/infirmary-source-notice.md) for related notices.

## To-Do
- Final icon design (a temporary icon is in use)
- Individual adjustment of vital signs
- Monitor skin interface
- Manual measurement tool for 12-lead ECG
- Event-driven continuous vital sign changes
- Separate teacher and student interfaces
- Teaching and Exam features
