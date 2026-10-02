# SPDX-License-Identifier: AGPL-3.0-or-later
"""SPDX coverage checks must preserve syntax and existing licensing scope."""
import tempfile
from pathlib import Path
import unittest

from spdx_headers import ADAPTED_LICENSE, TAG, check_or_write, exempt, with_header


class SpdxHeaderTests(unittest.TestCase):
    def test_shebang_encoding_and_xml_declaration_stay_first(self):
        cases = [
            ('tool.py', b'#!/usr/bin/env python3\n# coding: utf-8\nprint(1)\n', 2),
            ('NuGet.Config', b'<?xml version="1.0"?>\n<configuration/>\n', 1),
            ('sample.cs', b'\xef\xbb\xbfnamespace Example;\r\n', 0),
        ]
        for path, data, position in cases:
            with self.subTest(path=path):
                result = with_header(path, data)
                self.assertIn(TAG.encode(), result.splitlines()[position])
                self.assertEqual(result, with_header(path, result))
                if path.endswith('.cs'):
                    self.assertTrue(result.startswith(b'\xef\xbb\xbf'))
                else:
                    self.assertEqual(data.splitlines()[:position], result.splitlines()[:position])

    def test_non_code_and_upstream_materials_are_out_of_scope(self):
        for path, data in [('README.md', b'# Documentation\n'), ('model.json', b'{"value": 3}\n'),
                           ('sample.pcm', b'\0\xff'), ('LICENSE', b'Exact license text\n'),
                           ('.gitignore', b'artifacts/\n'), ('.editorconfig', b'root = true\n'),
                           ('eng/licenses/upstream.txt', b'Upstream\r\n'),
                           ('native/sim_audio_native/vendor/upstream.h', b'/* upstream */\n')]:
            self.assertTrue(exempt(path), path)
            self.assertEqual(with_header(path, data), data)

    def test_workflows_and_javascript_require_headers_but_issue_forms_do_not(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for path, data in (('.github/workflows/ci.yml', b'name: CI\n'),
                               ('.github/workflows/check.yaml', b'name: Check\n'),
                               ('.github/tests/triage.cjs', b'const value = 1;\n'),
                               ('tools/check.mjs', b'export const value = 1;\n'),
                               ('tools/check.js', b'const value = 1;\n')):
                with self.subTest(path=path):
                    target = root / path
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
                    self.assertFalse(exempt(path))
                    with self.assertRaisesRegex(ValueError, 'Missing SPDX header'):
                        check_or_write(root, [path], True)
                    target.write_bytes(with_header(path, data))
                    self.assertEqual(check_or_write(root, [path], True), 1)
        for path in ('.github/ISSUE_TEMPLATE/bug.yml', '.github/config.yaml', 'settings.yml'):
            self.assertTrue(exempt(path), path)

    def test_adapted_tables_preserve_both_license_terms(self):
        data = b'// Adapted from Infirmary Integrated\n// Apache-2.0; see license\nclass Table {}\n'
        self.assertIn(ADAPTED_LICENSE.encode(), with_header('Table.cs', data).splitlines()[0])

    def test_existing_historical_native_license_is_preserved(self):
        data = b'/* ' + TAG.encode() + b'AGPL-3.0-only */\nint value;\n'
        self.assertEqual(with_header('native/sim_audio_native/sim_audio.c', data), data)

    def test_check_rejects_missing_or_wrong_headers(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'a.cs').write_text('class A {}\n', encoding='utf-8')
            (root / 'a.json').write_text('{}\n', encoding='utf-8')
            paths = ['a.cs', 'a.json']
            with self.assertRaisesRegex(ValueError, 'Missing'):
                check_or_write(root, paths, True)
            check_or_write(root, paths, False)
            self.assertEqual(check_or_write(root, paths, True), 1)
            self.assertEqual((root / 'a.json').read_text(encoding='utf-8'), '{}\n')
            self.assertFalse((root / 'a.json.license').exists())
            (root / 'a.cs').write_text('// ' + TAG + 'MIT\nclass A {}\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Unexpected'):
                check_or_write(root, paths, True)


if __name__ == '__main__':
    unittest.main()
