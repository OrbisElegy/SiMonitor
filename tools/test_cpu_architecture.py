#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Verify CPU kernel source selection without restoring or executing a target."""

import json
from pathlib import Path
import subprocess
import unittest


ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / 'src/Monitor.Infrastructure/Monitor.Infrastructure.csproj'


class CpuArchitectureTests(unittest.TestCase):
    def selection(self, *properties):
        result = subprocess.run(
            ['dotnet', 'msbuild', str(PROJECT), '-getProperty:MonitorKernelArchitecture',
             '-getItem:Compile', *properties], cwd=ROOT, check=True, capture_output=True, text=True,
            encoding='utf-8')
        data = json.loads(result.stdout)
        files = [item['Identity'] for item in data['Items']['Compile']]
        return data['Properties']['MonitorKernelArchitecture'], files

    def test_rid_wins_over_host(self):
        for rid, architecture in [('linux-x64', 'x64'), ('win-x64', 'x64'),
                                  ('linux-arm64', 'arm64'), ('osx-arm64', 'arm64'),
                                  ('win-arm64', 'arm64'), ('win-x86', 'scalar')]:
            with self.subTest(rid=rid):
                selected, files = self.selection(f'-p:RuntimeIdentifier={rid}')
                self.assertEqual(selected, architecture)
                self.assertEqual(any(name.endswith('.X64.cs') for name in files), architecture == 'x64')
                self.assertEqual(any(name.endswith('.Arm64.cs') for name in files), architecture == 'arm64')

    def test_portable_scalar_excludes_both(self):
        selected, files = self.selection('-p:MonitorKernelArchitecture=scalar')
        self.assertEqual(selected, 'scalar')
        self.assertFalse(any(name.endswith(('.X64.cs', '.Arm64.cs')) for name in files))

    def test_explicit_platform_without_rid(self):
        selected, files = self.selection('-p:PlatformTarget=ARM64')
        self.assertEqual(selected, 'arm64')
        self.assertFalse(any(name.endswith('.X64.cs') for name in files))


if __name__ == '__main__':
    unittest.main()
