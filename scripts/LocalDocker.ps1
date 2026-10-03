# Dot-sourceable availability helpers. Probes never start/unpause Docker or modify containers.
function Resolve-LocalDockerCommand {
    param([string] $DockerPath = 'docker')
    # Windows can expose both docker.exe and an extensionless docker shim.
    Get-Command $DockerPath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-LocalDockerStatus {
    [CmdletBinding()]
    param([string] $DockerPath = 'docker', [scriptblock] $Probe)
    if (-not $Probe) {
        $command = Resolve-LocalDockerCommand -DockerPath $DockerPath
        if (-not $command) { return [pscustomobject]@{ Available = $false; Reason = 'not-installed'; OS = ''; Architecture = '' } }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo.FileName = $command.Source
        $process.StartInfo.ArgumentList.Add('info')
        $process.StartInfo.ArgumentList.Add('--format')
        $process.StartInfo.ArgumentList.Add('{{.OSType}}|{{.Architecture}}')
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        try {
            [void]$process.Start()
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(10000)) {
                $process.Kill($true) # Only our own stalled info probe, never the Docker daemon.
                return [pscustomobject]@{ Available = $false; Reason = 'probe-timeout'; OS = ''; Architecture = '' }
            }
            $result = @{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult(); Error = $stderr.GetAwaiter().GetResult() }
        }
        catch { return [pscustomobject]@{ Available = $false; Reason = 'unreachable'; OS = ''; Architecture = '' } }
        finally { $process.Dispose() }
    }
    else { $result = & $Probe }
    if ($result.ExitCode -ne 0) {
        $reason = if ($result.Error -match 'paused') { 'paused' } else { 'unreachable' }
        return [pscustomobject]@{ Available = $false; Reason = $reason; OS = ''; Architecture = '' }
    }
    $parts = $result.Output.Trim().Split('|')
    if ($parts.Count -ne 2 -or $parts[0] -ne 'linux') {
        return [pscustomobject]@{ Available = $false; Reason = 'linux-containers-required'; OS = $parts[0]; Architecture = '' }
    }
    return [pscustomobject]@{ Available = $true; Reason = 'ready'; OS = $parts[0]; Architecture = $parts[1] }
}

function Get-LocalDockerBuildArguments {
    param([ValidateSet('tests', 'full', 'compat')][string] $Target = 'tests',
        [string] $RepositoryRoot, [string] $NuGetSource = 'https://www.nuget.org/api/v2/')
    if ($Target -eq 'compat') { $file = '.devcontainer/Dockerfile'; $stage = 'validation' }
    else { $file = 'scripts/radio-sdk.Dockerfile'; $stage = $Target }
    return @('build', '--progress', 'plain', '--target', $stage, '--build-arg', "RADIO_NUGET_SOURCE=$NuGetSource",
        '-f', (Join-Path $RepositoryRoot $file), '-t', "shoutkit-radio-local-$Target", $RepositoryRoot)
}
