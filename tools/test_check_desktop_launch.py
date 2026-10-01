# SPDX-License-Identifier: AGPL-3.0-or-later
"""Exercise launch modes and window identity without a live graphical session."""
import contextlib
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch

import check_desktop_launch as launch


class DesktopLaunchTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.assembly = Path(temporary.name) / 'Monitor.Desktop.dll'
        self.assembly.write_bytes(b'fixture')
        self.process = Mock(pid=321)
        self.process.poll.return_value = None
        self.process.wait.return_value = 0
        self.commands = []
        self.title = "Seele's SiMonitor · V0.6 Standalone"
        self.window_pid = 321

    def run_x11_tool(self, command, **kwargs):
        if command[0] == 'xwininfo':
            output = f'  0x200001 "{self.title}": ("Monitor.Desktop" "Monitor.Desktop") 1440x940+0+0\n'
        else:
            self.assertEqual(command, ['xprop', '-id', '0x200001', '_NET_WM_PID'])
            output = f'_NET_WM_PID(CARDINAL) = {self.window_pid}\n'
        return subprocess.CompletedProcess(command, 0, stdout=output)

    def start(self, command, **kwargs):
        self.commands.append(command)
        self.assertNotEqual(Path(kwargs['cwd']), self.assembly.parent)
        return self.process

    def run_check(self, *options, times=None):
        with patch('sys.argv', ['check_desktop_launch.py', str(self.assembly), *options]), \
                patch.object(launch.subprocess, 'Popen', side_effect=self.start), \
                patch.object(launch.subprocess, 'run', side_effect=self.run_x11_tool), \
                patch.object(launch.time, 'monotonic', side_effect=times or [0, 0]), \
                contextlib.redirect_stdout(io.StringIO()):
            return launch.main()

    def test_default_launch_accepts_product_and_localized_development_titles(self):
        for title in ("Seele's SiMonitor · V0.6 Standalone",
                      "Seele's SiMonitor · V1.2 Standalone（开发版）",
                      "Seele's SiMonitor · V2.0 Standalone (development)"):
            with self.subTest(title=title):
                self.title = title
                self.commands.clear()
                self.assertEqual(self.run_check(), 0)
                self.assertEqual(self.commands, [['dotnet', str(self.assembly)]])
        self.assertEqual(self.process.terminate.call_count, 3)

    def test_development_mode_also_checks_legacy_alias(self):
        self.assertEqual(self.run_check('--development'), 0)
        self.assertEqual(self.commands, [['dotnet', str(self.assembly)],
                                        ['dotnet', str(self.assembly), '--ui-preview']])
        self.assertEqual(self.process.terminate.call_count, 2)

    def test_existing_monitor_window_cannot_satisfy_new_launch(self):
        self.window_pid = 654
        with self.assertRaisesRegex(RuntimeError, 'Monitor window did not appear'):
            self.run_check(times=[0, 21])
        self.process.terminate.assert_called_once()

    def test_exit_is_reported_and_process_is_reaped(self):
        self.process.poll.return_value = 1
        self.process.returncode = 1
        with self.assertRaisesRegex(RuntimeError, 'Client exited: 1'):
            self.run_check(times=[0])
        self.process.terminate.assert_called_once()
        self.process.wait.assert_called_once_with(timeout=5)

    def test_process_that_does_not_terminate_is_killed(self):
        self.process.wait.side_effect = [subprocess.TimeoutExpired('dotnet', 5), 0]
        self.assertEqual(self.run_check(), 0)
        self.process.kill.assert_called_once()


if __name__ == '__main__':
    unittest.main()
