#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Run already-built specs/native checks in isolated processes, up to 32 workers.
Desktop checks open native windows and require a graphical session.
On Linux without a display, use xvfb-run -a python3 tools/run_parallel_checks.py.
"""
import argparse
import concurrent.futures
import os
import re
from pathlib import Path
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--jobs', type=int, default=min(32, os.cpu_count() or 1))
    parser.add_argument('--configuration', choices=['Debug', 'Release'], default='Debug')
    choice = parser.add_mutually_exclusive_group()
    choice.add_argument('--specs-only', action='store_true', help='Run core specifications without desktop windows')
    choice.add_argument('--native-only', action='store_true', help='Run desktop checks using native windows')
    args = parser.parse_args()
    if not 1 <= args.jobs <= 32:
        parser.error('--jobs must be between 1 and 32')
    root = Path(__file__).resolve().parent.parent
    output = root / 'artifacts' / 'parallel-checks'
    output.mkdir(parents=True, exist_ok=True)
    tasks = []
    for kind, folder, name, flag in [
        ('specs', 'tests', 'Monitor.Specs', '--shard'),
        ('native', 'src', 'Monitor.Desktop', '--smoke-shard'),
    ]:
        if (kind == 'native' and args.specs_only) or (kind == 'specs' and args.native_only):
            continue
        dll = root / folder / name / 'bin' / args.configuration / 'net10.0' / (name + '.dll')
        if not dll.exists():
            parser.error(f'Build the solution first: missing {dll}')
        for shard in range(args.jobs):
            tasks.append((f'{kind}-{shard:02}', ['dotnet', str(dll), flag, str(shard), str(args.jobs)]))

    def run(task):
        name, command = task
        started = time.monotonic()
        with (output / (name + '.log')).open('wb') as log:
            result = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, check=False)
        return name, result.returncode, time.monotonic() - started

    started = time.monotonic()
    failures = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for name, status, seconds in pool.map(run, tasks):
            print(f'{"PASS" if status == 0 else "FAIL"} {name}: {seconds:.1f}s', flush=True)
            if status != 0:
                failures.append(name)
    for kind, id_pattern, total_pattern, first in [
        ('specs', rb'^ok (\d+) -', rb'; total (\d+)\)', 1),
        ('native', rb'^ok: native scenario (\d+) ', rb'^native scenarios total: (\d+)', 0),
    ]:
        logs = [output / (name + '.log') for name, _ in tasks if name.startswith(kind + '-')]
        if not logs:
            continue
        ids, totals = [], []
        for path in logs:
            # Subprocesses own their output encoding; coverage markers are ASCII.
            data = path.read_bytes()
            ids.extend(map(int, re.findall(id_pattern, data, re.MULTILINE)))
            totals.extend(map(int, re.findall(total_pattern, data, re.MULTILINE)))
        if len(totals) != len(logs) or len(set(totals)) != 1 or sorted(ids) != list(range(first, first + totals[0])):
            failures.append(kind + '-coverage')
            print(f'FAIL {kind}: missing, duplicate or incomplete shard coverage')
        else:
            print(f'PASS {kind}: {totals[0]} checks, each executed exactly once')
    print(f'{len(tasks)} shards, {args.jobs} workers, elapsed {time.monotonic() - started:.1f}s; logs: {output}')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
