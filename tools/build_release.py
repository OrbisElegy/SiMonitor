#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Build the release product locally, with no development UI or CLI entries."""
import sys
from build import main

if __name__ == '__main__':
    sys.argv.insert(1, '--product-release')
    sys.exit(main())
