[CmdletBinding()]
param([Parameter(Mandatory)][string] $ReportPath, [switch] $RequireComplete)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$catalog = @(Get-Content -LiteralPath (Join-Path $repository 'qa/native-qa-cases.json') -Raw | ConvertFrom-Json -AsHashtable)
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable
if ($report.schemaVersion -ne 1 -or !$report.runId -or !$report.createdAtUtc) { throw 'Invalid native QA report header.' }
$expected = @($catalog.id | Sort-Object)
$actual = @($report.cases.id | Sort-Object)
if (Compare-Object $expected $actual) { throw 'Native QA report does not match the current case catalog.' }
$allowed = @('NotRun', 'Pass', 'Fail', 'Blocked')
foreach ($case in $report.cases) {
    if ($case.status -notin $allowed) { throw "Invalid status for $($case.id): $($case.status)" }
    if ($case.status -ne 'NotRun' -and [string]::IsNullOrWhiteSpace($case.notes)) { throw "Evidence notes required for $($case.id)." }
}
$notRun = @($report.cases | Where-Object status -eq 'NotRun').Count
$failed = @($report.cases | Where-Object status -eq 'Fail').Count
$blocked = @($report.cases | Where-Object status -eq 'Blocked').Count
if ($RequireComplete -and ($notRun -ne 0 -or $failed -ne 0 -or $blocked -ne 0)) {
    throw "Native QA incomplete: $notRun not run, $failed failed, $blocked blocked."
}
Write-Host "Native QA report valid: $notRun not run, $failed failed, $blocked blocked."
