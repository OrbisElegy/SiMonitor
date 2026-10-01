#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Prepare dependencies and build SiMonitor for this host; never publish or push."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from fetch_dependencies import ROOT, fetch_native, restore_managed


def build_product(configuration, jobs):
    project = ROOT / 'src/Monitor.Desktop/Monitor.Desktop.csproj'
    common = ['--no-restore', '-c', configuration, '-p:UseSharedCompilation=false', f'-m:{jobs}']
    # The host development build supplies assets; no generator CLI ships in product mode.
    subprocess.run(['dotnet', 'build', str(project), *common, '-p:ProductRelease=false'], cwd=ROOT, check=True)
    catalog = ROOT / 'src/Monitor.Desktop/bin' / configuration / 'net10.0/style-previews.bin'
    if not catalog.is_file():
        raise ValueError('Host preview catalog was not generated')
    output = ROOT / 'artifacts/release'
    output.parent.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='release-build-', dir=output.parent) as temporary:
        stage = Path(temporary) / 'output'
        subprocess.run(['dotnet', 'build', str(project), *common, '-p:ProductRelease=true',
                        '-p:UsePrebuiltStylePreviews=true', f'-p:StylePreviewBinary={catalog}',
                        '-o', str(stage)], cwd=ROOT, check=True)
        if not (stage / 'Monitor.Desktop.dll').is_file() or not (stage / 'style-previews.bin').is_file():
            raise ValueError('Incomplete product build')
        previous = Path(temporary) / 'previous'
        if output.exists():
            output.rename(previous)
        try:
            stage.rename(output)
        except OSError:
            if previous.exists():
                previous.rename(output)
            raise
    print('Product release:', output)
    print('Run: dotnet ' + str(output / 'Monitor.Desktop.dll'))


def main():
    if sys.version_info < (3, 11):
        print('Python 3.11 or newer is required.', file=sys.stderr)
        return 1
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--product-release', action='store_true', help='Build the product without development entry points')
    parser.add_argument('--configuration', choices=['Debug', 'Release'], default='Release')
    parser.add_argument('--jobs', type=int, default=min(32, os.cpu_count() or 1))
    parser.add_argument('--offline', action='store_true', help='Restore exclusively from existing caches')
    parser.add_argument('--managed-only', action='store_true', help='Skip the optional native audio build')
    parser.add_argument('--cache-dir', type=Path, help='Native download cache')
    args = parser.parse_args()
    if not 1 <= args.jobs <= 32:
        parser.error('--jobs must be between 1 and 32')
    if args.product_release and args.configuration != 'Release':
        parser.error('Product releases require --configuration Release')
    required = ['dotnet'] + ([] if args.managed_only else ['cmake'])
    missing = [name for name in required if shutil.which(name) is None]
    if missing:
        parser.error('Install required tools first: ' + ', '.join(missing))
    try:
        fetch_native(cache=args.cache_dir, offline=args.offline)
        restore_managed(offline=args.offline)
        subprocess.run([sys.executable, str(ROOT / 'tools/verify_dependency_ledger.py')], cwd=ROOT, check=True)
        if not args.managed_only:
            subprocess.run([sys.executable, str(ROOT / 'tools/build_native_audio.py'),
                            '--production-only', '--jobs', str(args.jobs)], cwd=ROOT, check=True)
        if args.product_release:
            build_product(args.configuration, args.jobs)
            return 0
        subprocess.run(['dotnet', 'build', 'Monitor.slnx', '--no-restore', '-c', args.configuration,
                        '-p:UseSharedCompilation=false', f'-m:{args.jobs}'], cwd=ROOT, check=True)
        print('Built:', ROOT / 'src/Monitor.Desktop/bin' / args.configuration / 'net10.0')
        print('Run: dotnet run --project src/Monitor.Desktop --no-build -c ' + args.configuration)
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f'Build failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
