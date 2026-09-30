#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Offline regression for candidate assembly and transfer integrity checks."""
import json
from pathlib import Path
import tempfile
import unittest

from desktop_distribution import MANIFEST, assemble, verify


class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        self.output = self.root / "output"
        self.output.mkdir()
        for name in ["LICENSE", "eng/dependencies.json", "docs/license-scope.md",
                     "docs/infirmary-source-notice.md", "eng/licenses/vendor.txt",
                     "native/sim_audio_native/vendor/LICENSE.miniaudio"]:
            path = self.source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("test material " + name)
        for name in ["Monitor.Desktop.exe", "Monitor.Desktop.dll", "Monitor.Desktop.runtimeconfig.json",
                     "style-previews.bin", "sim_audio_native.dll", "LICENSE.miniaudio"]:
            (self.output / name).write_bytes(b"fixture")

    def assemble(self):
        assemble(self.output, self.source, "win-x64", {"commit": "test", "workingTreeDirty": True})

    def test_round_trip_and_source_identity(self):
        self.assemble()
        result = verify(self.output)
        self.assertFalse(result["releaseAccepted"])
        self.assertTrue(result["source"]["workingTreeDirty"])
        self.assertIn("legal/eng/licenses/vendor.txt", result["files"])
        self.assertNotIn(MANIFEST, result["files"])
        self.assertEqual((self.output / "legal/LICENSE").read_bytes(), (self.source / "LICENSE").read_bytes())
        with self.assertRaises(ValueError):
            self.assemble()

    def test_changed_missing_or_extra_files_are_rejected(self):
        self.assemble()
        path = self.output / "style-previews.bin"
        path.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "changed=.*style-previews"):
            verify(self.output)
        path.unlink()
        with self.assertRaisesRegex(ValueError, "missing=.*style-previews"):
            verify(self.output)
        path.write_bytes(b"fixture")
        (self.output / "unexpected.txt").write_text("extra")
        with self.assertRaisesRegex(ValueError, "extra=.*unexpected"):
            verify(self.output)

    def test_missing_required_asset_does_not_write_metadata(self):
        (self.output / "sim_audio_native.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Missing published asset"):
            self.assemble()
        self.assertFalse((self.output / "legal").exists())
        self.assertFalse((self.output / MANIFEST).exists())

    def test_missing_legal_material_fails(self):
        (self.source / "LICENSE").unlink()
        with self.assertRaises(OSError):
            self.assemble()
        self.assertFalse((self.output / MANIFEST).exists())

    def test_symlink_rejected(self):
        try:
            (self.output / "linked").symlink_to(self.source, target_is_directory=True)
        except OSError:
            self.skipTest("symlinks unavailable")
        with self.assertRaisesRegex(ValueError, "Symbolic links"):
            self.assemble()

    def test_invalid_manifest_metadata_is_rejected(self):
        self.assemble()
        manifest = self.output / MANIFEST
        original = json.loads(manifest.read_text(encoding="utf-8"))
        missing_rid = dict(original)
        missing_rid.pop("rid")
        for defect, data in (("missing_rid", missing_rid),
                             ("unknown_schema", dict(original, schema="unknown")),
                             ("not_object", None)):
            with self.subTest(defect=defect):
                manifest.write_text(json.dumps(data))
                with self.assertRaisesRegex(ValueError, "Unsupported"):
                    verify(self.output)


if __name__ == "__main__":
    unittest.main()
