#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Restore the selected CPU TTS models and isolated Python environment."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import venv
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def safe_path(root, relative):
    parts = PurePosixPath(relative)
    if not relative or parts.is_absolute() or '..' in parts.parts or '\\' in relative or ':' in relative:
        raise ValueError(f'Unsafe TTS destination: {relative}')
    path = root.joinpath(*parts.parts)
    for parent in [path, *path.parents]:
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError(f'Symlink TTS destination: {path}')
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError(f'Unsafe TTS destination: {path}')
    return path


def valid(path, item):
    return (path.is_file() and not path.is_symlink()
            and path.stat().st_size <= item['maximum_bytes'] and digest(path) == item['sha256'])


def copy_limited(source, target, maximum):
    total = 0
    with target.open('wb') as output:
        while chunk := source.read(1024 * 1024):
            total += len(chunk)
            if total > maximum:
                raise ValueError('TTS download or member exceeds size limit')
            output.write(chunk)


def download(item, target, opener):
    if not item['url'].startswith('https://'):
        raise ValueError('TTS downloads require HTTPS')
    request = urllib.request.Request(item['url'], headers={'User-Agent': 'SiMonitor-TTS/1'})
    with opener(request, timeout=60) as response:
        if not response.geturl().startswith('https://'):
            raise ValueError('TTS download redirected away from HTTPS')
        copy_limited(response, target, item['maximum_bytes'])
    if not valid(target, item):
        raise ValueError(f'TTS SHA-256 mismatch: {item["url"]}')


def unpack(archive_path, item, staging):
    # Only declared regular files are read; never extract archive paths or links.
    if item['format'] == 'zip':
        with zipfile.ZipFile(archive_path) as archive:
            for member in item['members']:
                info = archive.getinfo(member['path'])
                if info.is_dir() or (info.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError('TTS archive member is not a regular file')
                target = safe_path(staging, member['path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info) as source:
                    copy_limited(source, target, member['maximum_bytes'])
    elif item['format'] == 'tar.bz2':
        with tarfile.open(archive_path, 'r:bz2') as archive:
            for member in item['members']:
                info = archive.getmember(member['path'])
                if not info.isfile():
                    raise ValueError('TTS archive member is not a regular file')
                target = safe_path(staging, member['path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(info) as source:
                    copy_limited(source, target, member['maximum_bytes'])
    else:
        raise ValueError('Unsupported TTS archive format')
    for member in item['members']:
        if not valid(safe_path(staging, member['path']), member):
            raise ValueError(f'TTS member SHA-256 mismatch: {member["path"]}')


def fetch_models(root=ROOT, cache=None, offline=False, check=False, opener=urllib.request.urlopen):
    cache = Path(cache).resolve() if cache else root / '.cache/tts'
    manifest = json.loads((root / 'eng/audio/tts-models.json').read_text(encoding='utf-8'))
    entries = [(a, a['members']) for a in manifest['archives']]
    entries += [(f, [f]) for f in manifest['files']]
    # Validate the entire manifest before making any changes.
    for item, members in entries:
        for entry in [item, *members]:
            if len(entry['sha256']) != 64 or any(c not in '0123456789abcdef' for c in entry['sha256']):
                raise ValueError('Invalid TTS SHA-256')
            if not isinstance(entry['maximum_bytes'], int) or entry['maximum_bytes'] <= 0:
                raise ValueError('Invalid TTS size limit')
        if not item['url'].startswith('https://'):
            raise ValueError('TTS downloads require HTTPS')
        for member in members:
            safe_path(cache, member['path'])
    for item, members in entries:
        missing = [m for m in members if not valid(safe_path(cache, m['path']), m)]
        if not missing:
            print('verified TTS:', item.get('id', item.get('path')))
            continue
        if check or offline:
            raise ValueError('TTS files missing or corrupt: ' + ', '.join(m['path'] for m in missing)
                             + '; run python3 tools/fetch_dependencies.py --tts-only')
        cache.mkdir(parents=True, exist_ok=True)
        print('downloading TTS:', item.get('id', item.get('path')), flush=True)
        with tempfile.TemporaryDirectory(prefix='.tts-download-', dir=cache) as temporary:
            temporary = Path(temporary)
            blob = temporary / 'download'
            download(item, blob, opener)
            if 'members' in item:
                unpack(blob, item, temporary / 'files')
            else:
                target = safe_path(temporary / 'files', item['path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(blob, target)
            # All member hashes pass before any existing files are replaced.
            for member in missing:
                target = safe_path(cache, member['path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                os.replace(safe_path(temporary / 'files', member['path']), target)
    return cache


def environment_python(cache):
    return cache / 'venv' / ('Scripts/python.exe' if os.name == 'nt' else 'bin/python')


def requirements(root):
    lines = (root / 'eng/audio/tts-requirements.txt').read_text(encoding='utf-8').splitlines()
    return dict(line.strip().split('==') for line in lines if line.strip() and not line.startswith('#'))


def environment_valid(python, expected):
    if not python.is_file():
        return False
    code = 'import importlib.metadata as m,json,sys; print(json.dumps({n:m.version(n) for n in json.loads(sys.argv[1])}))'
    result = subprocess.run([str(python), '-B', '-c', code, json.dumps(list(expected))], capture_output=True, text=True)
    try:
        return result.returncode == 0 and json.loads(result.stdout) == expected
    except json.JSONDecodeError:
        return False


def prepare_python(root, cache, offline=False, check=False):
    python = environment_python(cache)
    expected = requirements(root)
    if environment_valid(python, expected):
        print('verified TTS Python:', python)
        return python
    if check or offline:
        raise ValueError('TTS Python environment missing or incompatible; run --tts-only online first')
    if (cache / 'venv').is_symlink():
        raise ValueError('Refusing to install into a symlinked TTS environment')
    if not python.is_file():
        try:
            venv.EnvBuilder(with_pip=importlib.util.find_spec('ensurepip') is not None).create(cache / 'venv')
        except (subprocess.CalledProcessError, OSError):
            # Some distributions separate ensurepip from Python; use their pip
            # frontend to install only into the isolated environment instead.
            venv.EnvBuilder(with_pip=False).create(cache / 'venv')
    pip_check = subprocess.run([str(python), '-m', 'pip', '--version'], capture_output=True)
    pip = [str(python), '-m', 'pip'] if pip_check.returncode == 0 else [sys.executable, '-m', 'pip', '--python', str(python)]
    subprocess.run([*pip, 'install', '--no-cache-dir', '--disable-pip-version-check',
                    '-r', str(root / 'eng/audio/tts-requirements.txt')], check=True)
    if not environment_valid(python, expected):
        raise ValueError('TTS Python environment failed version verification')
    return python


def prepare(root=ROOT, cache=None, offline=False, check=False, models_only=False):
    cache = fetch_models(root, cache, offline, check)
    if not models_only:
        prepare_python(root, cache, offline, check)
    return cache


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache-dir', type=Path, help='TTS cache root; default .cache/tts')
    parser.add_argument('--offline', action='store_true')
    parser.add_argument('--check', action='store_true', help='Read-only verification; no network or installs')
    parser.add_argument('--models-only', action='store_true', help='Skip Python environment preparation')
    args = parser.parse_args()
    try:
        prepare(cache=args.cache_dir, offline=args.offline, check=args.check, models_only=args.models_only)
        return 0
    except (OSError, ValueError, KeyError, tarfile.TarError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f'TTS preparation failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
