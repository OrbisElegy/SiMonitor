#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check native launch under an isolated Xvfb display using xwininfo and xprop.

Pass a built or published Monitor.Desktop.dll. The working directory is an
empty temporary directory, so assets must resolve beside the executable.
The default checks launch without arguments. For a development build, add
--development to check the retained --ui-preview alias as well.
This is Linux startup evidence, not Windows release qualification.
"""
import argparse
import locale
from pathlib import Path
import re
import subprocess
import tempfile
import time


def has_monitor_window(tree, process_id):
    # Version and development suffixes vary with the build and saved language.
    for window_id in re.findall(r'^\s*(0x[0-9a-fA-F]+) "Seele\'s SiMonitor · [^"\n]+":', tree, re.MULTILINE):
        result = subprocess.run(['xprop', '-id', window_id, '_NET_WM_PID'],
                                capture_output=True, encoding=locale.getencoding(), timeout=5)
        if result.returncode == 0 and re.search(
                rf'^_NET_WM_PID\(CARDINAL\) = {process_id}\s*$', result.stdout, re.MULTILINE):
            return True
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('assembly', type=Path)
    parser.add_argument('--development', action='store_true', help='Also check the development --ui-preview alias')
    args = parser.parse_args()
    assembly = args.assembly.resolve(strict=True)
    for options in ([[], ['--ui-preview']] if args.development else [[]]):
        with tempfile.TemporaryDirectory(prefix='monitor-startup-') as directory:
            with tempfile.TemporaryFile(mode='w+b') as log:
                process = subprocess.Popen(['dotnet', str(assembly), *options], cwd=directory,
                                           stdout=log, stderr=subprocess.STDOUT)
                try:
                    deadline = time.monotonic() + 20
                    while True:
                        if process.poll() is not None:
                            log.seek(0)
                            raise RuntimeError(f'Client exited: {process.returncode}\n{log.read()!r}')
                        tree = subprocess.run(['xwininfo', '-root', '-tree'], check=True,
                                              capture_output=True, encoding=locale.getencoding(), timeout=5).stdout
                        if has_monitor_window(tree, process.pid):
                            print(f'PASS: {options or "default"} opens integrated monitor from external cwd')
                            break
                        if time.monotonic() >= deadline:
                            log.seek(0)
                            raise RuntimeError(f'Monitor window did not appear\n{tree}\n{log.read()!r}')
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
