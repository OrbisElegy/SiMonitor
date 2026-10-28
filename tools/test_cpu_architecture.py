#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Verify CPU source selection and project-reference RID flow without restore."""

import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import quoteattr


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

    def test_project_reference_preserves_target_rid_and_sources(self):
        with tempfile.TemporaryDirectory(prefix='monitor-cpu-references-') as directory:
            temporary = Path(directory)
            probe = temporary / 'Probe.csproj'
            probe.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include={quoteattr(str(PROJECT))} />
  </ItemGroup>
</Project>
''', encoding='utf-8')
            targets = temporary / 'Probe.targets'
            targets.write_text('''<Project>
  <Target Name="ReportKernelSelection" Returns="@(_KernelSelection)">
    <ItemGroup>
      <_KernelSelection Include="$(MSBuildProjectName)">
        <Architecture>$(MonitorKernelArchitecture)</Architecture>
        <Rid>$(RuntimeIdentifier)</Rid>
        <Sources>@(Compile)</Sources>
        <ProjectPath>$(MSBuildProjectFullPath)</ProjectPath>
      </_KernelSelection>
    </ItemGroup>
  </Target>
</Project>
''', encoding='utf-8')
            for rid, architecture in [('linux-arm64', 'arm64'), ('win-arm64', 'arm64'),
                                      ('linux-x64', 'x64')]:
                with self.subTest(rid=rid):
                    # Run the SDK's real reference negotiation and MSBuild dispatch,
                    # returning source metadata instead of compiling or running target code.
                    result = subprocess.run(
                        ['dotnet', 'msbuild', str(probe), '-t:ResolveProjectReferences',
                         '-getItem:_ResolvedProjectReferencePaths', f'-p:RuntimeIdentifier={rid}',
                         f'-p:CustomAfterMicrosoftCommonTargets={targets}',
                         '-p:SkipResolvePackageAssets=true',
                         '-p:ProjectReferenceBuildTargets=ReportKernelSelection'],
                        cwd=ROOT, check=True, capture_output=True, text=True, encoding='utf-8')
                    references = json.loads(result.stdout)['Items']['_ResolvedProjectReferencePaths']
                    self.assertEqual(len(references), 1)
                    reference = references[0]
                    self.assertEqual(Path(reference['ProjectPath']), PROJECT)
                    self.assertEqual(reference['Rid'], rid)
                    self.assertEqual(reference['Architecture'], architecture)
                    files = reference['Sources'].split(';')
                    self.assertEqual(any(name.endswith('.X64.cs') for name in files), architecture == 'x64')
                    self.assertEqual(any(name.endswith('.Arm64.cs') for name in files), architecture == 'arm64')


if __name__ == '__main__':
    unittest.main()
