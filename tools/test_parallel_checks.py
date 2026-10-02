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
                    logs = {
                        '--shard': b'ok 1 - ' + diagnostic + b'\n(1 passed; total 1)\n',
                        '--smoke-shard': b'ok: native scenario 0 ' + diagnostic + b'\nnative scenarios total: 1\n',
                    }

                    def run(command, **kwargs):
                        # A child writes to the inherited descriptor, bypassing Python's text wrapper.
                        os.write(kwargs['stdout'].fileno(), logs[command[2]])
                        return subprocess.CompletedProcess(command, 0)

                    with patch.object(runner, '__file__', str(root / 'tools/run_parallel_checks.py')), \
                            patch('sys.argv', ['run_parallel_checks.py', '--jobs', '1']), \
                            patch.object(runner.subprocess, 'run', side_effect=run), \
                            contextlib.redirect_stdout(io.StringIO()):
                        self.assertEqual(runner.main(), 0)
                    output = root / 'artifacts/parallel-checks'
                    self.assertEqual((output / 'specs-00.log').read_bytes(), logs['--shard'])
                    self.assertEqual((output / 'native-00.log').read_bytes(), logs['--smoke-shard'])


if __name__ == '__main__':
    unittest.main()
