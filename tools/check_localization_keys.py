#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check that catalog keys referenced in C# sources exist in the built-in catalogs.

A literal counts as a key when it is a dotted lowercase-first identifier whose
first segment is a namespace already used by the catalogs, such as "sound.pause",
and it does not end in a file extension. Only product sources are scanned; smoke
checks and fixtures may hold arbitrary identifiers. Keys built at run time are
not visible to this check.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
CATALOGS = ROOT / 'src/Monitor.Infrastructure/Localization/Resources'
LITERAL = re.compile(r'"([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"')
FILE_EXTENSIONS = {'json', 'png', 'bin', 'txt', 'md', 'pcm', 'svg', 'ico'}
TEST_SUFFIXES = ('SmokeChecks.cs', 'Fixture.cs')


def load_catalog(locale):
    return json.loads((CATALOGS / f'{locale}.json').read_text(encoding='utf-8'))


def referenced_keys(sources, namespaces):
    """Yield (path, line number, key) for literals in a catalog namespace."""
    for path in sources:
        for number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), start=1):
            for key in LITERAL.findall(line):
                if key.split('.', 1)[0] in namespaces and key.rsplit('.', 1)[1] not in FILE_EXTENSIONS:
                    yield path, number, key


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    catalog = load_catalog('zh-CN')
    namespaces = {key.split('.', 1)[0] for key in catalog}
    sources = sorted(path for path in (ROOT / 'src').rglob('*.cs')
                     if 'obj' not in path.parts and 'bin' not in path.parts and not path.name.endswith(TEST_SUFFIXES))
    missing = [(path, number, key) for path, number, key in referenced_keys(sources, namespaces) if key not in catalog]
    for path, number, key in missing:
        print(f'{path.relative_to(ROOT)}:{number}: unknown localization key {key}', file=sys.stderr)
    if missing:
        return 1
    print(f'ok: every referenced localization key exists ({len(catalog)} catalog keys)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
