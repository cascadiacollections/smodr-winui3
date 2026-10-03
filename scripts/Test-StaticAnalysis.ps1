[CmdletBinding()]
param(
    [ValidateSet('Portable', 'Windows')][string] $Scope = 'Portable',
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [string] $InspectCodePath,
    [string] $OutputDirectory = 'out/static-analysis'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
# Unique reports prevent a failed invocation from reusing an earlier clean verdict.
$run = Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$solution = if ($Scope -eq 'Portable') { 'Radio.Portable.slnx' } else { 'smodr.slnx' }
$working = if ($Scope -eq 'Portable') { Join-Path $repo 'portable' } else { $repo }
Push-Location $working
try {
    & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Required SDK is unavailable.' }
    & dotnet build $solution -c Release --no-restore --no-incremental -warnaserror `
        '-p:RunAnalyzers=true' '-p:RunAnalyzersDuringBuild=true' "-p:RadioAnalysisOutputDirectory=$run" `
        "-p:Platform=$(if ($Scope -eq 'Portable') { 'Any CPU' } else { $Platform })"
    if ($LASTEXITCODE -ne 0) { throw 'Roslyn analysis/build failed. Reports, when produced, are retained.' }
    $reports = @(Get-ChildItem -LiteralPath $run -Filter '*.sarif' | ForEach-Object FullName)
    if ($reports.Count -eq 0) { throw 'Compiler emitted no analysis reports.' }
    & "$PSScriptRoot/Test-Sarif.ps1" -Path $reports
    if ($InspectCodePath) {
        # Explicit opt-in executable: no editor, global tool, or silent download/install.
        $executable = (Get-Item -LiteralPath $InspectCodePath -ErrorAction Stop).FullName
        $jetbrainsReport = Join-Path $run 'resharper.sarif'
        & $executable $solution --build --format=Sarif --severity=WARNING "--output=$jetbrainsReport" `
            "--properties=Configuration=Release;Platform=$(if ($Scope -eq 'Portable') { 'Any CPU' } else { $Platform });RestoreLockedMode=true"
        if ($LASTEXITCODE -ne 0) { throw 'InspectCode failed (SDK/language support must match this scope).' }
        & "$PSScriptRoot/Test-Sarif.ps1" -Path $jetbrainsReport
    }
    Write-Host "Analysis evidence: $run"
} finally { Pop-Location }
