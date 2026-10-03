[CmdletBinding()]
param(
    [string] $ManifestPath = 'smodr/Package.appxmanifest',
    [switch] $Production,
    [string] $ExpectedName,
    [string] $ExpectedPublisher
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$path = [IO.Path]::GetFullPath($ManifestPath, $repository)
[xml] $manifest = Get-Content -LiteralPath $path -Raw
$package = $manifest.SelectSingleNode("/*[local-name()='Package']")
$identity = $package.SelectSingleNode("*[local-name()='Identity']")
if (!$identity.Name -or !$identity.Publisher -or !$identity.Version) { throw 'Package identity is incomplete.' }
$version = [Version]$identity.Version
if ($version.Build -lt 0 -or $version.Revision -lt 0 -or @($version.Major, $version.Minor, $version.Build, $version.Revision | Where-Object { $_ -gt 65535 }).Count -ne 0) {
    throw "Package version is not a four-part MSIX version: $($identity.Version)"
}
$applications = @($package.SelectNodes("*[local-name()='Applications']/*[local-name()='Application']"))
if ($applications.Count -ne 1 -or !$applications[0].Executable -or !$applications[0].EntryPoint) {
    throw 'Manifest must contain one executable application.'
}
$capabilities = @($package.SelectNodes("*[local-name()='Capabilities']/*") | ForEach-Object { $_.Name })
if ($capabilities.Count -ne 1 -or $capabilities[0] -ne 'runFullTrust') {
    throw "Unexpected package capabilities: $($capabilities -join ', ')"
}
$protocols = @($package.SelectNodes(".//*[local-name()='Protocol']") | ForEach-Object { $_.Name })
if ($protocols.Count -ne 1 -or $protocols[0] -cne 'holmdel') { throw 'Expected exactly the holmdel protocol registration.' }
$families = @($package.SelectNodes("*[local-name()='Dependencies']/*[local-name()='TargetDeviceFamily']"))
if (@($families | Where-Object Name -eq 'Windows.Desktop').Count -ne 1) { throw 'Windows.Desktop target family is required.' }
if ($Production) {
    if ([string]::IsNullOrWhiteSpace($ExpectedName) -or [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
        throw 'Production validation requires the expected package name and publisher.'
    }
    if ($identity.Name -cne $ExpectedName -or $identity.Publisher -cne $ExpectedPublisher) {
        throw 'Package identity does not exactly match the expected production identity.'
    }
}
Write-Host "Package manifest valid: $($identity.Name), $($identity.Publisher), $($identity.Version)"
