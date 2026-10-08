[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ToolPath,
    [Parameter(Mandatory)][string] $DropPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $PackageVersion = '1.0.0',
    [string] $PackageName = 'Shoutkit',
    [string] $PackageSupplier = 'Organization: Cascadia Collections',
    [string] $NamespaceBase = 'https://github.com/cascadiacollections/smodr-winui3'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$tool = (Get-Item -LiteralPath $ToolPath -ErrorAction Stop).FullName
$drop = (Get-Item -LiteralPath $DropPath -ErrorAction Stop).FullName
$output = [IO.Path]::GetFullPath($OutputDirectory, $repository)
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) {
        throw 'SBOM output directory must be empty so stale evidence cannot pass.'
    }
} else { New-Item -ItemType Directory -Path $output | Out-Null }
$sourceSnapshot = [IO.Directory]::CreateTempSubdirectory('shoutkit-sbom-source-')
try {
    # Scan current tracked source only. Old ignored manifests can otherwise be
    # discovered as external SBOM inputs and introduce paths outside the drop.
    $files = @(& git -C $repository -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked SBOM source.' }
    foreach ($relative in $files) {
        $source = [IO.Path]::GetFullPath((Join-Path $repository $relative))
        if (-not $source.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escapes repository.' }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SBOM source cannot contain file links.' }
        $target = Join-Path $sourceSnapshot.FullName $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath $source -Destination $target
    }
    # NuGet component detection reads evaluated assets, not packages.lock.json.
    # Include only the current app graph, without scanning arbitrary ignored outputs.
    $appAssets = Join-Path $repository 'smodr/obj/project.assets.json'
    if (-not (Test-Path -LiteralPath $appAssets -PathType Leaf)) { throw 'Restore the app before generating its SBOM.' }
    if ((Get-Item -LiteralPath $appAssets).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'App assets cannot be a file link.' }
    $snapshotAssets = Join-Path $sourceSnapshot.FullName 'smodr/obj/project.assets.json'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $snapshotAssets))
    Copy-Item -LiteralPath $appAssets -Destination $snapshotAssets
    & $tool generate -b $drop -bc $sourceSnapshot.FullName -m $output -pn $PackageName -pv $PackageVersion `
        -ps $PackageSupplier -nsb $NamespaceBase -nsu ([Guid]::NewGuid().ToString('N')) `
        -mi SPDX:2.2 -pm true -li false -F false -V Information
    if ($LASTEXITCODE -ne 0) { throw "SBOM tool failed with exit code $LASTEXITCODE." }
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $sourceSnapshot.FullName.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside temporary root.' }
    Remove-Item -LiteralPath $sourceSnapshot.FullName -Recurse -Force
}
$manifest = Join-Path $output '_manifest/spdx_2.2/manifest.spdx.json'
& "$PSScriptRoot/Test-Sbom.ps1" -ManifestPath $manifest -ExpectedName $PackageName -ExpectedNamespaceBase $NamespaceBase -DropPath $drop
if ($LASTEXITCODE -ne 0) { throw 'Generated SBOM validation failed.' }
Write-Host "SBOM evidence: $manifest"
