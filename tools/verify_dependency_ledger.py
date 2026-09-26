#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Verify locked packages, native sources and adapted-source evidence against the ledger."""
import json
import hashlib
from pathlib import Path


def verify_source_adaptations(root, ledger):
    """Check committed adaptation evidence without fetching the upstream project."""
    manifests = {}
    for path in sorted((root / 'eng').rglob('*.json')):
        data = json.loads(path.read_text())
        if isinstance(data, dict) and 'upstream_commit' in data:
            manifests[path.relative_to(root).as_posix()] = data
    registered = set()
    identifiers = set()
    for entry in ledger.get('source_adaptations', []):
        identifier = entry.get('id')
        if not identifier or identifier in identifiers:
            raise ValueError(f'Missing or duplicate source adaptation ID: {identifier}')
        identifiers.add(identifier)
        for field in ('source', 'source_path', 'license', 'license_file', 'notice_file'):
            if not isinstance(entry.get(field), str) or not entry[field].strip():
                raise ValueError(f'Missing adaptation {field}: {identifier}')
        for field, length in (('commit', 40), ('source_sha256', 64), ('license_sha256', 64)):
            value = entry.get(field)
            if not isinstance(value, str) or len(value) != length or any(c not in '0123456789abcdef' for c in value):
                raise ValueError(f'Invalid adaptation {field}: {identifier}')
        license_path = root / entry['license_file']
        if not license_path.is_file():
            raise ValueError(f'Missing adaptation license file: {identifier}')
        if hashlib.sha256(license_path.read_bytes()).hexdigest() != entry['license_sha256']:
            raise ValueError(f'Adaptation license hash mismatch: {identifier}')
        notice = root / entry['notice_file']
        if not notice.is_file() or not notice.read_text().strip():
            raise ValueError(f'Missing adaptation attribution notice: {identifier}')
        paths = entry.get('manifests')
        if not isinstance(paths, list) or not paths:
            raise ValueError(f'Missing adaptation manifests: {identifier}')
        for relative in paths:
            if not isinstance(relative, str) or relative in registered:
                raise ValueError(f'Invalid or duplicate adaptation manifest: {relative}')
            registered.add(relative)
            if relative not in manifests:
                raise ValueError(f'Missing adaptation manifest or upstream_commit: {relative}')
            manifest = manifests[relative]
            for field, expected in (
                ('upstream_commit', entry['commit']),
                ('source_path', entry['source_path']),
                ('source_sha256', entry['source_sha256']),
                ('license_file', entry['license_file']),
                ('license_sha256', entry['license_sha256']),
            ):
                if manifest.get(field) != expected:
                    raise ValueError(f'Adaptation {field} mismatch: {relative}')
            if 'license' in manifest and manifest['license'] != entry['license']:
                raise ValueError(f'Adaptation license mismatch: {relative}')
    unregistered = manifests.keys() - registered
    if unregistered:
        raise ValueError(f'Unregistered source adaptations: {sorted(unregistered)}')
    print(f'ok: {len(identifiers)} source adaptations cover {len(registered)} manifests with pinned source and license evidence')


def main():
    root = Path(__file__).resolve().parent.parent
    ledger = json.loads((root / 'eng/dependencies.json').read_text())
    verify_source_adaptations(root, ledger)
    entries = {}
    for entry in ledger['direct_packages'] + ledger['transitive_packages']:
        key = (entry['id'].lower(), entry['version'])
        if key in entries:
            raise ValueError(f'Duplicate ledger package: {key}')
        if not entry.get('license'):
            raise ValueError(f'Missing package license: {key}')
        if entry.get('license_file') and not (root / entry['license_file']).is_file():
            raise ValueError(f'Missing license file: {key}')
        entries[key] = entry['content_hash_sha512_base64']
    seen = set()
    for folder in ('src', 'tests'):
        for lock in sorted((root / folder).glob('*/packages.lock.json')):
            for packages in json.loads(lock.read_text())['dependencies'].values():
                for name, package in packages.items():
                    if package['type'].lower() == 'project':
                        continue
                    key = (name.lower(), package['resolved'])
                    if entries.get(key) != package['contentHash']:
                        raise ValueError(f'Lock/ledger mismatch in {lock.relative_to(root)}: {key}')
                    seen.add(key)
    if not seen or seen != entries.keys():
        raise ValueError(f'Unreferenced ledger packages: {entries.keys() - seen}')
    print(f'ok: {len(seen)} locked dependency versions match license/hash ledger')
    for entry in ledger['native_dependencies']:
        for source in entry.get('source_files', []):
            if not entry.get('license') or not entry.get('commit'):
                raise ValueError(f'Unpinned native source: {entry["id"]}')
            if hashlib.sha256((root / source['path']).read_bytes()).hexdigest() != source['sha256']:
                raise ValueError(f'Native source hash mismatch: {source["path"]}')
    print('ok: vendored native source hashes and license selections verified')


if __name__ == '__main__':
    main()
