[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ReportPath,
    [switch] $RequireComplete,
    [string] $ExpectedSourceCommit
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$catalog = @(Get-Content -LiteralPath (Join-Path $repository 'qa/native-qa-cases.json') -Raw | ConvertFrom-Json -AsHashtable)
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -AsHashtable
if ($report.schemaVersion -ne 1 -or !$report.runId -or !$report.createdAtUtc) { throw 'Invalid native QA report header.' }
if ($ExpectedSourceCommit -and $report.sourceCommit -cne $ExpectedSourceCommit) { throw 'Native QA source commit does not match.' }
$expected = @($catalog.id | Sort-Object)
$actual = @($report.cases.id | Sort-Object)
if (Compare-Object $expected $actual) { throw 'Native QA report does not match the current case catalog.' }
$allowed = @('NotRun', 'Pass', 'Fail', 'Blocked')
foreach ($case in $report.cases) {
    if ($case.status -notin $allowed) { throw "Invalid status for $($case.id): $($case.status)" }
    if ($case.status -ne 'NotRun' -and [string]::IsNullOrWhiteSpace($case.notes)) { throw "Evidence notes required for $($case.id)." }
}
foreach ($artifact in @($report.artifacts | Where-Object supplied -eq $true)) {
    $item = Get-Item -LiteralPath $artifact.path -ErrorAction Stop
    $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $artifact.sha256) { throw "Native QA artifact hash changed: $($artifact.kind)" }
    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName
    if ($signature.Status.ToString() -cne $artifact.signatureStatus -or
        $signature.SignerCertificate?.Thumbprint -cne $artifact.signerThumbprint) {
        throw "Native QA artifact signature changed: $($artifact.kind)"
    }
}
$notRun = @($report.cases | Where-Object status -eq 'NotRun').Count
$failed = @($report.cases | Where-Object status -eq 'Fail').Count
$blocked = @($report.cases | Where-Object status -eq 'Blocked').Count
if ($RequireComplete -and ($notRun -ne 0 -or $failed -ne 0 -or $blocked -ne 0)) {
    throw "Native QA incomplete: $notRun not run, $failed failed, $blocked blocked."
}
Write-Host "Native QA report valid: $notRun not run, $failed failed, $blocked blocked."
