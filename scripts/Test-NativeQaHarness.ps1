$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('shoutkit-native-qa-' + [Guid]::NewGuid().ToString('N'))
try {
    & "$PSScriptRoot/New-NativeQaRun.ps1" -OutputDirectory $root
    $reports = @(Get-ChildItem -LiteralPath $root -Filter native-qa.json -Recurse)
    if ($reports.Count -ne 1) { throw "Expected one native QA report, found $($reports.Count)." }
    $report = $reports[0].FullName
    $failed = $false
    try { & "$PSScriptRoot/Test-NativeQaReport.ps1" -ReportPath $report -RequireComplete } catch { $failed = $true }
    if (!$failed) { throw 'Incomplete native QA report was accepted.' }
    $before = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
    $failed = $false
    try { & "$PSScriptRoot/Update-NativeQaRun.ps1" -ReportPath $report -CaseId install.clean -Status Pass -Notes '   ' } catch { $failed = $true }
    if (!$failed -or (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash -cne $before) {
        throw 'Invalid native QA update changed the canonical report.'
    }
    & "$PSScriptRoot/Update-NativeQaRun.ps1" -ReportPath $report -CaseId install.clean -Status Blocked -Notes 'Fixture evidence.'
    $document = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (($document.cases | Where-Object id -eq 'install.clean').status -ne 'Blocked') { throw 'Native QA update was not durable.' }
    $artifact = Join-Path $root 'artifact.bin'
    Set-Content -LiteralPath $artifact -Value 'before'
    & "$PSScriptRoot/New-NativeQaRun.ps1" -OutputDirectory $root -ExecutablePath $artifact
    $artifactReport = @(Get-ChildItem -LiteralPath $root -Filter native-qa.json -Recurse | Where-Object {
        $candidate = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        @($candidate.artifacts | Where-Object supplied -eq $true).Count -eq 1
    }).FullName
    if (@($artifactReport).Count -ne 1) { throw 'Could not identify the artifact-bound native QA report.' }
    Add-Content -LiteralPath $artifact -Value 'after'
    $failed = $false
    try { & "$PSScriptRoot/Test-NativeQaReport.ps1" -ReportPath $artifactReport } catch { $failed = $true }
    if (!$failed) { throw 'Mutated native QA artifact was accepted.' }
    Write-Host 'Native QA evidence harness passed.'
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
