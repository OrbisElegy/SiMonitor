# SPDX-License-Identifier: AGPL-3.0-or-later
"""Regression checks for complete selected-tone PCM manifests."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import generate_beat_pitch_bank as pitch
import generate_monitor_tones as tones


class SelectedToneManifestTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.enterContext(patch.object(tones, 'ROOT', root))
        self.enterContext(patch.object(pitch, 'ROOT', root))
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))
        self.folder = root / 'src/Monitor.Infrastructure/Audio/SelectedTones'
        tones.generate(False)
        self.path = self.folder / 'manifest.json'

    def test_regeneration_preserves_complete_manifest_in_either_order(self):
        expected = self.path.read_bytes()
        for generate in (pitch.generate, tones.generate, tones.generate, pitch.generate):
            generate(False)
            self.assertEqual(expected, self.path.read_bytes())
        manifest = json.loads(expected)
        self.assertEqual(set(manifest['voices']), {p.stem for p in self.folder.glob('*.pcm')})
        for name, entry in manifest['voices'].items():
            data = (self.folder / (name + '.pcm')).read_bytes()
            self.assertEqual(entry['frames'] * 2, len(data))
            self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())
        self.assertEqual(manifest['voices']['HeartbeatPitchA']['frames'], 28 * 4800)
        tones.generate(True)
        pitch.generate(True)

    def test_missing_or_incorrect_bank_entry_is_rejected(self):
        original = json.loads(self.path.read_text(encoding='utf-8'))
        for defect in ('missing', 'frames', 'sha256'):
            with self.subTest(defect=defect):
                manifest = json.loads(json.dumps(original))
                entry = manifest['voices']['HeartbeatPitchA']
                if defect == 'missing':
                    del manifest['voices']['HeartbeatPitchA']
                else:
                    entry[defect] = 1 if defect == 'frames' else '0' * 64
                self.path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
                with self.assertRaises(AssertionError):
                    tones.generate(True)
                with self.assertRaisesRegex(SystemExit, 'manifest entry'):
                    pitch.generate(True)

    def test_corrupt_bank_pcm_is_rejected(self):
        path = self.folder / 'HeartbeatPitchA.pcm'
        data = bytearray(path.read_bytes())
        data[0] ^= 1
        path.write_bytes(data)
        with self.assertRaises(AssertionError):
            tones.generate(True)
        with self.assertRaisesRegex(SystemExit, 'differs'):
            pitch.generate(True)

    def test_unlisted_pcm_is_rejected(self):
        (self.folder / 'Unlisted.pcm').write_bytes(b'\0\0')
        with self.assertRaisesRegex(AssertionError, 'Unlisted PCM asset'):
            tones.generate(True)


if __name__ == '__main__':
    unittest.main()
