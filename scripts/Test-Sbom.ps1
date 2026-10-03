[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ManifestPath,
    [string] $ExpectedName = 'Shoutkit',
    [string] $ExpectedNamespaceBase = 'https://github.com/cascadiacollections/smodr-winui3',
    [string] $DropPath
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.spdxVersion -ne 'SPDX-2.2' -or $manifest.dataLicense -ne 'CC0-1.0') {
    throw 'SBOM must be SPDX 2.2 with the standard CC0 document data license.'
}
if ($manifest.name -notlike "$ExpectedName*") { throw "Unexpected SBOM name: $($manifest.name)" }
$namespaceBase = $ExpectedNamespaceBase.TrimEnd('/')
$namespace = [string]$manifest.documentNamespace
if ($namespace -cne $namespaceBase -and
    !$namespace.StartsWith("$namespaceBase/", [StringComparison]::Ordinal) -and
    !$namespace.StartsWith("$namespaceBase#", [StringComparison]::Ordinal)) {
    throw "Unexpected SBOM namespace: $($manifest.documentNamespace)"
}
if (@($manifest.creationInfo.creators).Count -eq 0 -or !$manifest.creationInfo.created) {
    throw 'SBOM has no creation provenance.'
}
if (@($manifest.files).Count -eq 0) { throw 'SBOM contains no shipped files.' }
if (@($manifest.packages).Count -lt 2) { throw 'SBOM contains no detected dependency packages.' }
$ids = @($manifest.packages | ForEach-Object SPDXID)
if ($ids.Count -ne @($ids | Sort-Object -Unique).Count) { throw 'SBOM contains duplicate package SPDX identifiers.' }
foreach ($file in @($manifest.files)) {
    $rawName = ([string]$file.fileName).Replace('\', '/')
    if (!$rawName -or [IO.Path]::IsPathRooted($rawName) -or $rawName.Split('/') -contains '..') { throw "Unsafe SBOM file path: $rawName" }
}
if ($DropPath) {
    $drop = (Get-Item -LiteralPath $DropPath -ErrorAction Stop).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $actual = @{}
    foreach ($item in Get-ChildItem -LiteralPath $drop -File -Recurse | Where-Object FullName -NotLike "$drop$([IO.Path]::DirectorySeparatorChar)_manifest$([IO.Path]::DirectorySeparatorChar)*") {
        $relative = [IO.Path]::GetRelativePath($drop, $item.FullName).Replace('\', '/')
        $actual[$relative] = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $declared = @{}
    foreach ($file in @($manifest.files)) {
        $rawName = ([string]$file.fileName).Replace('\', '/')
        if (!$rawName -or [IO.Path]::IsPathRooted($rawName) -or $rawName.Split('/') -contains '..') { throw "Unsafe SBOM file path: $rawName" }
        $name = if ($rawName.StartsWith('./', [StringComparison]::Ordinal)) { $rawName.Substring(2) } else { $rawName }
        if ($declared.ContainsKey($name)) { throw "Duplicate SBOM file path: $name" }
        $checksum = @($file.checksums | Where-Object algorithm -eq 'SHA256')
        if ($checksum.Count -ne 1 -or !$checksum[0].checksumValue) { throw "Missing SHA-256 for SBOM file: $name" }
        $declared[$name] = ([string]$checksum[0].checksumValue).ToLowerInvariant()
    }
    if (Compare-Object @($actual.Keys) @($declared.Keys)) { throw 'SBOM file inventory does not exactly match the published drop.' }
    foreach ($name in $actual.Keys) {
        if ($actual[$name] -cne $declared[$name]) { throw "SBOM checksum mismatch: $name" }
    }
}
Write-Host "Validated SPDX SBOM: $ManifestPath ($(@($manifest.files).Count) files, $(@($manifest.packages).Count) packages)."
