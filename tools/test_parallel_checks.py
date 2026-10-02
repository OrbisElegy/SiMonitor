# SPDX-License-Identifier: AGPL-3.0-or-later
"""Keep subprocess log bytes intact while checking ASCII shard coverage."""
import contextlib
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import run_parallel_checks as runner


class ParallelCheckLogTests(unittest.TestCase):
    def test_coverage_preserves_utf8_and_gbk_diagnostics(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for folder, name in (('tests', 'Monitor.Specs'), ('src', 'Monitor.Desktop')):
                assembly = root / folder / name / 'bin/Debug/net10.0' / (name + '.dll')
                assembly.parent.mkdir(parents=True)
                assembly.write_bytes(b'fixture')
            for diagnostic in ('波形₂'.encode('utf-8'), '波形'.encode('gbk')):
                with self.subTest(diagnostic=diagnostic):
                    logs = {}
                    for shard in range(2):
                        logs['--shard', shard] = (b'ok ' + str(shard + 1).encode('ascii') + b' - '
                                                  + diagnostic + b'\n(1 passed; total 2)\n')
                        logs['--smoke-shard', shard] = (b'ok: native scenario ' + str(shard).encode('ascii')
                                                        + b' ' + diagnostic + b'\nnative scenarios total: 2\n')
                    stderr = b'ALSA lib ... error evaluating name ' + diagnostic

                    def run(command, **kwargs):
                        # A child writes to the inherited descriptor, bypassing Python's text wrapper.
                        stderr_log = kwargs['stdout'] if kwargs['stderr'] == subprocess.STDOUT else kwargs['stderr']
                        os.write(stderr_log.fileno(), stderr)
                        os.write(kwargs['stdout'].fileno(), logs[command[2], int(command[3])])
                        return subprocess.CompletedProcess(command, 0)

                    with patch.object(runner, '__file__', str(root / 'tools/run_parallel_checks.py')), \
                            patch('sys.argv', ['run_parallel_checks.py', '--jobs', '2']), \
                            patch.object(runner.subprocess, 'run', side_effect=run), \
                            contextlib.redirect_stdout(io.StringIO()):
                        self.assertEqual(runner.main(), 0)
                    output = root / 'artifacts/parallel-checks'
                    for kind, flag in (('specs', '--shard'), ('desktop', '--smoke-shard')):
                        for shard in range(2):
                            name = f'{kind}-{shard:02}'
                            self.assertEqual((output / (name + '.log')).read_bytes(), logs[flag, shard])
                            self.assertEqual((output / (name + '.stderr.log')).read_bytes(), stderr)

    def test_stderr_markers_cannot_satisfy_missing_stdout_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            assembly = root / 'src/Monitor.Desktop/bin/Debug/net10.0/Monitor.Desktop.dll'
            assembly.parent.mkdir(parents=True)
            assembly.write_bytes(b'fixture')

            def run(command, **kwargs):
                shard = int(command[3])
                stderr_log = kwargs['stdout'] if kwargs['stderr'] == subprocess.STDOUT else kwargs['stderr']
                if shard == 0:
                    os.write(kwargs['stdout'].fileno(), b'ok: native scenario 0 fixture\n')
                else:
                    os.write(stderr_log.fileno(), b'ALSA diagnostic\nok: native scenario 1 diagnostic example\n')
                os.write(kwargs['stdout'].fileno(), b'native scenarios total: 2\n')
                return subprocess.CompletedProcess(command, 0)

            report = io.StringIO()
            with patch.object(runner, '__file__', str(root / 'tools/run_parallel_checks.py')), \
                    patch('sys.argv', ['run_parallel_checks.py', '--jobs', '2', '--desktop-only']), \
                    patch.object(runner.subprocess, 'run', side_effect=run), \
                    contextlib.redirect_stdout(report):
                self.assertEqual(runner.main(), 1)
            self.assertIn('FAIL desktop: missing, duplicate or incomplete shard coverage', report.getvalue())


if __name__ == '__main__':
    unittest.main()
