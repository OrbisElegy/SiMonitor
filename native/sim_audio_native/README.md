# Native audio ABI1

Project wrapper: AGPL-3.0-or-later. Downloaded miniaudio0.11.23 is unchanged,
commit f40cf03f80cdb7e741d43e53b7e706e8c1394bcf, selected license MIT-0.
Original dual-license notice is preserved in vendor/LICENSE.miniaudio;
source hashes and origin are in eng/dependencies.json.

First run `python3 tools/fetch_dependencies.py --native-only` from the repository
root, then `python3 tools/build_native_audio.py`. Use `python3 tools/build.py`
for the combined dependency/native/managed production build.
Requires CMake3.20+ and a C compiler (Windows: Visual Studio C build tools).
Production and SIM_AUDIO_TEST builds use separate artifact directories.
The test DLL MUST NOT be shipped or copied over the production DLL.
Local binary hashes, host architecture and build commands are written to
artifacts/native-audio/build-evidence.json; CMake records compiler details.
No native binary is checked into Git or bundled into Desktop yet.

Production enables ONLY WASAPI on Windows, shared mode,48kHz mono float PCM
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
A reported period is NOT measured latency. Qualification always returns false:
IAudioClient3 default/fundamental/minimum/maximum and current-period diagnostics,
paired device/QPC timestamps, full native fault reporting,
MMCSS evidence, hardware latency and hotplug/resume validation remain pending.
Current Linux checks validate the common native consumer and null worker
start/stop/join; they do not compile or exercise the Windows-specific branch.
The .NET AudioPcmBuffer and native ring must not become two40ms queues in the
product: managed integration must choose a single target queue and account for
any staging frames. No UI or physiological sound source is connected here.

Official references:
- https://github.com/mackron/miniaudio/tree/0.11.23
- https://miniaud.io/docs/manual/index.html#15.-backends

## Managed integration and explicit audition

After native build and `dotnet build Monitor.slnx`, run
`python3 tools/check_native_audio_binding.py`. This explicitly selects the test
library for ABI/lifecycle checks; normal audition rejects that library.

Windows engineering audition (five75bpm tones, not detected patient beats), run
from the repository root. `Resolve-Path` resolves the repository-relative DLL
path to the absolute path required by the command:

```powershell
dotnet run --project tests/Monitor.Specs --no-build -- --audio-native-audition (Resolve-Path -LiteralPath ".\artifacts\native-audio\Release\sim_audio_native.dll").Path
```

For single-configuration generators the DLL is directly in native-audio.
An optional third argument is a fixed WASAPI endpoint ID; omission follows the
default. Ctrl+C stops and joins output before unloading. Missing/wrong ABI/test
libraries and unavailable devices fail visibly; nothing auto-plays at startup.
NativeAudioOutputFactory binds ABI1 through cdecl delegates and keeps library
ownership until successful close. No managed reverse callback is registered.
Its bounded producer pump drains initial/staged managed PCM immediately;
native queue is the only40ms target. Full native buffers do not advance tone
phase. Pumping is single-owner and must run independently of UI in product use.
This command uses a1ms producer sleep for engineering audition only, not a
latency guarantee or a production scheduler. Physical audio latency requires separate target-platform qualification.

## Open-time period diagnostics

After rebuilding the production DLL, resolve the same repository-relative path:

```powershell
dotnet run --project tests/Monitor.Specs --no-build -- --audio-native-diagnostics (Resolve-Path -LiteralPath ".\artifacts\native-audio\Release\sim_audio_native.dll").Path > audio-open.json
```

This opens but does not start playback. Optional fixed endpoint ID is accepted.
ABI1 keys10–18 add an open-time IAudioClient3 snapshot: query status, supported
period range, current period, corresponding engine rate/channels and HRESULT.
Missing keys on older DLLs return unavailable, not a zero-latency result.
Snapshot status1 means queries succeeded, NOT that output latency passed.
Periods may change after the query; rebuild/reopen to capture a fresh snapshot.
The wrapper releases COM query references/format memory outside callbacks.
Linux builders with Clang and MinGW headers can additionally run
`python3 tools/build_native_audio.py --windows-syntax`; this checks Windows
source syntax but does not link/run a DLL or qualify a device.

## Running clock probe

Rebuild the native library, then use `--audio-native-clock-probe ABSOLUTE_DLL`
(optionally followed by endpoint ID) through the same Monitor.Specs command.
It starts approximately one second of SILENT output and collects20 paired
clock observations. It stops/joins before serializing JSON so output I/O does
not starve the producer. Unlike --audio-native-diagnostics, this starts a stream.

ABI1 optionally exports sa_clock_sample with scalar64-bit outputs; old libraries
report unavailable through the managed adapter. Each stream owns an IAudioClock
reference until close; control-thread queries never borrow a reroutable raw
client pointer or run inside the callback. Position/frequency gives stream time;
returned QPC is already100ns. S_FALSE remains reduced accuracy and is excluded
from nominal48kHz frame conversion. No sample is injected into AudioClockBridge
or treated as physical latency. Converter offset calibration and clock filtering
remain pending. The null backend returns unavailable, not a simulated clock.
