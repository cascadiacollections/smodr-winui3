$ErrorActionPreference = 'Stop'
$Architecture = if ($args.Count -gt 0) { [string]$args[0] } else { '' }
$DotnetArgs = @($args | Select-Object -Skip 1)
if ($Architecture -notin @('ARM64', 'x64')) {
    throw 'First argument must be ARM64 or x64.'
}
$repository = Split-Path -Parent $PSScriptRoot
$requiredVersion = (Get-Content -LiteralPath (Join-Path $repository 'global.json') -Raw |
    ConvertFrom-Json).sdk.version

if (-not $DotnetArgs -or $DotnetArgs.Count -eq 0) {
    throw 'Pass a dotnet command, for example: ARM64 build smodr.slnx'
}

$architectureKey = $Architecture.ToUpperInvariant()
$override = [Environment]::GetEnvironmentVariable("SHOUTKIT_DOTNET_$architectureKey")
$sharedOverride = [Environment]::GetEnvironmentVariable('SHOUTKIT_DOTNET')
$localFolder = if ($Architecture -eq 'ARM64') { 'dotnet11-rc-sdk' } else { 'dotnet11-rc-x64' }
$localSdk = Join-Path $repository "..\$localFolder\runtime\dotnet.exe"

$candidates = @($override, $sharedOverride)
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
if ([string]::Equals($Architecture, $hostArchitecture, [StringComparison]::OrdinalIgnoreCase)) {
    $candidates += Join-Path $repository '.dotnet/dotnet.exe'
}
$candidates += $localSdk
if ([string]::Equals($Architecture, $hostArchitecture, [StringComparison]::OrdinalIgnoreCase)) {
    if ($env:DOTNET_ROOT) {
        $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    }
    $onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
}

foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    $version = & $resolved --version 2>$null
    if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne $requiredVersion) { continue }

    & $resolved @DotnetArgs
    exit $LASTEXITCODE
}

throw "No $Architecture .NET SDK $requiredVersion found. Install the pinned SDK or set SHOUTKIT_DOTNET_$architectureKey to its dotnet.exe."
