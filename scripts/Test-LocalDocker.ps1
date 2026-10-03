#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('tests', 'full', 'compat')][string] $Target = 'tests',
    [switch] $IfAvailable,
    [switch] $NativeFallback,
    [switch] $CheckOnly,
    [switch] $ForceRetest,
    [string] $OutputRoot,
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
$run = if ($ForceRetest) { [Guid]::NewGuid().ToString('N') } else { 'cached' }
$arguments = Get-LocalDockerBuildArguments -Target $Target -RepositoryRoot $repository -NuGetSource $NuGetSource -ValidationRun $run
& $DockerPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Docker $Target validation failed ($LASTEXITCODE)." }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repository 'out/docker-validation' }
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "$Target/$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmss'))-$([Guid]::NewGuid().ToString('N'))"
[void][IO.Directory]::CreateDirectory($output)
$container = (& $DockerPath create "shoutkit-radio-local-$Target" /bin/true | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $container -notmatch '^[a-f0-9]{64}$') { throw 'Creating evidence-only Docker container failed.' }
try {
    Copy-LocalDockerEvidence -DockerPath $DockerPath -ContainerId $container -ArtifactRoot '/artifacts' -OutputDirectory $output -IncludePackages:($Target -eq 'full')
    & $DockerPath cp "${container}:/artifacts/validation-run-id" $output
    if ($LASTEXITCODE -ne 0) { throw 'Copying validation identity failed.' }
    $validationRun = (Get-Content -LiteralPath (Join-Path $output 'validation-run-id') -Raw).Trim()
    if ($ForceRetest -and $validationRun -ne $run) { throw 'Exported image does not match the requested fresh validation run.' }
    $imageId = (& $DockerPath inspect --format '{{.Image}}' $container | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Reading evidence image identity failed.' }
    @{ Target = $Target; Architecture = $status.Architecture; ImageId = $imageId;
        CapturedAtUtc = [DateTime]::UtcNow.ToString('O'); ForcedRetestRequested = [bool]$ForceRetest;
        ValidationRun = $validationRun } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'evidence.json') -Encoding utf8
}
finally {
    & $DockerPath rm $container | Out-Null # Only the stopped container created above.
    if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove task evidence container $container." }
}
Write-Host "Docker $Target validation passed. Evidence: $output. Locally tagged image only; nothing pushed."
