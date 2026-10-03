[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ManifestPath,
    [string] $ExpectedName = 'Shoutkit',
    [string] $ExpectedNamespaceBase = 'https://github.com/cascadiacollections/smodr-winui3'
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.spdxVersion -ne 'SPDX-2.2' -or $manifest.dataLicense -ne 'CC0-1.0') {
    throw 'SBOM must be SPDX 2.2 with the standard CC0 document data license.'
}
if ($manifest.name -notlike "$ExpectedName*") { throw "Unexpected SBOM name: $($manifest.name)" }
if (!$manifest.documentNamespace.StartsWith($ExpectedNamespaceBase, [StringComparison]::Ordinal)) {
    throw "Unexpected SBOM namespace: $($manifest.documentNamespace)"
}
if (@($manifest.creationInfo.creators).Count -eq 0 -or !$manifest.creationInfo.created) {
    throw 'SBOM has no creation provenance.'
}
if (@($manifest.files).Count -eq 0) { throw 'SBOM contains no shipped files.' }
if (@($manifest.packages).Count -lt 2) { throw 'SBOM contains no detected dependency packages.' }
$ids = @($manifest.packages | ForEach-Object SPDXID)
if ($ids.Count -ne @($ids | Sort-Object -Unique).Count) { throw 'SBOM contains duplicate package SPDX identifiers.' }
Write-Host "Validated SPDX SBOM: $ManifestPath ($(@($manifest.files).Count) files, $(@($manifest.packages).Count) packages)."
