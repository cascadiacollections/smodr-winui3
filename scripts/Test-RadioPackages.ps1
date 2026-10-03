#requires -Version 7.2
[CmdletBinding()]
param(
    [string] $DotnetPath = 'dotnet',
    [string] $OutputDirectory,
    [string] $BaselineDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository 'out/radio-packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$projects = @('Cascadia.Radio', 'Cascadia.Radio.Metadata', 'Cascadia.Radio.Services', 'Cascadia.RadioBrowser')
# Stable SDK selection is scoped to portable tests and samples, not the preview Windows solution.
Push-Location (Join-Path $repository 'Cascadia.Radio.Tests')
try {
    foreach ($project in $projects) {
        $arguments = @('pack', "../$project/$project.csproj", '-c', 'Release', '--no-restore', '-o', $OutputDirectory, '-warnaserror')
        if ($BaselineDirectory) {
            $baseline = Join-Path ([IO.Path]::GetFullPath($BaselineDirectory)) "$project.0.1.0.nupkg"
            if (-not (Test-Path -LiteralPath $baseline -PathType Leaf)) { throw "Missing compatibility baseline: $baseline" }
            $arguments += "-p:PackageValidationBaselinePath=$baseline"
        }
        & $DotnetPath @arguments
        if ($LASTEXITCODE -ne 0) { throw "Package validation failed for $project ($LASTEXITCODE)." }
    }
}
finally { Pop-Location }
Push-Location (Join-Path $repository 'samples/RadioSdk.Consumer')
try {
    # Fresh isolated cache avoids accidentally testing an older local package with the same draft version.
    $consumerCache = Join-Path $OutputDirectory ('consumer-cache/' + [Guid]::NewGuid().ToString('N'))
    & $DotnetPath restore --source $OutputDirectory --packages $consumerCache -p:NuGetAudit=false --force
    if ($LASTEXITCODE -ne 0) { throw "Package consumer restore failed ($LASTEXITCODE)." }
    & $DotnetPath build -c Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Package consumer build failed ($LASTEXITCODE)." }
    & $DotnetPath run -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Package consumer failed ($LASTEXITCODE)." }
}
finally { Pop-Location }
