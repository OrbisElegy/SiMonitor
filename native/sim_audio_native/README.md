# Native audio ABI1

Project wrapper: AGPL-3.0-or-later. Downloaded miniaudio 0.11.23 is unchanged,
commit f40cf03f80cdb7e741d43e53b7e706e8c1394bcf, selected license MIT-0.
Original dual-license notice is preserved in vendor/LICENSE.miniaudio;
source hashes and origin are in eng/dependencies.json.

## Sources and build outputs

Run all commands from the repository root. Use `python3` on Linux and macOS,
or `py -3` on Windows. Building requires Python 3.11+, CMake 3.20+ and a C
compiler (Windows: Visual Studio C++ Build Tools and the Windows SDK).
Managed commands also require the .NET SDK specified by `global.json`.

| Repository-relative path | Purpose | Availability |
| --- | --- | --- |
| `native/sim_audio_native/sim_audio.c` and `sim_audio.h` | Project wrapper source and public ABI | Tracked in Git |
| `native/sim_audio_native/vendor/miniaudio.h` | Unmodified upstream implementation | Fetched and SHA-256-verified by the dependency script; ignored by Git |
| `native/sim_audio_native/vendor/LICENSE.miniaudio` | Upstream license notice | Tracked in Git |
| `artifacts/native-audio/` | Production native build output and build evidence | Generated locally; ignored by Git |
| `artifacts/native-audio-test/` | Null-backend test build output | Generated locally; ignored by Git |

A fresh clone has no native binaries or downloaded header. Prepare the header
and build both production and test libraries with:

```sh
python3 tools/fetch_dependencies.py --native-only
python3 tools/build_native_audio.py
```

For source review alone, the first command is sufficient. Its source URL,
version and expected hash are recorded in `eng/dependencies.json`. Initial
preparation requires network access; a populated download cache can be reused
with `--offline`, optionally with `--cache-dir PATH`.

The build creates the ignored output directories automatically. Visual Studio
generators place the DLL in `artifacts/native-audio/Release/`; single-configuration
generators place it directly in `artifacts/native-audio/`. Test libraries use
the corresponding layout under `artifacts/native-audio-test/`. The actual
binary paths, hashes, host architecture and build commands are recorded in
`artifacts/native-audio/build-evidence.json`.

The test build also includes a deterministic retirement regression. Run it with:

```sh
ctest --test-dir artifacts/native-audio-test --build-config Release --output-on-failure
```

It drives the actual consumer and notification callback in both possible orders
around underrun retirement. Stop, reroute and interruption notifications must
retain reason 2; an ordinary underrun still produces reason 1. The test uses an
isolated PCM ring and no device worker or hardware. Its scheduling hook is compiled
only into the regression executable, never either shared library.

Use `python3 tools/build.py` for dependency preparation, the production native
library and a managed Release build. This combined command does not build the
null-backend test library. When building Desktop on Windows, the project copies
the available production DLL and its license into the build or publish output.
Build the native library before building Desktop. No native binaries are
tracked in Git, and test libraries must not replace production libraries.

## Backend and lifecycle

Production enables ONLY WASAPI on Windows, shared mode, 48 kHz mono float PCM
at the ABI, low-latency performance preference and noAutoConvertSRC=true.
miniaudio handles native sample-rate/channel conversion internally. A fixed
UTF-8 WASAPI endpoint ID or null/system default can be supplied. Device
listing is not yet exposed. Other operating systems fail explicitly.
No ASIO SDK is included. Test builds enable only miniaudio's null device.

Control and producer calls are serialized; the native device callback is the
sole ring consumer. Submit validates full blocks before copying, rejects full
buffers, and performs no allocation. Callback copies PCM or silence, latches
underrun and never calls managed code, UI, logging, decoding or lifecycle APIs.
The producer must not submit while close is executing. Stop/close joins the
worker before releasing storage; failure retains the handle for retry.
Callbacks can still be in flight while stop begins; successful close is the
ownership boundary. Notifications retire output; the owner polls state and
reopens explicitly. No old PCM can revive a retired handle.

ABI keys expose format, reported period and actual WASAPI buffer separately.
A reported period is NOT measured latency. Open-time IAudioClient3 period
diagnostics and paired device/QPC clock observations are available through the
commands below. Qualification remains false; these queries do not establish
hardware latency, MMCSS behavior or hotplug/resume reliability.
Current Linux checks validate the common native consumer and null worker
start/stop/join; they do not compile or exercise the Windows-specific branch.
The .NET AudioPcmBuffer and native ring must not become two independent 40 ms
queues: the managed producer must account for staging frames when maintaining
the native queue target.

