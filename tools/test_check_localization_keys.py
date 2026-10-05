# SPDX-License-Identifier: AGPL-3.0-or-later
"""Only literals in catalog namespaces count as localization keys."""
from pathlib import Path
import tempfile
import unittest

import check_localization_keys as checker


class ReferencedKeyTests(unittest.TestCase):
    def test_namespaced_dotted_literals_are_reported_with_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'Sample.cs'
            source.write_text('Bind(x, "sound.pause");\nvar r = "Monitor.Help.Topics";\nGet("other.key"); Get("sound.resume");\n'
                              'var path = "sound.json";\n', encoding='utf-8')
            found = [(number, key) for _, number, key in checker.referenced_keys([source], {'sound'})]
        self.assertEqual(found, [(1, 'sound.pause'), (3, 'sound.resume')])


if __name__ == '__main__':
    unittest.main()
