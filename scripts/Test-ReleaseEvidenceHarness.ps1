$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('shoutkit-release-evidence-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $trx = Join-Path $root 'pass.trx'
    @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="one" outcome="Passed" /></Results><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>
'@ | Set-Content -LiteralPath $trx
    $sarif = Join-Path $root 'clean.sarif'
    @{ version = '2.1.0'; runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @() }) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sarif
    & "$PSScriptRoot/New-ReleaseEvidence.ps1" -TestResults $trx -Sarif $sarif -OutputDirectory $root
    $reports = @(Get-ChildItem -LiteralPath $root -Filter release-evidence.json -Recurse)
    if ($reports.Count -ne 1) { throw "Expected one release report, found $($reports.Count)." }
    $report = Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json
    if ($report.automatedStatus -ne 'Pass' -or $report.productionStatus -ne 'Incomplete') {
        throw 'Headless-only evidence produced an incorrect readiness verdict.'
    }
    $duplicate = Join-Path $root 'duplicate.trx'
    Copy-Item -LiteralPath $trx -Destination $duplicate
    $failed = $false
    try { & "$PSScriptRoot/New-ReleaseEvidence.ps1" -TestResults @($trx, $duplicate) -Sarif $sarif -OutputDirectory $root } catch { $failed = $true }
    if (!$failed) { throw 'Duplicate evidence content was accepted.' }
    $failed = $false
    try { & "$PSScriptRoot/New-ReleaseEvidence.ps1" -Profile ReleaseCandidate -TestResults $trx -Sarif $sarif -ExpectedTestResultCount 1 -ExpectedSarifCount 1 -OutputDirectory $root } catch { $failed = $true }
    if (!$failed) { throw 'Unbound release-candidate evidence was accepted as ready.' }
    (Get-Content -LiteralPath $trx -Raw).Replace('outcome="Passed"', 'outcome="Failed"') | Set-Content -LiteralPath $trx
    $failed = $false
    try { & "$PSScriptRoot/New-ReleaseEvidence.ps1" -TestResults $trx -Sarif $sarif -OutputDirectory $root } catch { $failed = $true }
    if (!$failed) { throw 'Failed TRX was accepted.' }
    Write-Host 'Release evidence harness passed.'
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
