#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Assemble and verify local Desktop candidate metadata (not a signed release)."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

MANIFEST = "distribution-manifest.json"
SCHEMA = "MonitorDesktopDistribution@1"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(directory):
    result = {}
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Symbolic links are not supported: {path}")
        if path.is_file() and path.relative_to(directory).as_posix() != MANIFEST:
            result[path.relative_to(directory).as_posix()] = {
                "bytes": path.stat().st_size, "sha256": digest(path)}
    return result


def verify(directory):
    directory = Path(directory)
    manifest_path = directory / MANIFEST
    if manifest_path.is_symlink() or manifest_path.stat().st_size > 4 * 1024 * 1024:
        raise ValueError("Invalid manifest file")
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    if (not isinstance(data, dict) or data.get("schema") != SCHEMA or
            not isinstance(data.get("files"), dict) or not isinstance(data.get("rid"), str) or
            not data["rid"] or not isinstance(data.get("source"), dict) or data.get("releaseAccepted") is not False):
        raise ValueError("Unsupported distribution manifest")
    actual = inventory(directory)
    if data["files"] != actual:
        missing = sorted(set(data["files"]) - set(actual))
        extra = sorted(set(actual) - set(data["files"]))
        changed = sorted(k for k in actual.keys() & data["files"].keys() if actual[k] != data["files"][k])
        raise ValueError(f"Distribution differs: missing={missing}, extra={extra}, changed={changed}")
    return data


def runtime_pack_legal(directory, source, rid):
    """Find license files for the runtime pack recorded by this publish."""
    deps = json.loads((directory / "Monitor.Desktop.deps.json").read_text(encoding="utf-8"))
    prefix = f"runtimepack.Microsoft.NETCore.App.Runtime.{rid}/"
    matches = [key for key in deps["libraries"] if key.startswith(prefix)]
    if len(matches) != 1:
        raise ValueError(f"Expected one .NET runtime pack for {rid}: {matches}")
    version = matches[0][len(prefix):]
    assets = json.loads((source / "src/Monitor.Desktop/obj/project.assets.json").read_text(encoding="utf-8"))
    package_name = f"microsoft.netcore.app.runtime.{rid}"
    for folder in assets["packageFolders"]:
        package = Path(folder) / package_name / version
        license_path = package / "LICENSE.TXT"
        notice_path = package / "THIRD-PARTY-NOTICES.TXT"
        if license_path.is_file() and notice_path.is_file() and license_path.stat().st_size and notice_path.stat().st_size:
            return matches[0], license_path, notice_path
    raise ValueError(f"Missing license or notices for .NET runtime pack {matches[0]}")


def assemble(directory, source, rid, provenance):
    directory, source = Path(directory), Path(source)
    executable = "Monitor.Desktop.exe" if rid.startswith("win-") else "Monitor.Desktop"
    required = [executable, "Monitor.Desktop.dll", "Monitor.Desktop.runtimeconfig.json",
                "Monitor.Desktop.deps.json", "style-previews.bin"]
    if rid.startswith("win-"):
        required += ["sim_audio_native.dll", "LICENSE.miniaudio"]
    for name in required:
        if not (directory / name).is_file() or (directory / name).stat().st_size == 0:
            raise ValueError(f"Missing published asset: {name}")
    inventory(directory)  # reject links before copying metadata into the package
    runtime_name, runtime_license, runtime_notices = runtime_pack_legal(directory, source, rid)
    for reserved in [MANIFEST, "distribution-info.txt", "legal"]:
        if (directory / reserved).exists():
            raise ValueError(f"Distribution metadata already exists: {reserved}")
    legal = directory / "legal"
    legal.mkdir()
    for relative in ["LICENSE", "eng/dependencies.json", "docs/license-scope.md", "docs/infirmary-source-notice.md"]:
        target = legal / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / relative, target)
    shutil.copytree(source / "eng/licenses", legal / "eng/licenses")
    shutil.copyfile(source / "native/sim_audio_native/vendor/LICENSE.miniaudio", legal / "LICENSE.miniaudio")
    runtime_legal = legal / "runtime" / runtime_name.replace("/", "-")
    runtime_legal.mkdir(parents=True)
    shutil.copyfile(runtime_license, runtime_legal / "LICENSE.TXT")
    shutil.copyfile(runtime_notices, runtime_legal / "THIRD-PARTY-NOTICES.TXT")
    info = f"""Seele's SiMonitor V0.5 standalone development candidate ({rid})

Offline launch: run {executable} without arguments; keep this directory intact.
No .NET SDK installation is required for this self-contained package.
Windows audio: keep sim_audio_native.dll beside the executable.
Sound remains off at startup; enable it explicitly in Settings > Sound.
On other targets the current WASAPI backend is unavailable.

Applied settings are stored in local application data under
Monitor/display-preferences.json (Windows: %LOCALAPPDATA%/Monitor/).
Restart replays generator inputs from simulation zero, not the old timeline.
For a clean default configuration, close the app and move that file aside.

Teaching simulator only; not for clinical decisions or patient monitoring.
This candidate does not assert Windows soak, audio-fault or release acceptance.
Physical end-to-end audio latency is not a V0.5 acceptance gate.
The manifest detects accidental changes; it is not a signature or authenticity
proof. Retain the manifest when transferring this directory for testing.

Project license: AGPL-3.0-or-later. Locked NuGet package licenses, upstream
notices and the selected {runtime_name} notices are under legal/.
Matching complete-source delivery and final release acceptance remain required
before a public binary release.
"""
    (directory / "distribution-info.txt").write_text(info, encoding="utf-8")
    data = {"schema": SCHEMA, "rid": rid, "releaseAccepted": False,
            "source": provenance, "files": inventory(directory)}
    (directory / MANIFEST).write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    verify(directory)


def source_provenance(root):
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=root, text=True).strip()
    return {"commit": git("rev-parse", "HEAD"),
            "workingTreeDirty": bool(git("status", "--porcelain", "--untracked-files=normal")),
            "dotnetSdk": subprocess.check_output(["dotnet", "--version"], cwd=root, text=True).strip()}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Published directory to verify")
    args = parser.parse_args()
    try:
        result = verify(args.directory)
    except (OSError, ValueError) as error:
        parser.exit(1, f"FAIL: {error}\n")
    print(f"PASS: {len(result['files'])} files match the {result['rid']} candidate manifest")
