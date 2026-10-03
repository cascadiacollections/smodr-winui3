#requires -Version 7.2
[CmdletBinding()]
param([string] $DockerPath = 'docker', [string] $NuGetSource = 'https://www.nuget.org/api/v2/', [string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'LocalDocker.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$status = Get-LocalDockerStatus -DockerPath $DockerPath
if (-not $status.Available) { throw "Lifecycle Docker validation unavailable: $($status.Reason)." }
& $DockerPath build --progress plain --target lifecycle --build-arg "RADIO_NUGET_SOURCE=$NuGetSource" -f (Join-Path $repository '.devcontainer/Dockerfile') -t shoutkit-radio-local-lifecycle $repository
if ($LASTEXITCODE -ne 0) { throw 'Building lifecycle image failed.' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repository 'out/docker-validation' }
$run = [Guid]::NewGuid().ToString('N')
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "lifecycle/$run"
[void][IO.Directory]::CreateDirectory($output)
$volumes = @()
$container = ''
try {
    foreach ($kind in @('nuget', 'artifacts')) {
        $name = "shoutkit-lifecycle-$run-$kind"
        & $DockerPath volume create --label shoutkit.validation=lifecycle $name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Creating task $kind volume failed." }
        $volumes += $name
    }
    $container = (& $DockerPath create --user vscode --mount "type=volume,source=$($volumes[0]),target=/home/vscode/.nuget/packages" --mount "type=volume,source=$($volumes[1]),target=/var/tmp/shoutkit-radio-artifacts" shoutkit-radio-local-lifecycle | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $container -notmatch '^[a-f0-9]{64}$') { throw 'Creating lifecycle test container failed.' }
    & $DockerPath start --attach $container
    if ($LASTEXITCODE -ne 0) { throw 'Lifecycle hook execution failed.' }
    $exitCode = (& $DockerPath inspect --format '{{.State.ExitCode}}' $container | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $exitCode -ne '0') { throw "Lifecycle container failed: $exitCode." }
    Copy-LocalDockerEvidence -DockerPath $DockerPath -ContainerId $container -ArtifactRoot '/var/tmp/shoutkit-radio-artifacts' -OutputDirectory $output
    @{ Target = 'lifecycle'; Architecture = $status.Architecture; User = 'vscode';
        ColdAndWarmHooksPassed = $true; CapturedAtUtc = [DateTime]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'evidence.json') -Encoding utf8
    Write-Host "Cold/warm non-root lifecycle passed. Evidence: $output"
}
finally {
    if ($container -match '^[a-f0-9]{64}$') {
        & $DockerPath rm $container | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove task container $container." }
    }
    foreach ($volume in $volumes) {
        & $DockerPath volume rm $volume | Out-Null # Only freshly created, uniquely named task volumes.
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove task volume $volume." }
    }
}
