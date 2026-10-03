$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$root = Join-Path ([IO.Path]::GetTempPath()) ('shoutkit-manifest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $source = Join-Path $repository 'smodr/Package.appxmanifest'
    & "$PSScriptRoot/Test-PackageManifest.ps1" -ManifestPath $source
    $failed = $false
    try { & "$PSScriptRoot/Test-PackageManifest.ps1" -ManifestPath $source -Production } catch { $failed = $true }
    if (!$failed) { throw 'Development identity passed production validation.' }
    [xml] $mutated = Get-Content -LiteralPath $source -Raw
    $capabilities = $mutated.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Capabilities']")
    $copy = $capabilities.FirstChild.CloneNode($true)
    $capabilities.AppendChild($copy) | Out-Null
    $path = Join-Path $root 'extra-capability.xml'
    $mutated.Save($path)
    $failed = $false
    try { & "$PSScriptRoot/Test-PackageManifest.ps1" -ManifestPath $path } catch { $failed = $true }
    if (!$failed) { throw 'Unexpected capability passed validation.' }
    Write-Host 'Package manifest harness passed.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
