#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline tests for dependency admission, cache recovery and build orchestration."""
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import build
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
        (self.root / 'eng/dependencies.json').write_text(json.dumps({'native_dependencies': [{'source_files': [self.item]}]}))

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
            self.assertIn('<clear />', config.read_text())
            self.assertNotIn('http', config.read_text())
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


if __name__ == '__main__':
    unittest.main()
