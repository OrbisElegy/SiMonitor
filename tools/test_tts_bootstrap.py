#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Exercise cold TTS restoration, corrupt downloads and read-only operation."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import fetch_dependencies
import fetch_tts_models as fetch


class Response(io.BytesIO):
    def geturl(self):
        return 'https://example.invalid/model'


class TtsBootstrapTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        (self.root / 'eng/audio').mkdir(parents=True)
        self.cache = self.root / 'cache'
        self.data = b'known model bytes'
        self.member = dict(path='voice/model.onnx', sha256=hashlib.sha256(self.data).hexdigest(), maximum_bytes=len(self.data))
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, 'w') as archive:
            archive.writestr(self.member['path'], self.data)
        self.blob = buffer.getvalue()
        self.item = dict(id='voice', url='https://example.invalid/model', format='zip',
                         sha256=hashlib.sha256(self.blob).hexdigest(), maximum_bytes=len(self.blob), members=[self.member])
        self.save()

    def save(self):
        (self.root / 'eng/audio/tts-models.json').write_text(json.dumps(dict(archives=[self.item], files=[])), encoding='utf-8')

    def run_fetch(self, **kwargs):
        return fetch.fetch_models(self.root, self.cache, **kwargs)

    def test_cold_restore_and_no_archive_duplication(self):
        self.run_fetch(opener=lambda *a, **kw: Response(self.blob))
        self.assertEqual((self.cache / self.member['path']).read_bytes(), self.data)
        self.assertFalse(list(self.cache.glob('.tts-download-*')))
        self.run_fetch(offline=True, opener=lambda *a, **kw: self.fail('network'))
        self.run_fetch(check=True, opener=lambda *a, **kw: self.fail('network'))

    def test_corrupt_download_preserves_existing_model(self):
        target = self.cache / self.member['path']
        target.parent.mkdir(parents=True)
        target.write_bytes(b'existing')
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            self.run_fetch(opener=lambda *a, **kw: Response(b'bad'))
        self.assertEqual(target.read_bytes(), b'existing')
        self.assertFalse(list(self.cache.glob('.tts-download-*')))

    def test_member_hash_checked_before_replacement(self):
        self.member['sha256'] = '0' * 64
        self.save()
        with self.assertRaisesRegex(ValueError, 'member SHA-256'):
            self.run_fetch(opener=lambda *a, **kw: Response(self.blob))
        self.assertFalse((self.cache / self.member['path']).exists())

    def test_missing_check_and_offline_do_not_write(self):
        for option in ['offline', 'check']:
            with self.assertRaisesRegex(ValueError, 'missing or corrupt'):
                self.run_fetch(**{option: True}, opener=lambda *a, **kw: self.fail('network'))
            self.assertFalse(self.cache.exists())

    def test_traversal_rejected_before_download(self):
        self.member['path'] = '../escape'
        self.save()
        with self.assertRaisesRegex(ValueError, 'Unsafe'):
            self.run_fetch(opener=lambda *a, **kw: self.fail('network'))

    def test_tar_link_rejected(self):
        buffer = io.BytesIO()
        with tarfile.open(fileobj=buffer, mode='w:bz2') as archive:
            info = tarfile.TarInfo(self.member['path'])
            info.type = tarfile.SYMTYPE
            info.linkname = '/tmp/escape'
            archive.addfile(info)
        blob = buffer.getvalue()
        self.item.update(format='tar.bz2', sha256=hashlib.sha256(blob).hexdigest(), maximum_bytes=len(blob))
        self.save()
        with self.assertRaisesRegex(ValueError, 'regular file'):
            self.run_fetch(opener=lambda *a, **kw: Response(blob))

    def test_oversized_response_rejected(self):
        with self.assertRaisesRegex(ValueError, 'size limit'):
            self.run_fetch(opener=lambda *a, **kw: Response(self.blob + b'extra'))

    def test_missing_python_offline_never_installs(self):
        with patch.object(fetch, 'requirements', return_value={'numpy': '2.2.6'}), \
                patch.object(fetch.subprocess, 'run', side_effect=AssertionError('install')):
            with self.assertRaisesRegex(ValueError, 'Python environment'):
                fetch.prepare_python(self.root, self.cache, offline=True)
        self.assertFalse(self.cache.exists())

    def test_existing_entrypoint_tts_only_skips_native_and_nuget(self):
        with patch('sys.argv', ['fetch_dependencies.py', '--tts-only', '--check']), \
                patch.object(fetch, 'prepare') as prepare, \
                patch.object(fetch_dependencies, 'fetch_native', side_effect=AssertionError('native')), \
                patch.object(fetch_dependencies, 'restore_managed', side_effect=AssertionError('NuGet')):
            self.assertEqual(fetch_dependencies.main(), 0)
            self.assertTrue(prepare.call_args.kwargs['check'])


if __name__ == '__main__':
    unittest.main()
