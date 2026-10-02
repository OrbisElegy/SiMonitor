#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Build isolated production/test artifacts and record local binary evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import sys

from fetch_dependencies import fetch_native


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--windows-syntax', action='store_true')
    parser.add_argument('--production-only', action='store_true')
    parser.add_argument('--jobs', type=int, default=32)
    args = parser.parse_args()
    if not 1 <= args.jobs <= 32:
        parser.error('--jobs must be between 1 and 32')
    root = Path(__file__).resolve().parent.parent
    fetch_native(root, check=True)
    source = root / 'native/sim_audio_native'
    rows = []
    syntax_command = None
    if args.windows_syntax:
        syntax_command = ['clang', '--target=x86_64-w64-windows-gnu',
                          '-isystem', '/usr/share/mingw-w64/include', '-fsyntax-only',
                          str(source / 'sim_audio.c')]
        subprocess.run(syntax_command, check=True)
    for testing in ((False,) if args.production_only else (False, True)):
        folder = root / 'artifacts' / ('native-audio-test' if testing else 'native-audio')
        commands = [
            ['cmake', '-S', str(source), '-B', str(folder),
             '-DSIM_AUDIO_TEST=' + ('ON' if testing else 'OFF'), '-DCMAKE_BUILD_TYPE=Release'],
            ['cmake', '--build', str(folder), '--config', 'Release', '--parallel', str(args.jobs)],
        ]
        if testing:
            commands[0].append('-DBUILD_TESTING=ON')
        for cmd in commands:
            subprocess.run(cmd, check=True)
        if testing:
            subprocess.run(['ctest', '--test-dir', str(folder), '--build-config', 'Release',
                            '--output-on-failure', '--no-tests=error'], check=True)
        name = 'sim_audio_native.dll' if sys.platform == 'win32' else ('libsim_audio_native.dylib' if sys.platform == 'darwin' else 'libsim_audio_native.so')
        binary = folder / name
        if not binary.exists():
            binary = folder / 'Release' / name
        if testing or sys.platform.startswith('linux'):
            cmd = [sys.executable, str(source / 'tests/check_abi.py'), str(binary)]
            if not testing:
                cmd.append('--production-unavailable')
            subprocess.run(cmd, check=True)
        rows.append({'binary': str(binary.relative_to(root)),
                     'sha256': hashlib.sha256(binary.read_bytes()).hexdigest(),
                     'test_only': testing, 'configure_and_build': commands})
    manifest = {'host_os': platform.system(), 'host_arch': platform.machine(),
                'windows_syntax_only_command': syntax_command,
                'compiler_configuration': 'See adjacent CMakeCache.txt and CMakeFiles compiler metadata',
                'miniaudio_commit': 'f40cf03f80cdb7e741d43e53b7e706e8c1394bcf',
                'hardware_qualified': False, 'binaries': rows}
    p = root / 'artifacts/native-audio/build-evidence.json'
    p.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Binary evidence:', p)


if __name__ == '__main__':
    main()
