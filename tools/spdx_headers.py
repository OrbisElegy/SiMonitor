#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Add or verify SPDX headers in project code and build definitions."""
import argparse
from pathlib import Path
import re
import subprocess


PROJECT_LICENSE = 'AGPL-3.0-or-later'
ADAPTED_LICENSE = PROJECT_LICENSE + ' AND Apache-2.0'
TAG = 'SPDX-' + 'License-Identifier: '
ROOT = Path(__file__).resolve().parents[1]


def comment(path):
    name = Path(path).name
    suffix = Path(path).suffix.lower()
    if suffix == '.cs':
        return '// ', ''
    if suffix in ('.js', '.mjs', '.cjs'):
        return '// ', ''
    if suffix in ('.c', '.h'):
        return '/* ', ' */'
    if suffix in ('.csproj', '.props', '.targets', '.slnx') or name == 'NuGet.Config':
        return '<!-- ', ' -->'
    if suffix in ('.py', '.sh', '.ps1') or name in ('commit-msg', 'CMakeLists.txt'):
        return '# ', ''
    if suffix in ('.yml', '.yaml') and Path(path).parts[:2] == ('.github', 'workflows'):
        return '# ', ''
    return None


def exempt(path):
    return (path.startswith('native/sim_audio_native/vendor/')
            or path.startswith('eng/licenses/') or comment(path) is None)


def license_expression(path, data):
    if path.endswith('.cs') and b'Infirmary Integrated' in data and b'// Apache-2.0;' in data:
        return ADAPTED_LICENSE
    return PROJECT_LICENSE


def with_header(path, data):
    if exempt(path):
        return data
    prefix, suffix = comment(path)
    bom = b'\xef\xbb\xbf' if data.startswith(b'\xef\xbb\xbf') else b''
    lines = data[len(bom):].splitlines(keepends=True)
    newline = b'\r\n' if lines and lines[0].endswith(b'\r\n') else b'\n'
    header = (prefix + TAG + license_expression(path, data) + suffix).encode() + newline
    offset = 0
    if lines and (lines[0].startswith(b'#!') or lines[0].startswith(b'<?xml')):
        offset = 1
    if path.endswith('.py'):
        for index, line in enumerate(lines[:2]):
            if re.match(rb'^\s*#.*coding[:=]\s*[-\w.]+', line):
                offset = index + 1
    if offset < len(lines) and lines[offset].startswith((prefix + TAG).encode()):
        # Preserve the explicit license in older native-source revisions.
        legacy = b'/* ' + TAG.encode() + b'AGPL-3.0-only */' + newline
        native = path in ('native/sim_audio_native/sim_audio.c', 'native/sim_audio_native/sim_audio.h')
        if lines[offset] != header and not (native and lines[offset] == legacy):
            raise ValueError(f'Unexpected SPDX license header: {path}')
        return data
    lines.insert(offset, header)
    return bom + b''.join(lines)


def check_or_write(root, paths, check):
    errors = []
    count = 0
    for path in sorted(set(paths)):
        if exempt(path):
            continue
        target = root / path
        data = target.read_bytes()
        expected = with_header(path, data)
        count += 1
        if check:
            if data != expected:
                errors.append(f'Missing SPDX header: {path}')
        else:
            target.write_bytes(expected)
    if errors:
        raise ValueError('\n'.join(errors))
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    paths = subprocess.check_output(
        ['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT
    ).decode().split('\0')[:-1]
    count = check_or_write(ROOT, paths, args.check)
    print(f'PASS: SPDX coverage for {count} project code and build files')


if __name__ == '__main__':
    main()
