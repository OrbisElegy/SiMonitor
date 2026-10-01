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
        machines = {"win-x86": 0x014C, "win-x64": 0x8664, "win-arm64": 0xAA64}
        if args.rid not in machines:
            parser.error(f"unsupported Windows audio architecture: {args.rid}")
        binary = native.read_bytes()
        if len(binary) < 64 or binary[:2] != b"MZ":
            parser.error("native audio binary is not a Windows PE file")
        header = struct.unpack_from("<I", binary, 60)[0]
        if header + 6 > len(binary) or binary[header:header + 4] != b"PE\0\0" or \
                struct.unpack_from("<H", binary, header + 4)[0] != machines[args.rid]:
            parser.error(f"native audio binary does not match {args.rid}")

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
