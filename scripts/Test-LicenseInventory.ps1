#requires -Version 7.2
[CmdletBinding()]
param([string] $PublishedDirectory, [string] $MsixDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$generator = Join-Path $PSScriptRoot 'Update-LicenseInventory.ps1'
$repository = Split-Path -Parent $PSScriptRoot
$utf8 = [Text.UTF8Encoding]::new($false)
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("shoutkit-license-tests-" + [Guid]::NewGuid().ToString('N'))))
$cache = Join-Path $temporaryRoot 'packages'
$fixture = Join-Path $temporaryRoot 'repo'
$passed = 0

function Write-Fixture([string] $Path, [string] $Text) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Expect-Failure([scriptblock] $Action, [string] $MessagePattern) {
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        $caught = $true
        Assert-True ($_.Exception.Message -like $MessagePattern) "Unexpected error: $($_.Exception.Message)"
    }
    Assert-True $caught "Expected failure: $MessagePattern"
    $script:passed++
}

function Add-FixturePackage([string] $Id, [string] $License, [string] $Hash = 'fixture-hash') {
    $directory = Join-Path $cache ($Id.ToLowerInvariant() + '/1.0.0')
    Write-Fixture (Join-Path $directory ($Id.ToLowerInvariant() + '.nuspec')) @"
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>$Id</id><version>1.0.0</version><authors>Fixture author</authors>$License</metadata></package>
"@
    Write-Fixture (Join-Path $directory '.nupkg.metadata') (@{ version = 2; contentHash = $Hash } | ConvertTo-Json)
    # Deliberately different: signed archive hash is not NuGet's normalized lock hash.
    Write-Fixture (Join-Path $directory ($Id.ToLowerInvariant() + '.1.0.0.nupkg.sha512')) 'signed-archive-hash'
    return $directory
}

