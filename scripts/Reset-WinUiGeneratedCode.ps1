[CmdletBinding()]
param(
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$generatedRoot = Join-Path $repository 'smodr/obj'
$source = [IO.Path]::GetFullPath((Join-Path $generatedRoot "$Platform/$Configuration"))
if (-not $source.StartsWith([IO.Path]::GetFullPath($generatedRoot) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Generated output must be inside smodr/obj.'
}
if (-not (Test-Path -LiteralPath $source)) {
    Write-Host "No generated output exists for $Configuration|$Platform."
    return
}
if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Refusing to move a linked generated-output directory.'
}
$backup = Join-Path $repository ('out/generated-code-backup/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Move-Item -LiteralPath $source -Destination (Join-Path $backup "$Platform-$Configuration")
Write-Host "Archived generated output to $backup"
Write-Host "Reload the solution, then rebuild $Configuration|$Platform using the pinned SDK."
