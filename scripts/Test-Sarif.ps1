[CmdletBinding()]
param([Parameter(Mandatory)][string[]] $Path)
$ErrorActionPreference = 'Stop'
foreach ($reportPath in $Path) {
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
    if ($report.version -ne '2.1.0' -or !$report.ContainsKey('runs') -or @($report.runs).Count -eq 0) {
        throw "Missing/unsupported SARIF runs: $reportPath"
    }
    foreach ($run in $report.runs) {
        if (!$run.tool.driver.name) { throw "Missing SARIF tool identity: $reportPath" }
        foreach ($invocation in @($run.invocations)) {
            if ($null -ne $invocation -and $invocation.executionSuccessful -eq $false) {
                throw "Analysis did not complete: $reportPath"
            }
            foreach ($notification in @($invocation.toolExecutionNotifications)) {
                if ($notification.level -in @('warning', 'error')) { throw "Analysis notification: $($notification.message.text)" }
            }
        }
        foreach ($result in @($run.results)) {
            if ($null -eq $result) { continue }
            # Compiler pragmas remain reviewable in reports. Do not resurrect
            # intentional source suppressions; an external/unreviewed baseline is not a waiver.
            if (@($result.suppressions | Where-Object {
                $_.kind -eq 'inSource' -and (!$_.status -or $_.status -eq 'accepted')
            }).Count -gt 0) { continue }
            # SARIF's default result level is warning. Respect rule defaults too.
            $level = $result.level
            if (!$level -and $result.ContainsKey('ruleIndex')) {
                $rules = @($run.tool.driver.rules)
                if ($result.ruleIndex -lt 0 -or $result.ruleIndex -ge $rules.Count) { throw 'Invalid SARIF ruleIndex' }
                $level = $rules[$result.ruleIndex].defaultConfiguration.level
            }
            if (!$level) { $level = 'warning' }
            if ($level -in @('warning', 'error')) {
                throw "Static analysis $level [$($result.ruleId)]: $($result.message.text) ($reportPath)"
            }
        }
    }
}
Write-Host "SARIF gate passed ($($Path.Count) reports)."
