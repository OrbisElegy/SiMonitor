# SPDX-License-Identifier: AGPL-3.0-or-later
"""The ICO container must index each embedded PNG exactly."""
import struct
import unittest

import generate_app_icon as generator


class IconContainerTests(unittest.TestCase):
    def test_directory_entries_point_at_each_png(self):
        images = [(16, b'sixteen'), (256, b'two-five-six!')]
        data = generator.icon_container(images)
        reserved, kind, count = struct.unpack_from('<HHH', data, 0)
        self.assertEqual((reserved, kind, count), (0, 1, 2))
        for index, (size, payload) in enumerate(images):
            width, height, colors, _, planes, bits, length, offset = struct.unpack_from('<BBBBHHII', data, 6 + 16 * index)
            self.assertEqual((width, height), (0, 0) if size == 256 else (size, size))
            self.assertEqual((colors, planes, bits, length), (0, 1, 32, len(payload)))
            self.assertEqual(data[offset:offset + length], payload)


if __name__ == '__main__':
    unittest.main()
