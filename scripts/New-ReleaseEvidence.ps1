[CmdletBinding()]
param(
    [ValidateSet('Headless', 'ReleaseCandidate')][string] $Profile = 'Headless', [string] $SourceCommit,
    [Parameter(Mandatory)][string[]] $TestResults, [Parameter(Mandatory)][string[]] $Sarif,
    [int] $ExpectedTestResultCount = 0, [int] $ExpectedSarifCount = 0,
    [string] $Sbom, [string] $PublishDirectory, [string] $Package, [string] $NativeQa,
    [string] $OutputDirectory = 'out/release-evidence'
)
$ErrorActionPreference = 'Stop'; $repository = Split-Path $PSScriptRoot -Parent
function Resolve-Evidence([string] $path) { $item = Get-Item -LiteralPath $path -ErrorAction Stop; if ($item.PSIsContainer) { throw "Evidence must be a file: $path" }; $item }
function Get-FileEvidence([IO.FileInfo] $item) { @{ path=$item.FullName; length=$item.Length; modifiedAtUtc=$item.LastWriteTimeUtc.ToString('O'); sha256=(Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
function Read-TestResult([IO.FileInfo] $item) {
    [xml]$document = Get-Content $item.FullName -Raw
    $summary = @($document.SelectNodes("//*[local-name()='ResultSummary']")); $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($summary.Count -ne 1 -or $summary[0].outcome -ne 'Completed') { throw "TRX run did not complete: $($item.FullName)" }
    $counters = @($summary[0].SelectNodes(".//*[local-name()='Counters']")); if ($counters.Count -ne 1 -or $results.Count -eq 0) { throw "TRX has no unique, nonempty result set: $($item.FullName)" }
    $failed = @($results | Where-Object outcome -ne 'Passed').Count
    $nonPass = (@('error','timeout','aborted','inconclusive','notExecuted','disconnected','warning','notRunnable') | ForEach-Object { if ($counters[0].HasAttribute($_)) { [int]$counters[0].GetAttribute($_) } else { 0 } } | Measure-Object -Sum).Sum
    if ([int]$counters[0].total -ne $results.Count -or [int]$counters[0].executed -ne $results.Count -or [int]$counters[0].passed -ne ($results.Count-$failed) -or [int]$counters[0].failed -ne $failed -or $nonPass -ne 0) { throw "TRX counters disagree with results: $($item.FullName)" }
    (Get-FileEvidence $item) + @{ total=$results.Count; passed=$results.Count-$failed; failedOrOther=$failed }
}
$optionalPaths = @($Sbom,$Package,$NativeQa) | Where-Object { $_ }; $allPaths = @($TestResults)+@($Sarif)+@($optionalPaths)
$resolved = @($allPaths | ForEach-Object { Resolve-Evidence $_ }); if ($resolved.Count -ne @($resolved.FullName | Sort-Object -Unique).Count) { throw 'Duplicate evidence paths are not allowed.' }
if ($ExpectedTestResultCount -gt 0 -and $TestResults.Count -ne $ExpectedTestResultCount) { throw 'Unexpected TRX evidence count.' }
if ($ExpectedSarifCount -gt 0 -and $Sarif.Count -ne $ExpectedSarifCount) { throw 'Unexpected SARIF evidence count.' }
$initialHashes=@{}; foreach($item in $resolved){$initialHashes[$item.FullName]=(Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
foreach($group in @(@($TestResults),@($Sarif))){$hashes=@($group|ForEach-Object{(Get-FileHash -LiteralPath (Resolve-Evidence $_).FullName -Algorithm SHA256).Hash});if($hashes.Count -ne @($hashes|Sort-Object -Unique).Count){throw 'Duplicate evidence content is not allowed within an evidence class.'}}
$commit=& git -C $repository rev-parse HEAD 2>$null; if(!$SourceCommit){$SourceCommit=$commit}; if($SourceCommit -notmatch '^[0-9a-fA-F]{40}$' -or $commit -cne $SourceCommit){throw 'SourceCommit must exactly match the checked-out HEAD.'}
$tests=@($TestResults|ForEach-Object{Read-TestResult (Resolve-Evidence $_)}); $sarifFiles=@($Sarif|ForEach-Object{Resolve-Evidence $_})
$sarifStatus='Pass';$sarifError=$null; try{& "$PSScriptRoot/Test-Sarif.ps1" -Path @($sarifFiles.FullName)}catch{$sarifStatus='Fail';$sarifError=$_.Exception.Message}
$sbomEvidence=@{status='NotProvided'}; if($Sbom){$item=Resolve-Evidence $Sbom;try{& "$PSScriptRoot/Test-Sbom.ps1" -ManifestPath $item.FullName -DropPath $PublishDirectory;$sbomEvidence=(Get-FileEvidence $item)+@{status='Pass'}}catch{$sbomEvidence=(Get-FileEvidence $item)+@{status='Fail';error=$_.Exception.Message}}}
$packageEvidence=@{status='NotProvided'};if($Package){$item=Resolve-Evidence $Package;$signature=Get-AuthenticodeSignature $item.FullName;$packageEvidence=(Get-FileEvidence $item)+@{status='Recorded';signatureStatus=$signature.Status.ToString();signerThumbprint=$signature.SignerCertificate?.Thumbprint}}
$nativeEvidence=@{status='NotProvided'};$nativeReport=$null;if($NativeQa){$item=Resolve-Evidence $NativeQa;try{& "$PSScriptRoot/Test-NativeQaReport.ps1" -ReportPath $item.FullName -RequireComplete -ExpectedSourceCommit $SourceCommit;$nativeReport=Get-Content $item.FullName -Raw|ConvertFrom-Json -AsHashtable;$nativeEvidence=(Get-FileEvidence $item)+@{status='Pass'}}catch{$nativeEvidence=(Get-FileEvidence $item)+@{status='Incomplete';error=$_.Exception.Message}}}
if($nativeReport -and $Package){$recorded=@($nativeReport.artifacts|Where-Object{$_.kind -eq 'package' -and $_.supplied -eq $true});if($recorded.Count -ne 1 -or $recorded[0].sha256 -cne $packageEvidence.sha256){$nativeEvidence.status='Incomplete';$nativeEvidence.error='Native QA package hash does not match the candidate.'}}
foreach($item in $resolved){if($initialHashes[$item.FullName] -cne (Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()){throw "Evidence changed during aggregation: $($item.FullName)"}}
$gitStatus=@(& git -C $repository status --porcelain=v1 --untracked-files=all 2>$null);$testFailures=($tests|Measure-Object failedOrOther -Sum).Sum;$automatedStatus=if($testFailures -eq 0 -and $sarifStatus -eq 'Pass'){'Pass'}else{'Fail'}
$productionStatus='Incomplete'
$report=@{schemaVersion=1;generatedAtUtc=(Get-Date).ToUniversalTime().ToString('O');profile=$Profile;sourceCommit=$SourceCommit;workingTreeDirty=$gitStatus.Count-ne 0;automatedStatus=$automatedStatus;productionStatus=$productionStatus;tests=$tests;sarif=@{status=$sarifStatus;error=$sarifError;files=@($sarifFiles|ForEach-Object{Get-FileEvidence $_})};sbom=$sbomEvidence;package=$packageEvidence;nativeQa=$nativeEvidence;limitations=@('TRX and SARIF files do not cryptographically identify the source commit, so this tool cannot declare production readiness.','Final MSIX identity, architecture, version, and expected signer still require bound release attestation.','Headless evidence is not a substitute for native accessibility/playback QA.')}
$runDirectory=Join-Path ([IO.Path]::GetFullPath($OutputDirectory,$repository)) ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $runDirectory -Force|Out-Null;$reportPath=Join-Path $runDirectory 'release-evidence.json';$temporary="$reportPath.tmp"
try{$report|ConvertTo-Json -Depth 12|Set-Content $temporary;Move-Item $temporary $reportPath}finally{Remove-Item $temporary -Force -ErrorAction SilentlyContinue};Write-Host "Release evidence: $reportPath";Write-Host "Automated: $automatedStatus; production: $productionStatus"
if($automatedStatus -ne 'Pass'){throw 'Automated release evidence contains failures.'};if($Profile -eq 'ReleaseCandidate' -and $productionStatus -ne 'Ready'){throw 'Release-candidate evidence is incomplete.'}
