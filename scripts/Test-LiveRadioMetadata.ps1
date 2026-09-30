param(
    [string] $KexpStreamUrl = 'http://live-mp3-128.kexp.org/kexp128.mp3',
    [string] $BrookdaleStreamUrl,
    [ValidateRange(1, 10)][int] $Samples = 3
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $PSScriptRoot 'dotnet-dev.ps1'
$project = Join-Path $repository 'smodr.RadioSmoke/smodr.RadioSmoke.csproj'

foreach ($station in @(
    @{ Name = 'KEXP'; Url = $KexpStreamUrl },
    @{ Name = 'Brookdale'; Url = $BrookdaleStreamUrl }
)) {
    if ([string]::IsNullOrWhiteSpace($station.Url)) { continue }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner ARM64 run --project $project --configuration Release -- $station.Url $station.Name $Samples
    if ($LASTEXITCODE -ne 0) { throw "$($station.Name) live metadata smoke failed." }
}
