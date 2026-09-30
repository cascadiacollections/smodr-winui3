param(
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repository 'smodr/Package.appxmanifest'
$project = Join-Path $repository 'smodr/smodr.csproj'
$originalManifest = [System.IO.File]::ReadAllBytes($manifestPath)
[xml] $manifest = [System.Text.Encoding]::UTF8.GetString($originalManifest)
$identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if (-not $identity -or $identity.GetAttribute('Publisher') -cne 'CN=excel' -or
    $identity.GetAttribute('Name') -cne '791b9f2a-7675-4564-88ea-3cf4789f711b') {
    throw 'This isolated CI smoke is only for the current development package identity.'
}
if (Get-AppxPackage -Name ($identity.GetAttribute('Name'))) {
    throw 'The development package is already installed; refusing to replace it.'
}

$testRoot = Join-Path $repository "out/msix-install-smoke-$Platform-$([guid]::NewGuid().ToString('N'))"
$certificatePath = Join-Path $testRoot 'ci-public.cer'
$certificate = $null
$installed = $null
try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $certificate = New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature `
        -Subject 'CN=excel' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -FriendlyName 'Shoutkit disposable CI package certificate'
    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null
    Import-Certificate -FilePath $certificatePath `
        -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

    function Build-TestPackage([string] $version, [string] $phase) {
        $identity.SetAttribute('Version', $version)
        $manifest.Save($manifestPath)
        $packageDirectory = Join-Path $testRoot $phase
        $buildArguments = @(
            'msbuild', $project, '-restore', '-v:q',
            '-p:RestoreLockedMode=true', '-p:Configuration=Release',
            "-p:Platform=$Platform", '-p:WindowsPackageType=MSIX',
            '-p:GenerateAppxPackageOnBuild=true', '-p:AppxBundle=Never',
            '-p:UapAppxPackageBuildMode=SideloadOnly',
            '-p:AppxPackageSigningEnabled=true',
            "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)",
            "-p:AppxPackageDir=$packageDirectory/"
        )
        & dotnet @buildArguments
        if ($LASTEXITCODE -ne 0) { throw "MSIX $phase build failed." }
        $package = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.msix' -File -Recurse |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $package) { throw "MSIX $phase build produced no package." }
        $signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
        if ($signature.Status -ne 'Valid' -or
            $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "MSIX $phase signature is not valid: $($signature.Status)."
        }
        return $package.FullName
    }

    $basePackage = Build-TestPackage '1.0.0.0' 'base'
    Add-AppxPackage -Path $basePackage -ErrorAction Stop
    $installed = Get-AppxPackage -Name ($identity.GetAttribute('Name'))
    if (-not $installed -or $installed.Version.ToString() -ne '1.0.0.0') {
        throw 'Base package was not registered at version 1.0.0.0.'
    }

    $localState = Join-Path $env:LOCALAPPDATA "Packages/$($installed.PackageFamilyName)/LocalState"
    New-Item -ItemType Directory -Path $localState -Force | Out-Null
    $marker = Join-Path $localState 'ci-upgrade-marker.txt'
    [System.IO.File]::WriteAllText($marker, 'retain-on-upgrade')

    $upgradePackage = Build-TestPackage '1.0.0.1' 'upgrade'
    Add-AppxPackage -Path $upgradePackage -ErrorAction Stop
    $upgraded = Get-AppxPackage -Name ($identity.GetAttribute('Name'))
    if (-not $upgraded -or $upgraded.Version.ToString() -ne '1.0.0.1' -or
        $upgraded.PackageFamilyName -cne $installed.PackageFamilyName -or
        -not (Test-Path -LiteralPath $marker) -or
        [System.IO.File]::ReadAllText($marker) -cne 'retain-on-upgrade') {
        throw 'Package upgrade did not preserve identity and package-local data.'
    }
    Write-Output "PASS: $Platform MSIX install and upgrade preserved LocalState."
}
finally {
    [System.IO.File]::WriteAllBytes($manifestPath, $originalManifest)
    $testPackage = Get-AppxPackage -Name ($identity.GetAttribute('Name'))
    if ($testPackage) { Remove-AppxPackage -Package $testPackage.PackageFullName }
    if ($certificate) {
        $trustedPath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        $personalPath = "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
        if (Test-Path -LiteralPath $trustedPath) { Remove-Item -LiteralPath $trustedPath }
        if (Test-Path -LiteralPath $personalPath) { Remove-Item -LiteralPath $personalPath }
    }
    $outRoot = [System.IO.Path]::GetFullPath((Join-Path $repository 'out'))
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($outRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedTestRoot).StartsWith('msix-install-smoke-',
            [System.StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
