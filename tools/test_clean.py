#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Safety and scope checks for repository-local cleanup."""

from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import clean


class CleanTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.make_file('src/Monitor.Desktop/bin/Release/app.dll')
        self.make_file('src/Monitor.Desktop/obj/project.assets.json')
        self.make_file('tests/Monitor.Specs/TestResults/result.trx')
        self.make_file('tools/__pycache__/tool.cpython-312.pyc')
        self.make_file('tools/loose.pyc')
        self.make_file('reports/result.trx')
        self.make_file('eng/generated/obj/cache.dat')
        self.make_file('artifacts/release/Monitor.Desktop.dll')
        self.make_file('.cache/downloads/pinned')
        self.make_file('native/sim_audio_native/vendor/miniaudio.h')
        self.make_file('README.md')

    def make_file(self, relative):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b'fixture')

    def test_clean_and_dirclean_have_distinct_scopes(self):
        clean.run(self.root, 'clean')
        for relative in ('src/Monitor.Desktop/bin', 'src/Monitor.Desktop/obj',
                         'tests/Monitor.Specs/TestResults', 'tools/__pycache__',
                         'tools/loose.pyc', 'reports/result.trx',
                         'eng/generated/obj', 'artifacts'):
            self.assertFalse((self.root / relative).exists(), relative)
        for relative in ('.cache/downloads/pinned',
                         'native/sim_audio_native/vendor/miniaudio.h', 'README.md'):
            self.assertTrue((self.root / relative).exists(), relative)
        clean.run(self.root, 'dirclean')
        self.assertFalse((self.root / '.cache').exists())
        self.assertFalse((self.root / 'native/sim_audio_native/vendor/miniaudio.h').exists())
        self.assertTrue((self.root / 'README.md').exists())

    def test_dry_run_does_not_remove_outputs(self):
        clean.run(self.root, 'dirclean', dry_run=True)
        self.assertTrue((self.root / 'artifacts/release/Monitor.Desktop.dll').exists())
        self.assertTrue((self.root / '.cache/downloads/pinned').exists())

    def test_symlink_target_and_tracked_content_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            outside = Path(directory)
            (self.root / 'artifacts').rename(outside / 'saved-artifacts')
            (self.root / 'artifacts').symlink_to(outside / 'saved-artifacts', target_is_directory=True)
            with self.assertRaisesRegex(ValueError, 'Unsafe cleanup target'):
                clean.run(self.root, 'clean')
            self.assertTrue((outside / 'saved-artifacts/release/Monitor.Desktop.dll').exists())
        (self.root / 'artifacts').unlink()
        self.make_file('artifacts/keep.txt')
        with patch.object(clean.subprocess, 'check_output', return_value=b'artifacts/keep.txt\0'):
            (self.root / '.git').mkdir()
            with self.assertRaisesRegex(ValueError, 'contains tracked files'):
                clean.run(self.root, 'clean')
        self.assertTrue((self.root / 'src/Monitor.Desktop/bin').exists())

    def test_dirclean_rejects_symlinked_header_parent(self):
        with tempfile.TemporaryDirectory() as directory:
            outside = Path(directory)
            vendor = self.root / 'native/sim_audio_native/vendor'
            vendor.rename(outside / 'vendor')
            vendor.symlink_to(outside / 'vendor', target_is_directory=True)
            with self.assertRaisesRegex(ValueError, 'Unsafe cleanup target'):
                clean.run(self.root, 'dirclean')
            self.assertTrue((outside / 'vendor/miniaudio.h').exists())
            self.assertTrue((self.root / 'artifacts/release/Monitor.Desktop.dll').exists())


if __name__ == '__main__':
    unittest.main()
