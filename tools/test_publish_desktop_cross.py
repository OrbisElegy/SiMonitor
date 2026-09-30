# SPDX-License-Identifier: AGPL-3.0-or-later
"""Validate file-backed PE export tables without loading target-platform code."""
import contextlib
import io
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

import publish_desktop_cross as publisher
from publish_desktop_cross import REQUIRED_AUDIO_EXPORTS, verify_native_audio_binary


# PE/COFF machine identifiers are independent of the publisher's lookup table.
PE_MACHINES = {'win-x86': 0x014C, 'win-x64': 0x8664, 'win-arm64': 0xAA64}


def pe_dll(rid='win-x64', exports=None):
    """Construct PE32/PE32+ headers, code section, and complete named exports."""
    exports = sorted(REQUIRED_AUDIO_EXPORTS if exports is None else exports)
    binary = bytearray(2048)
    binary[:2] = b'MZ'
    struct.pack_into('<I', binary, 60, 128)
    binary[128:132] = b'PE\0\0'
    optional_size = 224 if rid == 'win-x86' else 240
    struct.pack_into('<HHIIIHH', binary, 132, PE_MACHINES[rid], 2, 0, 0, 0, optional_size, 0x2002)
    optional = 152
    directory_offset = 96 if rid == 'win-x86' else 112
    struct.pack_into('<H', binary, optional, 0x10B if rid == 'win-x86' else 0x20B)
    struct.pack_into('<I', binary, optional + 60, 512)
    struct.pack_into('<I', binary, optional + directory_offset - 4, 16)
    struct.pack_into('<II', binary, optional + directory_offset, 0x2000, 1024)
    sections = optional + optional_size
    for index, (name, address, size, offset, flags) in enumerate([
            (b'.text', 0x1000, 512, 512, 0x60000020),
            (b'.edata', 0x2000, 1024, 1024, 0x40000040)]):
        struct.pack_into('<8sIIIIIIHHI', binary, sections + index * 40,
                         name, size, address, size, offset, 0, 0, 0, 0, flags)
    binary[512:1024] = b'\xc3' * 512
    struct.pack_into('<IIHHIIIIIII', binary, 1024, 0, 0, 0, 0, 0x23F0, 1,
                     len(exports), len(exports), 0x2040, 0x2080, 0x20C0)
    binary[2032:2048] = b'sim_audio.dll\0\0\0'
    name_offset = 1280
    for index, name in enumerate(exports):
        encoded = name.encode('ascii') + b'\0'
        binary[name_offset:name_offset + len(encoded)] = encoded
        struct.pack_into('<I', binary, 1088 + index * 4, 0x1000 + index * 16)
        struct.pack_into('<I', binary, 1152 + index * 4, 0x2000 + name_offset - 1024)
        struct.pack_into('<H', binary, 1216 + index * 2, index)
        name_offset += len(encoded)
    return binary


class NativeAudioPeTests(unittest.TestCase):
    def test_all_supported_architectures_accept_production_exports(self):
        for rid in PE_MACHINES:
            with self.subTest(rid=rid):
                verify_native_audio_binary(pe_dll(rid), rid)

    def test_optional_abi_extensions_are_accepted(self):
        verify_native_audio_binary(pe_dll(exports=REQUIRED_AUDIO_EXPORTS | {'sa_clock_sample', 'sa_wait_writable'}), 'win-x64')

    def test_same_architecture_test_library_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'sa_test_render'):
            verify_native_audio_binary(pe_dll(exports=REQUIRED_AUDIO_EXPORTS | {'sa_test_render'}), 'win-x64')

    def test_publish_rejects_invalid_audio_before_building(self):
        for exports in (REQUIRED_AUDIO_EXPORTS | {'sa_test_render'}, REQUIRED_AUDIO_EXPORTS - {'sa_submit'}):
            with self.subTest(exports=exports), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                binary = root / 'sim_audio_native.dll'
                binary.write_bytes(pe_dll(exports=exports))
                with patch('sys.argv', ['publish_desktop_cross.py', 'win-x64', '--native-audio-binary', str(binary)]), \
                        patch.object(publisher, '__file__', str(root / 'tools/publish_desktop_cross.py')), \
                        patch.object(publisher, 'source_provenance') as provenance, \
                        patch.object(publisher.subprocess, 'run') as build, \
                        contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                    publisher.main()
                self.assertEqual(error.exception.code, 2)
                provenance.assert_not_called()
                build.assert_not_called()

    def test_missing_required_export_is_rejected(self):
        for name in REQUIRED_AUDIO_EXPORTS:
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, name):
                verify_native_audio_binary(pe_dll(exports=REQUIRED_AUDIO_EXPORTS - {name}), 'win-x64')

    def test_different_architectures_are_rejected(self):
        for binary_rid in PE_MACHINES:
            for target_rid in PE_MACHINES:
                if binary_rid != target_rid:
                    with self.subTest(binary_rid=binary_rid, target_rid=target_rid), \
                            self.assertRaisesRegex(ValueError, 'does not match ' + target_rid):
                        verify_native_audio_binary(pe_dll(binary_rid), target_rid)

    def test_truncated_headers_and_export_tables_are_rejected(self):
        for size in (0, 63, 133, 400, 1050, 1220, 1290):
            with self.subTest(size=size), self.assertRaises(ValueError):
                verify_native_audio_binary(pe_dll()[:size], 'win-x64')

    def test_export_tables_must_resolve_to_file_backed_sections(self):
        binary = pe_dll()
        struct.pack_into('<I', binary, 1056, 0x9000)
        with self.assertRaisesRegex(ValueError, 'outside file-backed sections'):
            verify_native_audio_binary(binary, 'win-x64')

    def test_export_name_and_ordinal_are_validated(self):
        for offset, format_string, value in ((1152, '<I', 0x9000), (1216, '<H', 99)):
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                binary = pe_dll()
                struct.pack_into(format_string, binary, offset, value)
                verify_native_audio_binary(binary, 'win-x64')

    def test_absent_and_forwarded_functions_are_rejected(self):
        for function_rva in (0, 0x2300, 0x9000):
            with self.subTest(function_rva=function_rva), self.assertRaises(ValueError):
                binary = pe_dll()
                struct.pack_into('<I', binary, 1088, function_rva)
                verify_native_audio_binary(binary, 'win-x64')

    def test_executable_and_missing_export_directory_are_rejected(self):
        for offset, format_string, value in ((150, '<H', 2), (264, '<I', 0)):
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                binary = pe_dll()
                struct.pack_into(format_string, binary, offset, value)
                verify_native_audio_binary(binary, 'win-x64')


if __name__ == '__main__':
    unittest.main()
