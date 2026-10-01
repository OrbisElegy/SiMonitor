# Third-party license bundle

`eng/dependencies.json` maps every locked NuGet package to its license text and,
where supplied upstream, a notice file. Several packages share one upstream
license or notice; the shared files are stored once. The license files do not
change the project's own AGPL-3.0-or-later license.

| Locked packages | License and notice source |
| --- | --- |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core 10.0.11 | [dotnet/efcore v10.0.11](https://github.com/dotnet/efcore/blob/v10.0.11/LICENSE.txt) |
| Avalonia 12.1.2 family | [Avalonia package source commit](https://github.com/AvaloniaUI/Avalonia/tree/d3c867a9e2de379249b03dbeb3495bd7f076a81a): `licence.md`, `NOTICE.md` |
| Avalonia.BuildServices 11.3.2 (excluded build assets) | [BuildServices package source commit](https://github.com/AvaloniaUI/Avalonia.BuildServices/tree/777f975b0a0cecf0311273711d56697212c558c0): `LICENSE` |
| Avalonia.Angle.Windows.Natives 2.1.27548.20260419 | `LICENSE` in the locked NuGet package |
| SQLitePCLRaw 2.1.12 family, including e_sqlite3 | [SQLitePCLRaw v2.1.12](https://github.com/ericsink/SQLitePCL.raw/tree/v2.1.12): `LICENSE.TXT`, `NOTICE.TXT` |
| SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 families | `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` in their locked NuGet packages; both families carry identical copies |
| MicroCom.Runtime 0.11.6 | [MicroCom package source commit](https://github.com/kekekeks/MicroCom/tree/76785efcafd91b5902fd19dd11145f6dd655b7b4): `LICENSE` |
| Tmds.DBus.Protocol 0.94.1 | [Tmds.DBus package source commit](https://github.com/tmds/Tmds.DBus/tree/b4a7fed0b878f74cb54f7cca84d2889af4e596ba): `COPYING` |

The pinned miniaudio license is at
`native/sim_audio_native/vendor/LICENSE.miniaudio`. The Infirmary attribution
and license are at `docs/infirmary-source-notice.md` and
`eng/licenses/infirmary-integrated-LICENSE.md`. Its `source_adaptations` ledger
entry pins the upstream commit, source-file hash and bundled license hash, and
registers all four waveform manifests. `tools/verify_dependency_ledger.py` checks
this evidence and rejects unregistered adaptation manifests or missing attribution.

Self-contained desktop packages additionally copy `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT` from the exact .NET runtime pack recorded in the
published `Monitor.Desktop.deps.json`. These runtime files vary by target and
runtime version, so they are selected during distribution assembly.
