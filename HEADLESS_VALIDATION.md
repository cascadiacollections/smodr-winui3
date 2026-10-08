# Headless validation - 2026-10-07

Validated on Windows 11 ARM64 at base commit `26a86288378c61458e82a001d15acef9235e6c77`, with the working-tree fixes below. x64 binaries ran under Windows emulation on this host. Windows uses pinned .NET 11 RC1; portable checks use .NET 10.0.401.

The user reported earlier interactive QA worked great. That is user-reported evidence, without a case-by-case record or artifact hashes; this run did not repeat UI or audible playback testing.

## Results

| Check | Result |
| --- | --- |
| Locked Windows restores, ARM64 and x64 | Passed |
| Full Windows analyzer builds | Zero warnings/errors on both architectures |
| Windows tests | 389 passed on ARM64; 389 passed on x64; zero skipped |
| Isolated stable .NET 10 tests | 169 passed; zero skipped |
| SARIF validation | 23 clean reports: 9 ARM64, 9 x64, 5 portable |
| Race/soak suite | 46 tests per iteration; 39 ARM64 and 20 x64 iterations passed, approximately one minute each |
| Performance harness | Three successful runs per architecture; diagnostic timings, no performance thresholds |
| Portable formatting / changed-file whitespace | Passed |
| Solution configurations | Both formats, Debug/Release ARM64/x64, plus two negative fixtures passed |
| Local SDK packages | All four packages validated; fresh-cache offline consumer passed |
| API compatibility gate | Deliberately removed public types rejected with CP0001 |
| Fully trimmed consumer | Self-contained win-arm64 consumer published and executed successfully |
| Live metadata smoke | KEXP continuous ICY accepted The Breeders - Cannonball and plausible catalog artwork; one accepted block reported |
| Self-contained app publish | ARM64 and x64 passed; neither executable launched |
| Unsigned MSIX | ARM64 and x64 built; package notices verified |
| Software notices | 46 packages / 20 notice texts verified; eight inventory fixtures passed |
| Security/evidence harnesses | Package manifest, native QA mechanics, release evidence, seven SBOM fixtures, eleven analysis fixtures, fourteen Docker fixtures passed |
| Dependency audit | Locked restore with NuGetAudit=true, mode=all, NU1901-NU1904 as errors passed; package listing reported no vulnerable packages against available sources |
| SPDX SBOM | Pinned v4.1.5 tool SHA-256 verified; x64 inventory verified against 570 published files, 103 package records |
| Consolidated headless evidence | Automated Pass; production Incomplete by design |

## Changes and execution notes

- Converted the recovery test player's requested-playback backing field into an auto property and deconstructed the performance harness's decode dimensions. Both changes close required analyzer failures without changing app behavior.
- Normalized mixed CRLF/LF lines in PlaybackEnvironmentPolicy.cs to satisfy portable formatting. Git's normalized diff contains no semantic change.
- The pre-existing modified test lockfile blocked locked restore with NU1004. Its original bytes were backed up to `out/headless-20261007/packages.lock.before.json`; regeneration from the current project matches the committed dependencies, and locked restore now passes. The unrelated `out.lnk` remains untouched.
- Concurrent portable/Windows builds encountered XAML compiler locks. The accepted portable test/analyzer evidence uses isolated outputs. SDK packaging was rerun successfully after Windows compilation finished.
- Trim restore needed a temporary NuGet config to avoid CLI source normalization and the documented nuget.org v2 fallback after the v3 endpoint's TLS handshake failed. Its fresh draft-package cache disables audit only for this local consumer; the app's separate audited restore passed.
- The SBOM scans the repository, including local fixtures/output, so its package count is build inventory rather than a claim that every detected package ships in the app. Published file hashes are checked exactly.

## Evidence and remaining gates

Local generated outputs are under `out/headless-20261007/` (ignored by Git). The final hashed report is `evidence-with-sbom/20261008T051809Z-2ed0cf2662a641f2b9d5469934f3f0a1/release-evidence.json`. Test TRX files, SARIF, soak summaries, performance JSON, SDK packages, both published folders, and the x64 SBOM are retained there. Unsigned MSIX files are under `out/msix-ARM64/` and `out/msix-x64/`.

Docker was unreachable; no fresh Linux/container run is claimed. The MSIX install/upgrade script explicitly permits only a disposable GitHub Actions runner, so it was not run on this personal host. Both MSIX builds still warn that mspdbcmf.exe is unavailable and omit symbols packages. Production identity, signing, symbols tooling and channel-specific distribution remain open. Remote CodeQL and dependency-review workflows were not dispatched.

The KEXP smoke proves metadata/catalog reachability at this moment, not audible playback or native HLS timed-ID3 delivery. Hardware interruptions, visuals/accessibility and signed package activation require their native cases. Earlier user QA is acknowledged without fabricating case results. Favorites export/import, search filters and the other parity features remain separate product work; this pass validates the current application and fixes headless gate failures.
