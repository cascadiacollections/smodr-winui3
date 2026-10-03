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
    [xml] $production = Get-Content -LiteralPath $source -Raw
    $productionIdentity = $production.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    $productionIdentity.Name = 'Example.Shoutkit'
    $productionIdentity.Publisher = 'CN=Example Publisher'
    $productionPath = Join-Path $root 'production.xml'
    $production.Save($productionPath)
    & "$PSScriptRoot/Test-PackageManifest.ps1" -ManifestPath $productionPath -Production `
        -ExpectedName 'Example.Shoutkit' -ExpectedPublisher 'CN=Example Publisher'
    $failed = $false
    try { & "$PSScriptRoot/Test-PackageManifest.ps1" -ManifestPath $productionPath -Production `
        -ExpectedName 'Wrong.Name' -ExpectedPublisher 'CN=Example Publisher' } catch { $failed = $true }
    if (!$failed) { throw 'Unexpected production package identity passed validation.' }
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