try {
    Write-Fixture (Join-Path $fixture 'LICENSE.txt') "Fixture application license`n"
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'smodr/Assets'))
    $first = Add-FixturePackage 'Fixture.Runtime' '<license type="expression">MIT</license>'
    Write-Fixture (Join-Path $first 'NOTICE.txt') "Shared notice`r`n"
    $second = Add-FixturePackage 'Fixture.FileLicense' '<license type="file">licenses/vendor.txt</license>'
    Write-Fixture (Join-Path $second 'licenses/vendor.txt') "Shared notice`n"
    $build = Add-FixturePackage 'Microsoft.Windows.SDK.BuildTools' '<licenseUrl>https://example.com/build-license</licenseUrl>'
    [void](Add-FixturePackage 'Microsoft.NETCore.App.Runtime.win-arm64' '<license type="expression">MIT</license>')
    [void](Add-FixturePackage 'Microsoft.NETCore.App.Runtime.win-x64' '<license type="expression">MIT</license>')
    $lock = [ordered]@{
        version = 2
        dependencies = [ordered]@{
            net11 = [ordered]@{
                'Fixture.Runtime' = @{ type = 'Direct'; resolved = '1.0.0'; contentHash = 'fixture-hash' }
                'Fixture.FileLicense' = @{ type = 'Transitive'; resolved = '1.0.0'; contentHash = 'fixture-hash' }
                'Microsoft.Windows.SDK.BuildTools' = @{ type = 'Direct'; resolved = '1.0.0'; contentHash = 'fixture-hash' }
                'Fixture.TestProject' = @{ type = 'Project' }
            }
            'net11/win-arm64' = @{
                'Fixture.Runtime' = @{ type = 'Direct'; resolved = '1.0.0'; contentHash = 'fixture-hash' }
            }
        }
    }
    $lockPath = Join-Path $fixture 'smodr/packages.lock.json'
    Write-Fixture $lockPath ($lock | ConvertTo-Json -Depth 8)
    $assets = @{
        packageFolders = @{ $cache = @{} }
        project = @{ frameworks = @{ net11 = @{ downloadDependencies = @(
            @{ name = 'Microsoft.NETCore.App.Runtime.win-arm64'; version = '[1.0.0, 1.0.0]' },
            @{ name = 'Microsoft.NETCore.App.Runtime.win-x64'; version = '[1.0.0, 1.0.0]' },
            @{ name = 'Microsoft.NETCore.App.Host.win-arm64'; version = '[1.0.0, 1.0.0]' }
        ) } } }
    }
    Write-Fixture (Join-Path $fixture 'smodr/obj/project.assets.json') ($assets | ConvertTo-Json -Depth 8)
    & $generator -RepositoryRoot $fixture | Out-Null
    & $generator -RepositoryRoot $fixture -Check | Out-Null
    $output = Join-Path $fixture 'smodr/Assets/SoftwareLicenses.txt'
    $jsonPath = Join-Path $fixture 'smodr/Assets/SoftwareLicenseInventory.json'
    $before = [IO.File]::ReadAllBytes($output)
    $manifest = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-True ($manifest.Packages.Count -eq 5) 'Must deduplicate RIDs and exclude Project/test dependencies and SDK host tools.'
    Assert-True (@($manifest.Packages | Where-Object { $_.BuildOnly }).Count -eq 1) 'Known build tools must be labeled.'
    Assert-True (([IO.File]::ReadAllText($output) -split 'Shared notice').Count -eq 2) 'Identical notice text must appear once.'
    Assert-True (-not [IO.File]::ReadAllText($output).Contains($temporaryRoot)) 'Output must not include machine paths.'
    & $generator -RepositoryRoot $fixture | Out-Null
    Assert-True ([Convert]::ToBase64String($before) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Generation must be byte-deterministic.'
    $passed++

    Write-Fixture (Join-Path $first 'NOTICE.txt') 'Changed notice'
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Stale SoftwareLicenses.txt*'
    Assert-True ([Convert]::ToBase64String($before) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))) 'Check must never overwrite output.'
    Write-Fixture (Join-Path $first 'NOTICE.txt') "Shared notice`n"

    $lock.dependencies.net11['Fixture.Runtime'].contentHash = 'wrong-hash'
    $lock.dependencies['net11/win-arm64']['Fixture.Runtime'].contentHash = 'wrong-hash'
    Write-Fixture $lockPath ($lock | ConvertTo-Json -Depth 8)
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Restored package hash differs*'
    $lock.dependencies.net11['Fixture.Runtime'].contentHash = 'fixture-hash'
    $lock.dependencies['net11/win-arm64']['Fixture.Runtime'].contentHash = 'fixture-hash'
    $lock.dependencies.net11['Fixture.Runtime'].resolved = '2.0.0'
    Write-Fixture $lockPath ($lock | ConvertTo-Json -Depth 8)
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Missing restored package*'
    $lock.dependencies.net11['Fixture.Runtime'].resolved = '1.0.0'
    Write-Fixture $lockPath ($lock | ConvertTo-Json -Depth 8)

    [void](Add-FixturePackage 'Fixture.FileLicense' '<license type="file">../../outside.txt</license>')
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Notice escapes package*'
    [void](Add-FixturePackage 'Fixture.FileLicense' '<license type="file">missing.txt</license>')
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Missing declared license file*'
    [void](Add-FixturePackage 'Fixture.FileLicense' '')
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Missing license declaration*'
    [void](Add-FixturePackage 'Fixture.FileLicense' '<license type="file">licenses/vendor.txt</license>')

    Write-Fixture $jsonPath '{}'
    Expect-Failure { & $generator -RepositoryRoot $fixture -Check } '*Stale SoftwareLicenseInventory.json*'
    & $generator -RepositoryRoot $fixture | Out-Null
    & $generator -RepositoryRoot $fixture -Check | Out-Null

    Write-Output "Passed $passed license-inventory fixture checks."
    if ($PublishedDirectory) {
        foreach ($name in @('SoftwareLicenses.txt', 'SoftwareLicenseInventory.json')) {
            $source = Join-Path $repository "smodr/Assets/$name"
            $destination = Join-Path ([IO.Path]::GetFullPath($PublishedDirectory)) "Assets/$name"
            Assert-True (Test-Path -LiteralPath $destination -PathType Leaf) "Published asset missing: $name"
            Assert-True ((Get-FileHash -LiteralPath $source).Hash -ceq (Get-FileHash -LiteralPath $destination).Hash) "Published asset is stale: $name"
        }
        Write-Output 'Published license assets match the committed inventory.'
    }
    if ($MsixDirectory) {
        $packages = @(Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($MsixDirectory)) -Recurse -File -Filter '*.msix')
        Assert-True ($packages.Count -eq 1) 'MSIX verification requires a directory containing exactly one package.'
        $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
        try {
            foreach ($name in @('SoftwareLicenses.txt', 'SoftwareLicenseInventory.json')) {
                $entry = $archive.GetEntry("Assets/$name")
                Assert-True ($null -ne $entry) "MSIX asset missing: $name"
                $stream = $entry.Open()
                try {
                    $actualHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash
                    $expectedHash = (Get-FileHash -LiteralPath (Join-Path $repository "smodr/Assets/$name") -Algorithm SHA256).Hash
                    Assert-True ($actualHash -ceq $expectedHash) "MSIX asset is stale: $name"
                } finally { $stream.Dispose() }
            }
        } finally { $archive.Dispose() }
        Write-Output 'MSIX license assets match the committed inventory.'
    }
} finally {
    # The only deletion is the explicitly created per-run fixture directory.
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $temporaryRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $temporaryRoot) -notmatch '^shoutkit-license-tests-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the validated test fixture directory.'
    }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
