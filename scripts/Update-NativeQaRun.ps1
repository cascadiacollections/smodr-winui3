[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ReportPath,
    [Parameter(Mandatory)][string] $CaseId,
    [Parameter(Mandatory)][ValidateSet('Pass', 'Fail', 'Blocked')][string] $Status,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $Notes
)
$ErrorActionPreference = 'Stop'
$path = (Get-Item -LiteralPath $ReportPath -ErrorAction Stop).FullName
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
$matches = @($report.cases | Where-Object id -eq $CaseId)
if ($matches.Count -ne 1) { throw "Unknown or duplicate native QA case: $CaseId" }
$trimmedNotes = $Notes.Trim()
if ([string]::IsNullOrWhiteSpace($trimmedNotes)) { throw 'Evidence notes cannot be whitespace.' }
$matches[0].status = $Status
$matches[0].notes = $trimmedNotes
$matches[0].recordedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
if (@($report.cases | Where-Object status -eq 'NotRun').Count -eq 0) {
    $report.completedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
} else {
    $report.completedAtUtc = $null
}
$temporary = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary
    & "$PSScriptRoot/Test-NativeQaReport.ps1" -ReportPath $temporary
    Move-Item -LiteralPath $temporary -Destination $path -Force
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
