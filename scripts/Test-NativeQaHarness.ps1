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
    & "$PSScriptRoot/Update-NativeQaRun.ps1" -ReportPath $report -CaseId install.clean -Status Blocked -Notes 'Fixture evidence.'
    $document = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (($document.cases | Where-Object id -eq 'install.clean').status -ne 'Blocked') { throw 'Native QA update was not durable.' }
    Write-Host 'Native QA evidence harness passed.'
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
