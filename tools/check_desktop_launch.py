#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check default/legacy-alias native launch under an isolated Xvfb display.

Pass a built or published Monitor.Desktop.dll. The working directory is an
empty temporary directory, so assets must resolve beside the executable.
This is Linux startup evidence, not Windows release qualification.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('assembly', type=Path)
    args = parser.parse_args()
    assembly = args.assembly.resolve(strict=True)
    title = '心电监护 · V0.5 Standalone（开发版）'
    for options in ([], ['--ui-preview']):
        with tempfile.TemporaryDirectory(prefix='monitor-startup-') as directory:
            with tempfile.TemporaryFile(mode='w+t') as log:
                process = subprocess.Popen(['dotnet', str(assembly), *options], cwd=directory,
                                           stdout=log, stderr=subprocess.STDOUT)
                try:
                    deadline = time.monotonic() + 20
                    while True:
                        if process.poll() is not None:
                            log.seek(0)
                            raise RuntimeError(f'Client exited: {process.returncode}\n{log.read()}')
                        tree = subprocess.run(['xwininfo', '-root', '-tree'], check=True,
                                              capture_output=True, text=True, timeout=5).stdout
                        if title in tree:
                            print(f'PASS: {options or "default"} opens integrated monitor from external cwd')
                            break
                        if time.monotonic() >= deadline:
                            log.seek(0)
                            raise RuntimeError(f'Monitor window did not appear\n{tree}\n{log.read()}')
                        time.sleep(.1)
                finally:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
