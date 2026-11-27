# SPDX-License-Identifier: AGPL-3.0-or-later
"""Only literals in catalog namespaces count as localization keys."""
from pathlib import Path
import tempfile
import unittest

import check_localization_keys as checker


class ReferencedKeyTests(unittest.TestCase):
    def test_wire_ids_are_recognized_only_in_network_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            network = Path(directory) / 'Monitor.Network'
            network.mkdir()
            transport = network / 'Transport.cs'
            desktop = Path(directory) / 'View.cs'
            transport.write_text('Send("nibp.measurement.start"); Send("alarm.episode.changed");\n'
                                 'Get("display.settings"); Get("display.missing");\n', encoding='utf-8')
            desktop.write_text('Get("nibp.measurement.start");\n', encoding='utf-8')
            catalog = {'display.title', 'nibp.title', 'alarm.title'}
            missing = list(checker.missing_keys([transport, desktop], catalog, network, checker.classroom_wire_ids()))
        self.assertEqual(missing, [(transport, 2, 'display.missing'), (desktop, 1, 'nibp.measurement.start')])

    def test_namespaced_dotted_literals_are_reported_with_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'Sample.cs'
            source.write_text('Bind(x, "sound.pause");\nvar r = "Monitor.Help.Topics";\nGet("other.key"); Get("sound.resume");\n'
                              'var path = "sound.json";\n', encoding='utf-8')
            found = [(number, key) for _, number, key in checker.referenced_keys([source], {'sound'})]
        self.assertEqual(found, [(1, 'sound.pause'), (3, 'sound.resume')])

    def test_concatenated_fragments_are_not_complete_keys(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'Sample.cs'
            source.write_text('Get("manual.custom" + (i + 1));\n'
                              'Get("manual.custom"\n + suffix);\n'
                              'Get(prefix +\n "manual.custom");\n'
                              'Get("manual.unknown");\n', encoding='utf-8')
            found = [(number, key) for _, number, key in checker.referenced_keys([source], {'manual'})]
        self.assertEqual(found, [(6, 'manual.unknown')])


if __name__ == '__main__':
    unittest.main()
