param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath
)

$resolved = (Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path
$existing = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $resolved })
if ($existing.Count -ne 0) {
    throw "Close the existing Shoutkit instance before running this smoke test."
}

$first = Start-Process -FilePath $resolved -PassThru -WindowStyle Normal
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $first.Refresh()
    } while (-not $first.HasExited -and $first.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($first.HasExited -or $first.MainWindowHandle -eq 0 -or -not $first.Responding) {
        throw "The first launch did not produce a responsive window."
    }

    $second = Start-Process -FilePath $resolved -PassThru -WindowStyle Normal
    if (-not $second.WaitForExit(10000)) {
        throw "The second launch did not redirect and exit within 10 seconds."
    }

    $first.Refresh()
    $running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $resolved })
    if ($first.HasExited -or -not $first.Responding -or $running.Count -ne 1) {
        throw "The first window did not remain the sole responsive instance."
    }

    Write-Output "PASS: responsive window and single-instance activation ($($first.Id))."
}
finally {
    $first.Refresh()
    if (-not $first.HasExited) {
        Stop-Process -Id $first.Id
    }
}
