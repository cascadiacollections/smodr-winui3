#requires -Version 7.2
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'LocalDocker.ps1')
$cases = @(
    @{ ExitCode = 0; Output = 'linux|aarch64'; Error = ''; Expected = 'ready' },
    @{ ExitCode = 0; Output = 'linux|x86_64'; Error = ''; Expected = 'ready' },
    @{ ExitCode = 1; Output = ''; Error = 'Docker Desktop is manually paused.'; Expected = 'paused' },
    @{ ExitCode = 1; Output = ''; Error = 'Cannot connect'; Expected = 'unreachable' },
    @{ ExitCode = 0; Output = 'windows|x86_64'; Error = ''; Expected = 'linux-containers-required' },
    @{ ExitCode = 0; Output = ''; Error = ''; Expected = 'linux-containers-required' }
)
foreach ($case in $cases) {
    $status = Get-LocalDockerStatus -Probe { $case }
    if ($status.Reason -ne $case.Expected -or $status.Available -ne ($case.Expected -eq 'ready')) { throw "Availability fixture failed: $($case.Expected)" }
}
$repository = Split-Path -Parent $PSScriptRoot
foreach ($target in @('tests', 'full', 'compat')) {
    $arguments = Get-LocalDockerBuildArguments -Target $target -RepositoryRoot $repository
    if ($arguments[0] -ne 'build' -or $arguments -contains 'push' -or $arguments -contains 'unpause') { throw "Unsafe Docker arguments: $target" }
    $stage = if ($target -eq 'compat') { 'validation' } else { $target }
    if ($arguments[3] -ne '--target' -or $arguments[4] -ne $stage) { throw "Incorrect Docker stage: $target" }
}
& {
    function Get-Command {
        param($Name, $CommandType, $ErrorAction)
        @([pscustomobject]@{ Source = 'C:/Docker/docker.exe' }, [pscustomobject]@{ Source = 'C:/Docker/docker' })
    }
    $resolved = Resolve-LocalDockerCommand
    if ($resolved.Source -ne 'C:/Docker/docker.exe') { throw 'Multiple executable matches must resolve to the first application.' }
}
Write-Host 'Passed 10 Docker availability/command fixtures. No Docker process was required.'
