$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('radio-analysis-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $cases = @(
        @{ Name = 'clean'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @() }); Fails = $false },
        @{ Name = 'note'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ level = 'note' }) }); Fails = $false },
        @{ Name = 'warning'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ level = 'warning' }) }); Fails = $true },
        @{ Name = 'default-warning'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ ruleId = 'default' }) }); Fails = $true },
        @{ Name = 'rule-default'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture'; rules = @(@{ defaultConfiguration = @{ level = 'error' } }) } }; results = @(@{ ruleIndex = 0 }) }); Fails = $true },
        @{ Name = 'failed-invocation'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; invocations = @(@{ executionSuccessful = $false }) }); Fails = $true },
        @{ Name = 'notification'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; invocations = @(@{ toolExecutionNotifications = @(@{ level = 'error' }) }) }); Fails = $true },
        @{ Name = 'empty'; Runs = @(); Fails = $true }
        @{ Name = 'source-pragma'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ level = 'warning'; suppressions = @(@{ kind = 'inSource' }) }) }); Fails = $false },
        @{ Name = 'external-suppression'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ level = 'warning'; suppressions = @(@{ kind = 'external'; status = 'accepted' }) }) }); Fails = $true },
        @{ Name = 'rejected-source'; Runs = @(@{ tool = @{ driver = @{ name = 'fixture' } }; results = @(@{ level = 'warning'; suppressions = @(@{ kind = 'inSource'; status = 'rejected' }) }) }); Fails = $true }
    )
    foreach ($case in $cases) {
        $path = Join-Path $temporary ($case.Name + '.sarif')
        @{ version = '2.1.0'; runs = $case.Runs } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path
        $failed = $false
        try { & "$PSScriptRoot/Test-Sarif.ps1" -Path $path } catch { $failed = $true }
        if ($failed -ne $case.Fails) { throw "Incorrect verdict: $($case.Name)" }
    }
    Write-Host "Passed $($cases.Count) analysis gate fixtures."
} finally {
    # Only this harness's freshly created, exact private directory.
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
