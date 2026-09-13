#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline regression checks for adapted-source dependency coverage."""
import contextlib
import copy
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import verify_dependency_ledger as verifier


class SourceAdaptationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.license = self.root / 'eng/licenses/adapted.txt'
        self.license.parent.mkdir(parents=True)
        self.license.write_text('Bundled test license\n')
        self.notice = self.root / 'docs/adapted-notice.md'
        self.notice.parent.mkdir()
        self.notice.write_text('Upstream author attribution and local modifications.\n')
        self.relative = 'eng/physiology/adapted.json'
        self.path = self.root / self.relative
        self.path.parent.mkdir()
        self.manifest = {
            'upstream_commit': 'a' * 40,
            'source_path': 'upstream/waveforms.cs',
            'source_sha256': 'b' * 64,
            'license': 'Apache-2.0',
            'license_file': 'eng/licenses/adapted.txt',
            'license_sha256': hashlib.sha256(self.license.read_bytes()).hexdigest(),
        }
        self.write_manifest()
        self.entry = {
            'id': 'Adapted test source',
            'commit': self.manifest['upstream_commit'],
            'source': 'https://example.invalid/upstream/tree/' + 'a' * 40,
            'source_path': self.manifest['source_path'],
            'source_sha256': self.manifest['source_sha256'],
            'license': self.manifest['license'],
            'license_file': self.manifest['license_file'],
            'license_sha256': self.manifest['license_sha256'],
            'notice_file': 'docs/adapted-notice.md',
            'manifests': [self.relative],
        }
        self.ledger = {'source_adaptations': [self.entry]}

    def write_manifest(self):
        self.path.write_text(json.dumps(self.manifest))

    def verify(self):
        with contextlib.redirect_stdout(io.StringIO()):
            verifier.verify_source_adaptations(self.root, self.ledger)

    def test_complete_committed_evidence_passes_without_upstream_checkout(self):
        self.verify()

    def test_missing_entry_or_entire_section_is_rejected(self):
        for ledger in ({}, {'source_adaptations': []}):
            with self.subTest(ledger=ledger):
                self.ledger = ledger
                with self.assertRaisesRegex(ValueError, 'Unregistered source adaptations'):
                    self.verify()

    def test_new_unregistered_adaptation_is_rejected(self):
        (self.path.parent / 'new-adaptation.json').write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, 'Unregistered.*new-adaptation.json'):
            self.verify()

    def test_missing_registered_manifest_is_rejected(self):
        self.path.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing adaptation manifest'):
            self.verify()

    def test_missing_license_is_rejected(self):
        self.license.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing adaptation license file'):
            self.verify()

    def test_changed_license_bytes_are_rejected(self):
        self.license.write_text('Different license\n')
        with self.assertRaisesRegex(ValueError, 'license hash mismatch'):
            self.verify()

    def test_missing_or_empty_attribution_is_rejected(self):
        for contents in (None, ' \n'):
            with self.subTest(contents=contents):
                if contents is None:
                    self.notice.unlink()
                else:
                    self.notice.write_text(contents)
                with self.assertRaisesRegex(ValueError, 'attribution notice'):
                    self.verify()

    def test_manifest_provenance_must_match_ledger(self):
        for field in ('upstream_commit', 'source_path', 'source_sha256',
                      'license', 'license_file', 'license_sha256'):
            with self.subTest(field=field):
                changed = dict(self.manifest, **{field: 'different'})
                self.path.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError, 'mismatch'):
                    self.verify()
        self.write_manifest()

    def test_license_identifier_can_be_inherited_from_ledger(self):
        del self.manifest['license']
        self.write_manifest()
        self.verify()

    def test_missing_commit_cannot_hide_registered_manifest(self):
        del self.manifest['upstream_commit']
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, 'Missing adaptation manifest or upstream_commit'):
            self.verify()

    def test_duplicate_dependency_or_manifest_is_rejected(self):
        for duplicate_id in (True, False):
            with self.subTest(duplicate_id=duplicate_id):
                duplicate = copy.deepcopy(self.entry)
                if not duplicate_id:
                    duplicate['id'] = 'Other dependency'
                self.ledger['source_adaptations'] = [self.entry, duplicate]
                with self.assertRaisesRegex(ValueError, 'duplicate'):
                    self.verify()

    def test_missing_required_evidence_is_rejected(self):
        for field in ('source', 'source_path', 'license', 'license_file', 'notice_file',
                      'commit', 'source_sha256', 'license_sha256', 'manifests'):
            with self.subTest(field=field):
                entry = copy.deepcopy(self.entry)
                del entry[field]
                self.ledger['source_adaptations'] = [entry]
                with self.assertRaisesRegex(ValueError, 'Missing|Invalid'):
                    self.verify()

    def test_unpinned_commit_or_malformed_hash_is_rejected(self):
        for field, value in (('commit', 'main'), ('source_sha256', 'not-a-hash'),
                             ('license_sha256', 'z' * 64)):
            with self.subTest(field=field):
                entry = dict(self.entry, **{field: value})
                self.ledger['source_adaptations'] = [entry]
                with self.assertRaisesRegex(ValueError, 'Invalid adaptation'):
                    self.verify()

    def test_empty_or_missing_section_is_valid_without_adaptations(self):
        self.path.unlink()
        for ledger in ({}, {'source_adaptations': []}):
            self.ledger = ledger
            self.verify()

    def test_main_runs_adaptation_check(self):
        package = {'id': 'Test.Package', 'version': '1.0', 'license': 'Apache-2.0',
                   'license_file': self.entry['license_file'],
                   'content_hash_sha512_base64': 'test-content-hash'}
        self.ledger.update(direct_packages=[package], transitive_packages=[], native_dependencies=[])
        lock = self.root / 'src/Test/packages.lock.json'
        lock.parent.mkdir(parents=True)
        lock.write_text(json.dumps({'dependencies': {'net10.0': {'Test.Package': {
            'type': 'Direct', 'resolved': '1.0', 'contentHash': 'test-content-hash'}}}}))
        ledger_path = self.root / 'eng/dependencies.json'
        with patch.object(verifier, '__file__', str(self.root / 'tools/verify_dependency_ledger.py')):
            ledger_path.write_text(json.dumps(self.ledger))
            with contextlib.redirect_stdout(io.StringIO()):
                verifier.main()
            self.ledger['source_adaptations'] = []
            ledger_path.write_text(json.dumps(self.ledger))
            with self.assertRaisesRegex(ValueError, 'Unregistered source adaptations'):
                verifier.main()


if __name__ == '__main__':
    unittest.main()
