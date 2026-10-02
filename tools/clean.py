#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Remove repository-local build outputs; dirclean also removes fetched sources."""

import argparse
import os
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parent.parent
GENERATED_DIRECTORIES = {'bin', 'obj', 'TestResults', '__pycache__'}
PRUNE = {'.git', '.cache', 'artifacts', '.vs', '.idea', '.vscode'} | GENERATED_DIRECTORIES


def collect_targets(root, mode):
    targets = {root / 'artifacts'}
    for current, directories, files in os.walk(root, followlinks=False):
        folder = Path(current)
        if folder != root and (folder / '.git').exists():
            directories.clear()
            continue
        targets.update(folder / name for name in directories if name in GENERATED_DIRECTORIES)
        targets.update(folder / name for name in files if name.endswith(('.pyc', '.pyo', '.trx')))
        directories[:] = [name for name in directories
                          if name not in PRUNE and not (folder / name).is_symlink()]

    if mode == 'dirclean':
        targets.add(root / '.cache')
        targets.add(root / 'native/sim_audio_native/vendor/miniaudio.h')
    return sorted((path for path in targets if path.exists() or path.is_symlink()),
                  key=lambda path: path.relative_to(root).as_posix())


def check_targets(root, targets):
    for path in targets:
        relative = path.relative_to(root)
        ancestor = root
        if not relative.parts or '.git' in relative.parts:
            raise ValueError(f'Unsafe cleanup target: {path}')
        for part in relative.parts:
            ancestor /= part
            if ancestor.is_symlink():
                raise ValueError(f'Unsafe cleanup target: {path}')

    if not (root / '.git').exists():
        return
    tracked = subprocess.check_output(['git', 'ls-files', '--cached', '-z'], cwd=root).decode('utf-8').split('\0')
    for path in targets:
        relative = path.relative_to(root).as_posix()
        if any(name == relative or name.startswith(relative + '/') for name in tracked if name):
            raise ValueError(f'Cleanup target contains tracked files: {path}')


def run(root, mode, dry_run=False):
    root = root.resolve(strict=True)
    targets = collect_targets(root, mode)
    check_targets(root, targets)
    for path in targets:
        print(('Would remove: ' if dry_run else 'Removing: ') + path.relative_to(root).as_posix())
        if not dry_run:
            if path.is_dir():
                shutil.rmtree(path)
            else:
                path.unlink()
    if not targets:
        print('Nothing to remove.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('clean', 'dirclean'))
    parser.add_argument('--dry-run', action='store_true', help='List targets without deleting them')
    args = parser.parse_args()
    try:
        run(ROOT, args.mode, args.dry_run)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Cleanup failed: {error}\n')


if __name__ == '__main__':
    main()
