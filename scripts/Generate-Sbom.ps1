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
& $tool generate -b $drop -bc $repository -m $output -pn $PackageName -pv $PackageVersion `
    -ps $PackageSupplier -nsb $NamespaceBase -nsu ([Guid]::NewGuid().ToString('N')) `
    -mi SPDX:2.2 -pm true -li false -F false -V Information
if ($LASTEXITCODE -ne 0) { throw "SBOM tool failed with exit code $LASTEXITCODE." }
$manifest = Join-Path $output '_manifest/spdx_2.2/manifest.spdx.json'
& "$PSScriptRoot/Test-Sbom.ps1" -ManifestPath $manifest -ExpectedName $PackageName -ExpectedNamespaceBase $NamespaceBase -DropPath $drop
if ($LASTEXITCODE -ne 0) { throw 'Generated SBOM validation failed.' }
Write-Host "SBOM evidence: $manifest"
