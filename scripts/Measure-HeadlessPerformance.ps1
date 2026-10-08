#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [ValidateRange(1, 20)][int] $Runs = 5,
    [ValidateRange(200, 100000)][int] $ResourceCycles = 2000,
    [string] $DotnetPath = 'dotnet',
    [string] $OutputDirectory,
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository ('out/headless-performance/' + [Guid]::NewGuid().ToString('N')) }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $dotnetExecutable = (Get-Command $DotnetPath -ErrorAction Stop).Source
    if (-not $SkipBuild) {
        & $dotnetExecutable build smodr.HeadlessPerf/smodr.HeadlessPerf.csproj -c Release --no-restore "-p:Platform=$Platform" -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Performance harness build failed: $LASTEXITCODE" }
    }
    $assembly = Join-Path $repository "smodr.HeadlessPerf/bin/$Platform/Release/net11.0-windows10.0.22621.0/smodr.HeadlessPerf.dll"
    $summaries = @()
    foreach ($run in 1..$Runs) {
        $startInfo = [Diagnostics.ProcessStartInfo]::new($dotnetExecutable)
        $startInfo.ArgumentList.Add($assembly)
        $startInfo.ArgumentList.Add('--resource-cycles')
        $startInfo.ArgumentList.Add($ResourceCycles.ToString([Globalization.CultureInfo]::InvariantCulture))
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            if (-not $process.Start()) { throw 'Could not start the headless performance harness.' }
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Performance harness exceeded 60 seconds.' }
            $clock.Stop()
            $json = $stdout.GetAwaiter().GetResult()
            $errors = $stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw "Performance harness failed: $errors" }
            $report = $json | ConvertFrom-Json
            if ($report.schemaVersion -ne 2 -or $report.measurements.Count -ne 8 -or $report.architecture -ne $Platform -or $report.resourceSamples.Count -ne 11) { throw 'Unexpected performance report schema or process architecture.' }
            foreach ($metric in $report.measurements) {
                if ($metric.Samples -ne 25 -or -not [double]::IsFinite($metric.MinMs) -or -not [double]::IsFinite($metric.MedianMs) -or -not [double]::IsFinite($metric.P95Ms) -or $metric.MinMs -lt 0 -or $metric.MedianMs -lt $metric.MinMs -or $metric.P95Ms -lt $metric.MedianMs -or $metric.ManagedBytesPerOperation -lt 0) { throw 'Invalid performance measurement.' }
            }
            if ($report.resourceCycles -ne $ResourceCycles -or $report.resourceSamples[-1].cycle -ne $ResourceCycles) { throw 'Resource run was incomplete.' }
            foreach ($sample in $report.resourceSamples) {
                if ($sample.privateBytes -le 0 -or $sample.workingSetBytes -le 0 -or $sample.managedBytes -lt 0 -or $sample.handles -lt 0) { throw 'Invalid resource sample.' }
            }
            $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "run-$run.json") -Encoding utf8
            $summaries += [ordered]@{ run = $run; processAndEntireHarnessMs = $clock.Elapsed.TotalMilliseconds; measurements = $report.measurements; resourceSamples = $report.resourceSamples }
        }
        finally { $process.Dispose() }
    }
    [ordered]@{ schemaVersion = 1; platform = $Platform; runs = $summaries; limitation = 'Process time includes fixture setup and ALL benchmarks, not application startup time. No timing pass/fail thresholds.' } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
    Write-Output "Headless performance reports: $OutputDirectory"
}
finally { Pop-Location }
