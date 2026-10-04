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
        self.runtime = self.root / "packages/microsoft.netcore.app.runtime.win-x64/10.0.12"
        self.runtime.mkdir(parents=True)
        (self.runtime / "LICENSE.TXT").write_text("runtime license", encoding="utf-8")
        (self.runtime / "THIRD-PARTY-NOTICES.TXT").write_text("runtime notices", encoding="utf-8")
        assets = self.source / "src/Monitor.Desktop/obj/project.assets.json"
        assets.parent.mkdir(parents=True)
        assets.write_text(json.dumps({"packageFolders": {str(self.root / "packages"): {}}}), encoding="utf-8")
        for name in ["LICENSE", "eng/dependencies.json", "docs/legal/license-scope.md",
                     "docs/legal/infirmary-source-notice.md", "eng/licenses/vendor.txt",
                     "native/sim_audio_native/vendor/LICENSE.miniaudio"]:
            path = self.source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("test material " + name, encoding="utf-8")
        for name in ["Monitor.Desktop.exe", "Monitor.Desktop.dll", "Monitor.Desktop.runtimeconfig.json",
                     "style-previews.bin", "sim_audio_native.dll", "LICENSE.miniaudio"]:
            (self.output / name).write_bytes(b"fixture")
        (self.output / "Monitor.Desktop.deps.json").write_text(json.dumps({"libraries": {
            "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12": {"type": "runtimepack"}}}), encoding="utf-8")

    def assemble(self):
        assemble(self.output, self.source, "win-x64", {"commit": "test", "workingTreeDirty": True})

    def test_round_trip_and_source_identity(self):
        self.assemble()
        result = verify(self.output)
        self.assertFalse(result["releaseAccepted"])
        self.assertTrue(result["source"]["workingTreeDirty"])
        self.assertIn("legal/eng/licenses/vendor.txt", result["files"])
        self.assertIn("legal/runtime/runtimepack.Microsoft.NETCore.App.Runtime.win-x64-10.0.12/THIRD-PARTY-NOTICES.TXT", result["files"])
        self.assertNotIn(MANIFEST, result["files"])
        self.assertEqual((self.output / "legal/LICENSE").read_bytes(), (self.source / "LICENSE").read_bytes())
        with self.assertRaises(ValueError):
            self.assemble()

    def test_mutation_missing_and_extra(self):
        self.assemble()
        path = self.output / "style-previews.bin"
        path.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "changed=.*style-previews"):
            verify(self.output)
        path.unlink()
        with self.assertRaisesRegex(ValueError, "missing=.*style-previews"):
            verify(self.output)
        path.write_bytes(b"fixture")
        (self.output / "unexpected.txt").write_text("extra", encoding="utf-8")
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

    def test_missing_runtime_notice_fails_before_writing_metadata(self):
        (self.runtime / "THIRD-PARTY-NOTICES.TXT").unlink()
        with self.assertRaisesRegex(ValueError, "Missing license or notices"):
            self.assemble()
        self.assertFalse((self.output / "legal").exists())

    def test_symlink_rejected(self):
        try:
            (self.output / "linked").symlink_to(self.source, target_is_directory=True)
        except OSError:
            self.skipTest("symlinks unavailable")
        with self.assertRaisesRegex(ValueError, "Symbolic links"):
            self.assemble()

    def test_manifest_damage(self):
        self.assemble()
        manifest = self.output / MANIFEST
        data = json.loads(manifest.read_text(encoding="utf-8"))
        data.pop("rid")
        manifest.write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unsupported"):
            verify(self.output)
        data["schema"] = "unknown"
        manifest.write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unsupported"):
            verify(self.output)
        manifest.write_text("null", encoding="utf-8")
        with self.assertRaises(ValueError):
            verify(self.output)


if __name__ == "__main__":
    unittest.main()
