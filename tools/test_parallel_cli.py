# SPDX-License-Identifier: AGPL-3.0-or-later
"""Keep the desktop-only CLI alias compatible with isolated desktop checks."""
import contextlib
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import run_parallel_checks as runner


class ParallelCheckCliTests(unittest.TestCase):
    def test_desktop_only_and_legacy_alias_select_the_same_checks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            assembly = root / 'src/Monitor.Desktop/bin/Debug/net10.0/Monitor.Desktop.dll'
            assembly.parent.mkdir(parents=True)
            assembly.write_bytes(b'fixture')
            for flag in ('--desktop-only', '--native-only'):
                with self.subTest(flag=flag):
                    commands = []

                    def run(command, **kwargs):
                        commands.append(command)
                        os.write(kwargs['stderr'].fileno(), b'ALSA lib ... error evaluating name')
                        os.write(kwargs['stdout'].fileno(), b'ok: native scenario 0 fixture\nnative scenarios total: 1\n')
                        return subprocess.CompletedProcess(command, 0)

                    with patch.object(runner, '__file__', str(root / 'tools/run_parallel_checks.py')), \
                            patch('sys.argv', ['run_parallel_checks.py', '--jobs', '1', flag]), \
                            patch.object(runner.subprocess, 'run', side_effect=run), \
                            contextlib.redirect_stdout(io.StringIO()):
                        self.assertEqual(runner.main(), 0)
                    self.assertEqual(len(commands), 1)
                    self.assertEqual(commands[0][2], '--smoke-shard')


if __name__ == '__main__':
    unittest.main()
