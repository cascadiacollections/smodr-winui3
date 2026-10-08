#requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Version = '2.5.4-experimental',
    [string] $ToolkitVersion = '8.2.251219',
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [string] $DotnetPath = 'dotnet',
    [string] $Source = 'https://www.nuget.org/api/v2/'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+[-A-Za-z0-9.]*$') { throw 'Invalid SDK version.' }
if ($ToolkitVersion -notmatch '^\d+\.\d+\.\d+[-A-Za-z0-9.]*$') { throw 'Invalid Toolkit version.' }
$repository = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repository ('out/winui-experiment/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($destination)
Push-Location $repository
try {
    # Copy current source, including new source files; exclude ignored outputs and unrelated user files.
    $files = @(& git ls-files) + @(& git ls-files --others --exclude-standard smodr scripts Cascadia.Radio.Services smodr.Tests)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate experiment source.' }
    foreach ($relative in ($files | Sort-Object -Unique)) {
        $sourceFile = Join-Path $repository $relative
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) { continue }
        $target = Join-Path $destination $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath $sourceFile -Destination $target
    }
    Push-Location $destination
    try {
        $properties = @("-p:Platform=$Platform", "-p:WindowsAppSdkVersion=$Version", "-p:WinUiToolkitVersion=$ToolkitVersion")
        & $DotnetPath restore smodr.slnx @properties --force-evaluate --source $Source
        if ($LASTEXITCODE -ne 0) { throw 'Experimental restore failed.' }
        ./scripts/Update-LicenseInventory.ps1
        & $DotnetPath build smodr.slnx -c Release --no-restore @properties '-p:EnableExperimentalWinUI=true' -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Experimental build failed.' }
        & $DotnetPath test smodr.slnx -c Release --no-build --no-restore @properties --results-directory results --logger trx
        if ($LASTEXITCODE -ne 0) { throw 'Experimental tests failed.' }
        ./scripts/Measure-HeadlessPerformance.ps1 -Platform $Platform -DotnetPath $DotnetPath -Runs 3 -SkipBuild -OutputDirectory (Join-Path $destination 'results/performance')
        [ordered]@{ sdk = $Version; toolkit = $ToolkitVersion; platform = $Platform; status = 'Pass'; scope = 'Headless build, tests and harness performance; no WinUI rendering or audio.' } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'results/comparison.json') -Encoding utf8
        Write-Output "SDK $Version / Toolkit $ToolkitVersion passed: $destination"
    }
    finally { Pop-Location }
}
finally { Pop-Location }
