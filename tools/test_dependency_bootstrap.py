#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline tests for dependency admission, cache recovery and build orchestration."""
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import build
import build_native_audio
import fetch_dependencies as fetch


class Response(io.BytesIO):
    def geturl(self):
        return 'https://example.invalid/pinned/header.h'


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'eng').mkdir()
        self.data = b'pinned dependency\n'
        self.item = {'path': 'vendor/header.h', 'sha256': hashlib.sha256(self.data).hexdigest(),
                     'url': 'https://example.invalid/pinned/header.h', 'maximum_bytes': 100}
        self.write_manifest()

    def write_manifest(self):
        (self.root / 'eng/dependencies.json').write_text(json.dumps({'native_dependencies': [{'source_files': [self.item]}]}), encoding='utf-8')

    def prepare(self, **kwargs):
        fetch.fetch_native(self.root, **kwargs)

    def test_download_and_offline_cache_recovery(self):
        self.prepare(opener=lambda *a, **kw: Response(self.data))
        target = self.root / self.item['path']
        self.assertEqual(target.read_bytes(), self.data)
        target.unlink()
        with patch.object(fetch.urllib.request, 'urlopen', side_effect=AssertionError('network')):
            self.prepare(offline=True)
        self.assertEqual(target.read_bytes(), self.data)
        self.prepare(check=True)

    def test_bad_hash_preserves_existing_file(self):
        target = self.root / self.item['path']
        target.parent.mkdir()
        target.write_bytes(b'local modification')
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            self.prepare(opener=lambda *a, **kw: Response(b'wrong download'))
        self.assertEqual(target.read_bytes(), b'local modification')
        self.assertFalse(list((self.root / '.cache/downloads').glob('download-*')))

    def test_oversized_download_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'size limit'):
            self.prepare(opener=lambda *a, **kw: Response(b'x' * 101))
        self.assertFalse((self.root / self.item['path']).exists())

    def test_offline_missing_and_corrupt_cache_reject(self):
        with self.assertRaisesRegex(ValueError, 'Offline cache'):
            self.prepare(offline=True)
        cache = self.root / '.cache/downloads'
        cache.mkdir(parents=True)
        (cache / self.item['sha256']).write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'Offline cache'):
            self.prepare(offline=True)

    def test_check_never_downloads_or_writes(self):
        with self.assertRaisesRegex(ValueError, 'Missing or changed'):
            self.prepare(check=True, opener=lambda *a, **kw: self.fail('network'))
        self.assertFalse((self.root / '.cache').exists())

    def test_manifest_cannot_escape_root(self):
        self.item['path'] = '../header.h'
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, 'destination'):
            self.prepare()

    def test_valid_source_does_not_need_network(self):
        target = self.root / self.item['path']
        target.parent.mkdir()
        target.write_bytes(self.data)
        self.prepare(opener=lambda *a, **kw: self.fail('network'))

    def test_offline_nuget_has_no_feeds_and_is_locked(self):
        def run(command, **kwargs):
            self.assertIn('--locked-mode', command)
            config = Path(command[command.index('--configfile') + 1])
            self.assertIn('<clear />', config.read_text(encoding='utf-8'))
            self.assertNotIn('http', config.read_text(encoding='utf-8'))
        with patch.object(fetch.subprocess, 'run', side_effect=run):
            fetch.restore_managed(self.root, offline=True)

    def test_build_prepares_before_native_and_managed(self):
        calls = []
        with patch('sys.argv', ['build.py', '--offline', '--jobs', '7']), \
                patch.object(build.shutil, 'which', return_value='/tool'), \
                patch.object(build, 'fetch_native', side_effect=lambda **kw: calls.append(('fetch', kw))), \
                patch.object(build, 'restore_managed', side_effect=lambda **kw: calls.append(('restore', kw))), \
                patch.object(build.subprocess, 'run', side_effect=lambda cmd, **kw: calls.append(('run', cmd))):
            self.assertEqual(build.main(), 0)
        self.assertEqual([x[0] for x in calls[:2]], ['fetch', 'restore'])
        self.assertTrue(calls[0][1]['offline'])
        self.assertIn('--production-only', calls[3][1])
        self.assertIn('--no-restore', calls[4][1])
        self.assertIn('-m:7', calls[4][1])

    def test_native_test_build_runs_ctest_but_production_only_does_not(self):
        binary_name = ('sim_audio_native.dll' if sys.platform == 'win32' else
                       'libsim_audio_native.dylib' if sys.platform == 'darwin' else 'libsim_audio_native.so')
        for name in ('native-audio', 'native-audio-test'):
            folder = self.root / 'artifacts' / name
            folder.mkdir(parents=True)
            (folder / binary_name).write_bytes(b'fixture')
        test_folder = self.root / 'artifacts/native-audio-test'
        ctest = ['ctest', '--test-dir', str(test_folder), '--build-config', 'Release',
                 '--output-on-failure', '--no-tests=error']
        for production_only in (False, True):
            with self.subTest(production_only=production_only):
                calls = []
                args = ['build_native_audio.py', '--jobs', '1'] + (['--production-only'] if production_only else [])
                with patch('sys.argv', args), \
                        patch.object(build_native_audio, '__file__', str(self.root / 'tools/build_native_audio.py')), \
                        patch.object(build_native_audio, 'fetch_native'), \
                        patch.object(build_native_audio.subprocess, 'run', side_effect=lambda cmd, **kw: calls.append(cmd)):
                    build_native_audio.main()
                self.assertEqual([cmd for cmd in calls if cmd[0] == 'ctest'], [] if production_only else [ctest])
                if not production_only:
                    test_configure = next(cmd for cmd in calls
                                          if cmd[:2] == ['cmake', '-S'] and str(test_folder) in cmd)
                    self.assertIn('-DSIM_AUDIO_TEST=ON', test_configure)
                    self.assertIn('-DBUILD_TESTING=ON', test_configure)
                    test_build = ['cmake', '--build', str(test_folder), '--config', 'Release', '--parallel', '1']
                    self.assertGreater(calls.index(ctest), calls.index(test_build))

    def test_product_build_uses_prebuilt_assets_and_replaces_output(self):
        catalog = self.root / 'src/Monitor.Desktop/bin/Release/net10.0/style-previews.bin'
        catalog.parent.mkdir(parents=True)
        catalog.write_bytes(b'catalog')
        output = self.root / 'artifacts/release'
        output.mkdir(parents=True)
        (output / 'old-development-file').write_text('old', encoding='utf-8')
        calls = []
        def run(command, **kwargs):
            calls.append(command)
            if '-o' in command:
                stage = Path(command[command.index('-o') + 1])
                stage.mkdir()
                (stage / 'Monitor.Desktop.dll').write_bytes(b'product')
                (stage / 'style-previews.bin').write_bytes(b'catalog')
        with patch.object(build, 'ROOT', self.root), patch.object(build.subprocess, 'run', side_effect=run):
            build.build_product('Release', 8)
        self.assertIn('-p:ProductRelease=false', calls[0])
        self.assertIn('-p:ProductRelease=true', calls[1])
        self.assertIn('-p:UsePrebuiltStylePreviews=true', calls[1])
        self.assertFalse((output / 'old-development-file').exists())
        self.assertEqual((output / 'Monitor.Desktop.dll').read_bytes(), b'product')

    def test_failed_product_build_preserves_previous_output(self):
        catalog = self.root / 'src/Monitor.Desktop/bin/Release/net10.0/style-previews.bin'
        catalog.parent.mkdir(parents=True)
        catalog.write_bytes(b'catalog')
        output = self.root / 'artifacts/release'
        output.mkdir(parents=True)
        (output / 'Monitor.Desktop.dll').write_bytes(b'previous')
        def run(command, **kwargs):
            if '-o' in command:
                raise build.subprocess.CalledProcessError(1, command)
        with patch.object(build, 'ROOT', self.root), patch.object(build.subprocess, 'run', side_effect=run):
            with self.assertRaises(build.subprocess.CalledProcessError):
                build.build_product('Release', 8)
        self.assertEqual((output / 'Monitor.Desktop.dll').read_bytes(), b'previous')


if __name__ == '__main__':
    unittest.main()
