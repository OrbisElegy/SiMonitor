#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check that catalog keys referenced in C# sources exist in the built-in catalogs.

A literal counts as a key when it is a dotted lowercase-first identifier whose
first segment is a namespace already used by the catalogs, such as "sound.pause",
and it does not end in a file extension. Only product sources are scanned; smoke
checks and fixtures may hold arbitrary identifiers. In the network project, wire
identifiers can share these prefixes with the catalogs. Keys built at run time
are not visible to this check.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
CATALOGS = ROOT / 'src/Monitor.Infrastructure/Localization/Resources'
NETWORK = ROOT / 'src/Monitor.Network'
LITERAL = re.compile(r'"([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"')
FILE_EXTENSIONS = {'json', 'png', 'bin', 'txt', 'md', 'pcm', 'svg', 'ico'}
TEST_SUFFIXES = ('SmokeChecks.cs', 'Fixture.cs')


def load_catalog(locale):
    return json.loads((CATALOGS / f'{locale}.json').read_text(encoding='utf-8'))


def classroom_wire_ids():
    manifest = json.loads((NETWORK / 'Contracts/contract-manifest.json').read_text(encoding='utf-8'))
    actions = {item['action_id'] for item in manifest['action_catalog']}
    events = {item['event_type'] for item in manifest['event_payload_registry']}
    # The classroom configuration schema also defines this named projection.
    return actions | events | {'display.settings'}


def referenced_keys(sources, namespaces):
    """Yield complete literal keys; concatenated fragments are not catalog keys."""
    for path in sources:
        source = path.read_text(encoding='utf-8')
        for match in LITERAL.finditer(source):
            if source[match.end():].lstrip().startswith('+') or source[:match.start()].rstrip().endswith('+'):
                continue
            key = match.group(1)
            if key.split('.', 1)[0] in namespaces and key.rsplit('.', 1)[1] not in FILE_EXTENSIONS:
                yield path, source.count('\n', 0, match.start()) + 1, key


def missing_keys(sources, catalog, network, wire_ids):
    namespaces = {key.split('.', 1)[0] for key in catalog}
    for path, number, key in referenced_keys(sources, namespaces):
        if key not in catalog and not (path.is_relative_to(network) and key in wire_ids):
            yield path, number, key


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    catalog = load_catalog('zh-CN')
    sources = sorted(path for path in (ROOT / 'src').rglob('*.cs')
                     if 'obj' not in path.parts and 'bin' not in path.parts and not path.name.endswith(TEST_SUFFIXES))
    missing = list(missing_keys(sources, catalog, NETWORK, classroom_wire_ids()))
    for path, number, key in missing:
        print(f'{path.relative_to(ROOT)}:{number}: unknown localization key {key}', file=sys.stderr)
    if missing:
        return 1
    print(f'ok: every referenced localization key exists ({len(catalog)} catalog keys)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
