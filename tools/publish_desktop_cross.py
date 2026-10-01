#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Publish Desktop for another RID without executing target-platform code.

Build the preview catalog for the current host in an isolated source copy,
then supply that catalog to the target publish. This keeps RID-specific NuGet
lock changes out of the source checkout.
"""

import argparse
import filecmp
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile

from desktop_distribution import assemble, source_provenance


REQUIRED_AUDIO_EXPORTS = {'sa_abi_version', 'sa_open', 'sa_submit', 'sa_start', 'sa_close', 'sa_info'}
WINDOWS_MACHINES = {"win-x86": 0x014C, "win-x64": 0x8664, "win-arm64": 0xAA64}


def verify_native_audio_binary(binary, rid):
    """Read PE headers and exports without loading or executing the target DLL."""
    if rid not in WINDOWS_MACHINES:
        raise ValueError(f"unsupported Windows audio architecture: {rid}")

    def require_range(offset, size):
        if offset < 0 or size < 0 or offset + size > len(binary):
            raise ValueError("truncated or invalid native audio PE file")

    require_range(0, 64)
    if binary[:2] != b"MZ":
        raise ValueError("native audio binary is not a Windows PE file")
    header = struct.unpack_from("<I", binary, 60)[0]
    require_range(header, 24)
    if binary[header:header + 4] != b"PE\0\0":
        raise ValueError("native audio binary is not a Windows PE file")
    machine, section_count, _, _, _, optional_size, characteristics = struct.unpack_from("<HHIIIHH", binary, header + 4)
    if machine != WINDOWS_MACHINES[rid]:
        raise ValueError(f"native audio binary does not match {rid}")
    if not characteristics & 0x2000:
        raise ValueError("native audio PE file is not a DLL")
    optional = header + 24
    require_range(optional, optional_size)
    if optional_size < 2:
        raise ValueError("missing native audio PE optional header")
    magic = struct.unpack_from("<H", binary, optional)[0]
    expected_magic = 0x10B if rid == "win-x86" else 0x20B
    directory_offset = 96 if magic == 0x10B else 112
    if magic != expected_magic or optional_size < directory_offset + 8:
        raise ValueError("invalid native audio PE optional header")
    if struct.unpack_from("<I", binary, optional + directory_offset - 4)[0] < 1:
        raise ValueError("native audio DLL has no export directory")
    export_rva, export_size = struct.unpack_from("<II", binary, optional + directory_offset)
    if not export_rva or export_size < 40:
        raise ValueError("native audio DLL has no export directory")
    section_table = optional + optional_size
    require_range(section_table, section_count * 40)
    sections = [struct.unpack_from("<IIII", binary, section_table + index * 40 + 8)
                for index in range(section_count)]
    header_size = struct.unpack_from("<I", binary, optional + 60)[0]

    def rva_offset(rva, size):
        if rva and rva + size <= header_size:
            require_range(rva, size)
            return rva
        for _, address, raw_size, raw_offset in sections:
            if address <= rva and rva + size <= address + raw_size:
                offset = raw_offset + rva - address
                require_range(offset, size)
                return offset
        raise ValueError("native audio PE export points outside file-backed sections")

    export = rva_offset(export_rva, 40)
    function_count, name_count, functions_rva, names_rva, ordinals_rva = struct.unpack_from("<IIIII", binary, export + 20)
    functions = rva_offset(functions_rva, function_count * 4)
    names = rva_offset(names_rva, name_count * 4)
    ordinals = rva_offset(ordinals_rva, name_count * 2)
    exports = set()
    for index in range(name_count):
        name_rva = struct.unpack_from("<I", binary, names + index * 4)[0]
        name_offset = rva_offset(name_rva, 1)
        end = binary.find(b"\0", name_offset, name_offset + 256)
        if end < 0:
            raise ValueError("invalid native audio PE export name")
        rva_offset(name_rva, end - name_offset + 1)
        name = binary[name_offset:end].decode("ascii")
        ordinal = struct.unpack_from("<H", binary, ordinals + index * 2)[0]
        if ordinal >= function_count:
            raise ValueError("invalid native audio PE export ordinal")
        function_rva = struct.unpack_from("<I", binary, functions + ordinal * 4)[0]
        if not function_rva or export_rva <= function_rva < export_rva + export_size:
            raise ValueError("native audio DLL requires local function exports")
        rva_offset(function_rva, 1)
        exports.add(name)
    if 'sa_test_render' in exports:
        raise ValueError("native audio DLL exports sa_test_render; test backends cannot be published")
    missing = REQUIRED_AUDIO_EXPORTS - exports
    if missing:
        raise ValueError(f"native audio DLL is missing required ABI exports: {', '.join(sorted(missing))}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", help="Target runtime identifier, such as win-x64 or osx-arm64")
    parser.add_argument("--native-audio-binary", type=Path,
                        help="Production sim_audio_native.dll for a Windows target")
    parser.add_argument("--output", type=Path, help="New publish directory under artifacts by default")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z0-9][a-z0-9.-]*", args.rid):
        parser.error("invalid runtime identifier")
    windows = args.rid.startswith("win-")
    if windows != (args.native_audio_binary is not None):
        parser.error("Windows targets require --native-audio-binary; other targets must omit it")

    root = Path(__file__).resolve().parent.parent
    artifacts = root / "artifacts"
    artifacts.mkdir(exist_ok=True)
    output = args.output.resolve() if args.output else artifacts / f"Monitor.Desktop-{args.rid}"
    if output.exists():
        parser.error(f"output already exists: {output}")
    native = args.native_audio_binary.resolve(strict=True) if windows else None
    if native is not None:
        if native.name != "sim_audio_native.dll":
            parser.error("--native-audio-binary must name sim_audio_native.dll")
        try:
            verify_native_audio_binary(native.read_bytes(), args.rid)
        except ValueError as error:
            parser.error(str(error))

    provenance = source_provenance(root)
    with tempfile.TemporaryDirectory(prefix="desktop-cross-", dir=artifacts) as temporary:
        stage = Path(temporary) / "source"
        shutil.copytree(root, stage, ignore=shutil.ignore_patterns(
            ".git", ".cache", "artifacts", "bin", "obj", ".agents", ".codex", "__pycache__"))
        project = stage / "src/Monitor.Desktop/Monitor.Desktop.csproj"
        common = ["-p:NuGetAudit=false", "-p:UseSharedCompilation=false",
                  f"-m:{min(32, os.cpu_count() or 1)}"]
        subprocess.run(["dotnet", "build", str(project), "-c", "Release", *common],
                       cwd=stage, check=True)
        catalog = stage / "src/Monitor.Desktop/bin/Release/net10.0/style-previews.bin"
        if not catalog.is_file():
            raise RuntimeError(f"Host preview catalog was not generated: {catalog}")
        target_output = Path(temporary) / "publish"
        command = ["dotnet", "publish", str(project), "-c", "Release", "-r", args.rid,
                   "--self-contained", "true", *common, "-p:ProductRelease=true",
                   "-p:UsePrebuiltStylePreviews=true", f"-p:StylePreviewBinary={catalog}",
                   "-o", str(target_output)]
        if native is not None:
            command.append(f"-p:NativeAudioBinary={native}")
        subprocess.run(command, cwd=stage, check=True)
        published_catalog = target_output / "style-previews.bin"
        if not published_catalog.is_file() or not filecmp.cmp(catalog, published_catalog, shallow=False):
            raise RuntimeError("Target publish omitted or changed the preview catalog")
        if native is not None and (target_output / native.name).read_bytes() != native.read_bytes():
            raise RuntimeError("Target publish omitted or changed the native audio DLL")
        assemble(target_output, stage, args.rid, provenance)
        output.parent.mkdir(parents=True, exist_ok=True)
        target_output.replace(output)
    print(f"Published {args.rid}: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
