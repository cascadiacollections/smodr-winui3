#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('tests', 'full', 'compat')][string] $Target = 'tests',
    [switch] $IfAvailable,
    [switch] $NativeFallback,
    [switch] $CheckOnly,
    [string] $DockerPath = 'docker',
    [string] $DotnetPath = 'dotnet',
    [string] $NuGetSource = 'https://www.nuget.org/api/v2/'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'LocalDocker.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$status = Get-LocalDockerStatus -DockerPath $DockerPath
if ($CheckOnly) { $status; return }
if (-not $status.Available) {
    if ($NativeFallback) {
        Write-Host "Docker validation not run ($($status.Reason)); running native portable .NET 10 tests instead."
        Push-Location (Join-Path $repository 'portable')
        try {
            $artifacts = Join-Path $repository 'out/native-portable'
            & $DotnetPath restore Radio.Portable.slnx --locked-mode --source $NuGetSource -p:NuGetAudit=false --artifacts-path $artifacts
            if ($LASTEXITCODE -ne 0) { throw 'Native portable restore failed.' }
            & $DotnetPath test Radio.Portable.slnx -c Release --no-restore --artifacts-path $artifacts -warnaserror
            if ($LASTEXITCODE -ne 0) { throw 'Native portable tests failed.' }
        }
        finally { Pop-Location }
        Write-Host 'Native portable tests passed. Docker/package/compatibility validation was NOT run.'
        return
    }
    if ($IfAvailable) { Write-Host "SKIPPED Docker validation: $($status.Reason). No validation was performed."; return }
    throw "Docker validation unavailable: $($status.Reason). Use -IfAvailable to explicitly skip or -NativeFallback for portable tests."
}
Write-Host "Running Linux Docker $Target validation ($($status.Architecture)); no UI, live streams or publishing."
$arguments = Get-LocalDockerBuildArguments -Target $Target -RepositoryRoot $repository -NuGetSource $NuGetSource
& $DockerPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Docker $Target validation failed ($LASTEXITCODE)." }
Write-Host "Docker $Target validation passed. Locally tagged image only; nothing pushed."
