[CmdletBinding()]
param(
    [string] $OutputDirectory = 'out/native-qa',
    [string] $ExecutablePath,
    [string] $PackagePath
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$casesPath = Join-Path $repository 'qa/native-qa-cases.json'
$cases = @(Get-Content -LiteralPath $casesPath -Raw | ConvertFrom-Json -AsHashtable)
if ($cases.Count -eq 0 -or @($cases.id | Sort-Object -Unique).Count -ne $cases.Count) {
    throw 'Native QA case catalog is empty or has duplicate identifiers.'
}
$root = [IO.Path]::GetFullPath($OutputDirectory, $repository)
$run = Join-Path $root ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
function Get-Artifact([string] $kind, [string] $path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return @{ kind = $kind; supplied = $false } }
    $item = Get-Item -LiteralPath $path -ErrorAction Stop
    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName
    return @{
        kind = $kind; supplied = $true; path = $item.FullName
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        signatureStatus = $signature.Status.ToString()
        signerThumbprint = $signature.SignerCertificate?.Thumbprint
    }
}
$commit = (& git -C $repository rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $commit = $null }
$report = @{
    schemaVersion = 1; runId = Split-Path $run -Leaf
    createdAtUtc = (Get-Date).ToUniversalTime().ToString('O'); completedAtUtc = $null
    sourceCommit = $commit; machine = $env:COMPUTERNAME
    os = [Environment]::OSVersion.VersionString; architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    artifacts = @((Get-Artifact 'executable' $ExecutablePath), (Get-Artifact 'package' $PackagePath))
    cases = @($cases | ForEach-Object { @{ id = $_.id; area = $_.area; title = $_.title; status = 'NotRun'; notes = $null; recordedAtUtc = $null } })
}
$reportPath = Join-Path $run 'native-qa.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
& "$PSScriptRoot/Test-NativeQaReport.ps1" -ReportPath $reportPath
Write-Host "Native QA run created: $reportPath"