Official references:

- https://github.com/mackron/miniaudio/tree/0.11.23
- https://miniaud.io/docs/manual/index.html#15.-backends

## Managed binding checks

The binding checker uses the managed Debug output and explicitly selects the
null-backend library. Prepare both libraries and that managed configuration:

```sh
python3 tools/fetch_dependencies.py
python3 tools/build_native_audio.py
dotnet build Monitor.slnx --no-restore --configuration Debug
python3 tools/check_native_audio_binding.py
```

These checks exercise ABI/lifecycle behavior; normal audition rejects the test
library. The binding checker supports Windows and Linux library layouts.

## Windows preparation for audition and diagnostics

Build the production library and managed Release output, then resolve the
production DLL recorded in the build evidence. Run this PowerShell block from
the repository root before using any of the following commands:

```powershell
py -3 tools/build.py
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$nativeBuild = Get-Content -Raw -LiteralPath ".\artifacts\native-audio\build-evidence.json" | ConvertFrom-Json
$productionBinary = $nativeBuild.binaries | Where-Object { -not $_.test_only }
$nativeAudioDll = (Resolve-Path -LiteralPath $productionBinary.binary).Path
```

This resolves the generated DLL for either output layout, without assuming a
`Release` subdirectory. Repeat preparation after changing native sources or
build configuration. The managed commands below use `--configuration Release`
to match `tools/build.py`.

## Explicit audition

Windows engineering audition generates five tones at 75 bpm, not detected
patient beats. After the Windows preparation above, run:

```powershell
dotnet run --project tests/Monitor.Specs --no-build --configuration Release -- --audio-native-audition $nativeAudioDll
```

An optional third argument is a fixed WASAPI endpoint ID; omission follows the
default. Ctrl+C stops and joins output before unloading. Missing/wrong ABI/test
libraries and unavailable devices fail visibly; nothing auto-plays at startup.
NativeAudioOutputFactory binds ABI1 through cdecl delegates and keeps library
ownership until successful close. No managed reverse callback is registered.
Its bounded producer pump drains initial/staged managed PCM immediately;
native queue is the only 40 ms target. Full native buffers do not advance tone
phase. Pumping is single-owner and must run independently of UI in product use.
This command uses a 1 ms producer sleep for engineering audition only, not a
latency guarantee or a production scheduler. Physical audio latency requires
separate target-platform qualification.

## Open-time period diagnostics

After the Windows preparation above, use the resolved production DLL:

```powershell
dotnet run --project tests/Monitor.Specs --no-build --configuration Release -- --audio-native-diagnostics $nativeAudioDll > .\artifacts\native-audio\audio-open.json
```

This opens but does not start playback. Optional fixed endpoint ID is accepted.
ABI1 keys 10–18 add an open-time IAudioClient3 snapshot: query status, supported
period range, current period, corresponding engine rate/channels and HRESULT.
Missing keys on older DLLs return unavailable, not a zero-latency result.
Snapshot status 1 means queries succeeded, NOT that output latency passed.
Periods may change after the query; rebuild/reopen to capture a fresh snapshot.
The wrapper releases COM query references/format memory outside callbacks.
Linux builders with Clang and MinGW headers can additionally run
`python3 tools/build_native_audio.py --windows-syntax`; this checks Windows
source syntax but does not link/run a DLL or qualify a device.

## Running clock probe

After the Windows preparation above, run:

```powershell
dotnet run --project tests/Monitor.Specs --no-build --configuration Release -- --audio-native-clock-probe $nativeAudioDll > .\artifacts\native-audio\audio-clock.json
```

An optional endpoint ID follows the DLL argument. The probe starts
approximately one second of SILENT output and collects 20 paired
clock observations. It stops/joins before serializing JSON so output I/O does
not starve the producer. Unlike --audio-native-diagnostics, this starts a stream.

ABI1 optionally exports sa_clock_sample with scalar 64-bit outputs; old libraries
report unavailable through the managed adapter. Each stream owns an IAudioClock
reference until close; control-thread queries never borrow a reroutable raw
client pointer or run inside the callback. Position/frequency gives stream time;
returned QPC is already in 100 ns units. S_FALSE remains reduced accuracy and is
excluded from nominal 48 kHz frame conversion. No sample is injected into
AudioClockBridge or treated as physical latency. Converter offset calibration
and clock filtering remain pending. The null backend returns unavailable,
not a simulated clock.
