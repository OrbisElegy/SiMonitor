#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Build isolated production/test artifacts and record local binary evidence."""
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
source = root / 'native/sim_audio_native'
rows = []
for testing in (False, True):
    folder = root / 'artifacts' / ('native-audio-test' if testing else 'native-audio')
    commands = [
        ['cmake', '-S', str(source), '-B', str(folder),
         '-DSIM_AUDIO_TEST=' + ('ON' if testing else 'OFF'), '-DCMAKE_BUILD_TYPE=Release'],
        ['cmake', '--build', str(folder), '--config', 'Release', '--parallel', '32'],
    ]
    for cmd in commands:
        subprocess.run(cmd, check=True)
    name = 'sim_audio_native.dll' if sys.platform == 'win32' else 'libsim_audio_native.so'
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
            'compiler_configuration': 'See adjacent CMakeCache.txt and CMakeFiles compiler metadata',
            'miniaudio_commit': 'f40cf03f80cdb7e741d43e53b7e706e8c1394bcf',
            'hardware_qualified': False, 'binaries': rows}
p = root / 'artifacts/native-audio/build-evidence.json'
p.write_text(json.dumps(manifest, indent=2) + '\n')
print('Binary evidence:', p)
