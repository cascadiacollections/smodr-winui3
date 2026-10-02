# Generated software notices

Settings reads the committed `smodr/Assets/SoftwareLicenses.txt` offline. The companion `SoftwareLicenseInventory.json` is a machine-readable inventory with package identities, resolved versions, normalized NuGet content hashes, metadata hashes, and references to deduplicated notice text. Both files are explicitly copied to build/publish output and MSIX content.

## Refresh after a dependency or SDK update

Use PowerShell 7.2 or newer (`pwsh`), not Windows PowerShell 5.1:

```powershell
dotnet restore smodr.slnx -p:Platform=ARM64 --locked-mode
pwsh -NoProfile -File scripts/Update-LicenseInventory.ps1
pwsh -NoProfile -File scripts/Update-LicenseInventory.ps1 -Check
pwsh -NoProfile -File scripts/Test-LicenseInventory.ps1
```

After intentionally updating the package graph, refresh the lockfiles before running these commands. On this corporate host use the existing local SDK runner and approved NuGet source as documented in README; the generator itself makes no network calls and changes no package/source configuration. Review and commit both generated assets with the dependency change. Do not hand-edit them.

The generator reads all non-project entries from the app's lockfile across framework/RID sections, deduplicates package/version pairs, and reads restored `.nuspec` declarations and notice files from the package folders in `smodr/obj/project.assets.json`. `-PackageFolders <paths>` can override cache locations without changing output. SDK-provided Windows .NET runtime packs are added from exact-version download dependencies, because self-contained runtime packs are absent from the ordinary app lockfile. SDK host tools and unrelated downloaded frameworks are not included. Test-project-only dependencies are not scanned. Known Windows SDK build-tool entries are labeled build-only; the rest of the conservative app graph is not claimed to be a byte-perfect list of shipped components.

NuGet's normalized `contentHash` is obtained from restore metadata and checked against the app lockfile; it is not necessarily the SHA512 of the signed archive. The generator trusts the existing restore and does not replace NuGet package-signature validation or supply-chain scanning. Included license/notice text is normalized to LF and deduplicated by SHA256, with package-to-file references retained. Machine paths and timestamps are excluded. Vendor whitespace is preserved; the generated notice file has an editor/Git whitespace exemption.

The modern NuGet `license` file/expression declaration is preferred. Legacy URL-only packages are explicitly recorded without downloading the URL or pretending the declaration is an SPDX license. Missing declarations, missing declared files/packages, conflicting restore hashes, invalid package identities, escaped/linked notice paths, and oversized or non-text notices stop generation. SPDX-expression packages without bundled text are listed with a release-review warning, not silently assigned a fabricated copyright or license text.

## Automated gates

Both Windows CI architectures run `Update-LicenseInventory.ps1 -Check` after locked restore, plus synthetic fixture checks. `-Check` is read-only and fails if either committed asset is stale. The fixture suite covers RID deduplication, project/test exclusion, build labels, file/SPDX/legacy metadata, byte-deterministic output, notice deduplication, changed notices, restore-hash/version mismatches, unsafe/missing license paths, absent metadata, and stale JSON.

After publishing and unsigned MSIX packaging, CI verifies that both bundled assets exactly match the committed files (the package check opens the ZIP read-only and does not install it):

```powershell
pwsh -NoProfile -File scripts/Test-LicenseInventory.ps1 -PublishedDirectory out/shoutkit-arm64
pwsh -NoProfile -File scripts/Test-LicenseInventory.ps1 -MsixDirectory out/msix-ARM64
```

## Remaining distribution review

This automation is inventory and notice-drift detection, not legal approval or an assertion of complete redistribution compliance. Review the exact signed artifact, vendor redistribution conditions, expression-only/legacy-URL packages lacking bundled terms, framework/native components outside this inventory, and the upstream GPL-3.0 versus repository MIT provenance question before distributing externally. Do not remove these gates merely because inventory checks pass. See [RELEASE.md](RELEASE.md).

Metadata format references: [NuGet nuspec license declarations](https://learn.microsoft.com/en-us/nuget/reference/nuspec#license) and [.NET license-information guidance](https://github.com/dotnet/core/blob/main/license-information.md).
