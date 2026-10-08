#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Fetch native sources, locked NuGet packages and optional TTS tools (Python 3.11+)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def valid(path, expected):
    return path.is_file() and not path.is_symlink() and digest(path) == expected


def sources(root):
    ledger = json.loads((root / 'eng/dependencies.json').read_text(encoding='utf-8'))
    for entry in ledger['native_dependencies']:
        for item in entry.get('source_files', []):
            if 'url' in item:
                relative = Path(item['path'])
                if relative.is_absolute() or '..' in relative.parts or '\\' in item['path']:
                    raise ValueError('Invalid dependency destination')
                if not item['url'].startswith('https://') or len(item['sha256']) != 64:
                    raise ValueError('Expected HTTPS source and SHA-256')
                yield item


def fetch_native(root=ROOT, cache=None, offline=False, check=False, opener=urllib.request.urlopen):
    root = root.resolve()
    cache = Path(cache).resolve() if cache else root / '.cache/downloads'
    for item in sources(root):
        destination = root / item['path']
        if not destination.resolve().is_relative_to(root) or destination.is_symlink():
            raise ValueError(f'Unsafe dependency destination: {destination}')
        expected = item['sha256']
        if valid(destination, expected):
            print('verified:', item['path'])
            continue
        if check:
            raise ValueError(f'Missing or changed {item["path"]}; run python tools/fetch_dependencies.py')
        blob = cache / expected
        if not valid(blob, expected):
            if offline:
                raise ValueError(f'Offline cache missing or corrupt: {blob}')
            cache.mkdir(parents=True, exist_ok=True)
            fd, temporary = tempfile.mkstemp(prefix='download-', dir=cache)
            try:
                with os.fdopen(fd, 'wb') as output:
                    request = urllib.request.Request(item['url'], headers={'User-Agent': 'SiMonitor-build/1'})
                    with opener(request, timeout=60) as response:
                        if not response.geturl().startswith('https://'):
                            raise ValueError('Refusing non-HTTPS redirect')
                        total = 0
                        while chunk := response.read(65536):
                            total += len(chunk)
                            if total > item['maximum_bytes']:
                                raise ValueError('Dependency exceeds size limit')
                            output.write(chunk)
                if digest(Path(temporary)) != expected:
                    raise ValueError(f'SHA-256 mismatch: {item["url"]}')
                os.replace(temporary, blob)
            finally:
                Path(temporary).unlink(missing_ok=True)
        destination.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(prefix='.source-', dir=destination.parent)
        try:
            with os.fdopen(fd, 'wb') as output, blob.open('rb') as source:
                shutil.copyfileobj(source, output)
            if digest(Path(temporary)) != expected:
                raise ValueError('Cached source changed during copy')
            os.replace(temporary, destination)
        finally:
            Path(temporary).unlink(missing_ok=True)
        print('prepared:', item['path'])


def restore_managed(root=ROOT, offline=False):
    command = ['dotnet', 'restore', str(root / 'Monitor.slnx'), '--locked-mode', '-p:NuGetAudit=false']
    # Empty feeds enforce offline operation while allowing NuGet's global cache.
    with tempfile.TemporaryDirectory(prefix='simonitor-restore-') as temporary:
        if offline:
            config = Path(temporary) / 'NuGet.Config'
            config.write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
            command += ['--configfile', str(config)]
        subprocess.run(command, cwd=root, check=True)


def main():
    if sys.version_info < (3, 11):
        print('Python 3.11 or newer is required.', file=sys.stderr)
        return 1
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--offline', action='store_true', help='Use existing caches without network; includes TTS when selected')
    parser.add_argument('--native-only', action='store_true', help='Skip NuGet restore')
    parser.add_argument('--check', action='store_true', help='Verify selected native/TTS resources without writes or network; skip NuGet')
    parser.add_argument('--cache-dir', type=Path, help='Native download cache (default: .cache/downloads)')
    tts = parser.add_mutually_exclusive_group()
    tts.add_argument('--tts', action='store_true', help='Also restore selected TTS models and CPU Python tools')
    tts.add_argument('--tts-only', action='store_true', help='Restore only selected TTS models and CPU Python tools')
    parser.add_argument('--tts-cache-dir', type=Path, help='TTS cache root (default: .cache/tts)')
    args = parser.parse_args()
    if args.tts_cache_dir and not (args.tts or args.tts_only):
        parser.error('--tts-cache-dir requires --tts or --tts-only')
    if args.tts_only and args.native_only:
        parser.error('--tts-only and --native-only cannot be combined')
    try:
        if args.tts or args.tts_only:
            from fetch_tts_models import prepare
            prepare(cache=args.tts_cache_dir, offline=args.offline, check=args.check)
        if not args.tts_only:
            fetch_native(cache=args.cache_dir, offline=args.offline, check=args.check)
            if not args.native_only and not args.check:
                restore_managed(offline=args.offline)
        return 0
    except (OSError, ValueError, KeyError, tarfile.TarError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f'Dependency preparation failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
