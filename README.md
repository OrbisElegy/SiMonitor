# Seele's SiMonitor

Seele's SiMonitor is a teaching simulator for ECG and other monitor waveforms.
It is not intended for clinical monitoring, diagnosis, or treatment.

The repository contains the .NET desktop application, deterministic simulation
libraries, native audio adapter, build tools, executable specifications, locked
dependencies, and license materials. A product build is a local candidate and
does not imply release acceptance.

## Requirements

- Python 3.11 or newer
- Stable .NET 10 SDK (see `global.json`)
- CMake 3.20 or newer and a C compiler for native audio
- On Windows, Visual Studio C++ Build Tools and the Windows SDK

Run all commands from the repository root. Use `python3` below on Linux and
macOS, or `py -3` on Windows. The scripts restore pinned dependencies and
verify downloaded native source hashes.

## Build

```sh
python3 tools/build.py --jobs 32
```

To prepare dependencies separately and build from caches:

```sh
python3 tools/fetch_dependencies.py
python3 tools/build.py --offline --jobs 32
```

For a local product build:

```sh
python3 tools/build_release.py --jobs 32
```

The product output is under `artifacts/release/`. The regular development
build is under `src/Monitor.Desktop/bin/Release/net10.0/`. The native audio
backend currently targets Windows WASAPI.

`artifacts/` contains generated output and is intentionally ignored by Git;
the build scripts create it. Native wrapper sources are tracked under
`native/sim_audio_native/`. The dependency script fetches the pinned
`vendor/miniaudio.h` into that directory and verifies its SHA-256 before
building. Fetch dependencies before reviewing that upstream header.
See the [native audio guide](native/sim_audio_native/README.md) for source
locations, output layouts, and native diagnostics.

## Verify

```sh
python3 tools/fetch_dependencies.py
python3 tools/verify_dependency_ledger.py
dotnet build Monitor.slnx --no-restore --configuration Release
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release
```

## License and attribution

Original project code is licensed under AGPL-3.0-or-later; see `LICENSE`
and `docs/license-scope.md`. Third-party code and assets retain their own
licenses. Dependency origins and hashes are in `eng/dependencies.json`, with
notices in `eng/licenses/` and `docs/infirmary-source-notice.md`.
