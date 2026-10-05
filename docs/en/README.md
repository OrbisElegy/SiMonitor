# Seele's SiMonitor

<img src="../../src/Monitor.Desktop/Assets/app-icon.png" alt="Temporary SiMonitor icon" width="128" height="128" />

[简体中文](../../README.md) | **English**

Seele's SiMonitor is a teaching simulator for ECG and other patient-monitor waveforms. **It is not a medical device and must not be used for clinical monitoring, diagnosis, or treatment.**

The repository contains a .NET desktop application, a deterministic simulation library, a native audio adapter, build tools, executable specifications, locked dependencies, and license records. 

For a shorter overview, see the [English summary](summary.md). Read the [contribution guide](../../CONTRIBUTING.md) before contributing.

See the [documentation index](../README.md) for the organized technical references.

See the [interface reference](../interfaces/README.md) (Chinese) for current module contracts, data formats, native audio ABI, command-line entry points, and complete C# public declarations.

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
The script handles only generated files inside this repository. It does not uninstall system toolchains or clear the global NuGet cache shared with other projects.

## License and attribution

Project-authored code is licensed under AGPL-3.0-or-later; see [`LICENSE`](../../LICENSE) and the [license scope](../legal/license-scope.md). Third-party code and resources retain their own licenses. Dependency sources and hashes are recorded in [`eng/dependencies.json`](../../eng/dependencies.json); see [`eng/licenses/`](../../eng/licenses) and the [Infirmary source notice](../legal/infirmary-source-notice.md) for related notices.

## To-Do
- i18n
- Final icon design (a temporary icon is in use)
- Individual adjustment of vital signs
- Monitor skin interface
- Manual measurement tool for 12-lead ECG
- Event-driven continuous vital sign changes
- Separate teacher and student interfaces
- Teaching and Exam features
