#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [ValidateRange(1, 30)][int] $Minutes = 1,
    [string] $DotnetPath = 'dotnet',
    [string] $OutputDirectory,
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository ('out/headless-soak/' + [Guid]::NewGuid().ToString('N')) }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    if (-not $SkipBuild) {
        & $DotnetPath build smodr.Tests/smodr.Tests.csproj -c Release --no-restore "-p:Platform=$Platform" -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Headless test build failed: $LASTEXITCODE" }
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $iteration = 0
    do {
        $iteration++
        & $DotnetPath test smodr.Tests/smodr.Tests.csproj -c Release --no-build --no-restore "-p:Platform=$Platform" --filter 'TestCategory=Soak|TestCategory=RuntimeRace' --results-directory $OutputDirectory --logger "trx;LogFileName=soak-$iteration.trx"
        if ($LASTEXITCODE -ne 0) { throw "Headless soak iteration $iteration failed: $LASTEXITCODE" }
    } while ($clock.Elapsed.TotalMinutes -lt $Minutes)
    [ordered]@{ schemaVersion = 1; platform = $Platform; iterations = $iteration; elapsedSeconds = $clock.Elapsed.TotalSeconds; capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); scope = 'Synthetic engine ownership, virtual-time recovery, bounded caches/history, real loopback HTTP; not native audio or WinUI.' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
    Write-Output "Headless soak passed $iteration iterations. Results: $OutputDirectory"
}
finally { Pop-Location }
