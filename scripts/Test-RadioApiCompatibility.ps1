#requires -Version 7.2
[CmdletBinding()]
param(
    [string] $DotnetPath = 'dotnet',
    [string] $BaselinePackage
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
if (-not $BaselinePackage) { $BaselinePackage = Join-Path $repository 'out/radio-packages/Cascadia.RadioBrowser.0.1.0.nupkg' }
$BaselinePackage = [IO.Path]::GetFullPath($BaselinePackage)
if (-not (Test-Path -LiteralPath $BaselinePackage -PathType Leaf)) { throw "Missing baseline package: $BaselinePackage" }
$fixture = Join-Path $repository ('out/api-compat-fixture/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$project = Join-Path $fixture 'BreakingSdk.csproj'
[IO.File]::WriteAllText($project, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <AssemblyName>Cascadia.RadioBrowser</AssemblyName>
    <RootNamespace>Cascadia.RadioBrowser</RootNamespace>
    <PackageId>Cascadia.RadioBrowser</PackageId>
    <Version>0.1.1</Version>
    <EnablePackageValidation>true</EnablePackageValidation>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <Description>Deliberately breaking offline compatibility fixture.</Description>
  </PropertyGroup>
</Project>
'@)
[IO.File]::WriteAllText((Join-Path $fixture 'BreakingChangeProbe.cs'), 'namespace Cascadia.RadioBrowser; public sealed class BreakingChangeProbe;')
Push-Location (Join-Path $repository 'Cascadia.Radio.Tests')
try {
    & $DotnetPath restore $project -p:NuGetAudit=false --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "Compatibility fixture restore failed ($LASTEXITCODE)." }
    $output = (& $DotnetPath pack $project -c Release --no-restore "-p:PackageValidationBaselinePath=$BaselinePackage" -o (Join-Path $fixture 'packages') 2>&1 | Out-String)
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $fixture 'validation.log'), $output)
    if ($code -eq 0 -or $output -notmatch 'CP0001') { throw "API compatibility failed to detect the deliberately removed public API. See $fixture/validation.log" }
    Write-Host 'API compatibility negative fixture passed: removed public types were rejected (CP0001).'
    # The intentional failed pack must not cause a successful CI PowerShell step to exit 1.
    $global:LASTEXITCODE = 0
}
finally { Pop-Location }
