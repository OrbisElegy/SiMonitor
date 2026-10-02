#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Run explicit managed/native checks against the isolated null library."""
from pathlib import Path
import subprocess
import sys


def main():
    root = Path(__file__).resolve().parent.parent
    name = 'sim_audio_native.dll' if sys.platform == 'win32' else 'libsim_audio_native.so'
    lib = root / 'artifacts/native-audio-test' / name
    if not lib.exists():
        lib = lib.parent / 'Release' / name
    specs = root / 'tests/Monitor.Specs/bin/Debug/net10.0/Monitor.Specs.dll'
    subprocess.run(['dotnet', str(specs), '--audio-native-check', str(lib)], check=True)
    # This must refuse the null sink rather than silently "play" into it.
    result = subprocess.run(['dotnet', str(specs), '--audio-native-audition', str(lib)], capture_output=True)
    if result.returncode != 1 or b'TestBackendRejected' not in result.stderr:
        raise RuntimeError(f'Audition failed to reject the test backend: {result.stderr!r}')
    print('PASS production audition refuses test library')


if __name__ == '__main__':
    main()
