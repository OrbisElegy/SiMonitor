#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Exercise Desktop's native DLL copy/run rules with an isolated SDK project.

The fixture selects the Windows MSBuild branches on any host. Its dummy DLL
tests deployment only; it does not claim Windows loader or hardware coverage.
"""
import copy
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent.parent


@unittest.skipUnless(shutil.which('dotnet'), '.NET SDK is required')
class NativeAudioRunTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.project = self.root / 'src/Monitor.Desktop/Probe.csproj'
        self.project.parent.mkdir(parents=True)
        self.project.with_name('Program.cs').write_text(
            'System.Console.WriteLine("APP_STARTED");\n', encoding='utf-8')
        self.root.joinpath('NuGet.Config').write_text(
            '<configuration><packageSources><clear /></packageSources></configuration>',
            encoding='utf-8')
        project = ET.fromstring('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                                '<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>'
                                '</PropertyGroup></Project>')
        desktop = ET.parse(ROOT / 'src/Monitor.Desktop/Monitor.Desktop.csproj').getroot()
        for element in desktop:
            if (element.find('NativeAudioBinary') is not None
                    or element.get('Condition') == "'$(NativeAudioBinary)' != ''"
                    or element.get('Name') == 'RequireNativeAudioForRun'):
                node = copy.deepcopy(element)
                if 'Condition' in node.attrib:
                    node.set('Condition', node.get('Condition').replace(
                        "$([MSBuild]::IsOSPlatform('Windows'))", 'true'))
                project.append(node)
        ET.ElementTree(project).write(self.project, encoding='utf-8', xml_declaration=True)
        license_path = self.root / 'native/sim_audio_native/vendor/LICENSE.miniaudio'
        license_path.parent.mkdir(parents=True)
        license_path.write_text('fixture license', encoding='utf-8')
        result = self.dotnet('restore', str(self.project))
        self.assertEqual(result.returncode, 0, result.stdout)

    def dotnet(self, *arguments):
        return subprocess.run(['dotnet', *arguments], cwd=self.root,
                              env=os.environ | {'SIMONITOR_AUDIO_OUTPUT': ''},
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              encoding='utf-8', errors='replace', timeout=90)

    def test_missing_then_built_dll_is_copied_on_next_run(self):
        for layout in ('Release', '.'):
            with self.subTest(layout=layout):
                binary = self.root / 'artifacts/native-audio' / layout / 'sim_audio_native.dll'
                binary.parent.mkdir(parents=True, exist_ok=True)
                for configuration in ('Debug', 'Release'):
                    output = self.project.parent / 'bin' / configuration / 'net10.0/sim_audio_native.dll'
                    output.unlink(missing_ok=True)
                    command = ('run', '--project', str(self.project), '--no-restore',
                               '--configuration', configuration)
                    result = self.dotnet(*command)
                    self.assertNotEqual(result.returncode, 0, result.stdout)
                    self.assertIn(f'py -3 tools/build.py --configuration {configuration}', result.stdout)
                    self.assertNotIn('APP_STARTED', result.stdout)
                    binary.write_bytes(b'production DLL deployment fixture')
                    result = self.dotnet(*command)
                    self.assertEqual(result.returncode, 0, result.stdout)
                    self.assertIn('APP_STARTED', result.stdout)
                    self.assertEqual(output.read_bytes(), binary.read_bytes())
                    self.assertTrue(output.with_name('LICENSE.miniaudio').is_file())
                    binary.write_bytes(b'rebuilt production DLL deployment fixture')
                    timestamp = output.stat().st_mtime_ns + 2_000_000_000
                    os.utime(binary, ns=(timestamp, timestamp))
                    result = self.dotnet(*command)
                    self.assertEqual(result.returncode, 0, result.stdout)
                    self.assertEqual(output.read_bytes(), binary.read_bytes())
                    output.unlink()
                    result = self.dotnet(*command, '--no-build')
                    self.assertNotEqual(result.returncode, 0, result.stdout)
                    self.assertNotIn('APP_STARTED', result.stdout)
                    binary.unlink()


if __name__ == '__main__':
    unittest.main()
