param(
    [ValidateSet('ARM64', 'x64')][string] $Platform = 'ARM64',
    [string] $CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repository 'smodr/Package.appxmanifest'
$runner = Join-Path $PSScriptRoot 'dotnet-dev.ps1'
$project = Join-Path $repository 'smodr/smodr.csproj'
$signed = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
& (Join-Path $PSScriptRoot 'Test-PackageManifest.ps1') -ManifestPath $manifestPath -Production:$signed
$packageDir = Join-Path $repository ("out/msix-$Platform" + $(if ($signed) { '-signed' } else { '' }))
$arguments = @(
    $Platform, 'msbuild', $project, '-restore',
    '-p:RestoreLockedMode=true', '-p:Configuration=Release',
    "-p:Platform=$Platform", '-p:WindowsPackageType=MSIX',
    '-p:GenerateAppxPackageOnBuild=true', '-p:AppxBundle=Never',
    '-p:UapAppxPackageBuildMode=SideloadOnly',
    "-p:AppxPackageDir=$packageDir\"
)

if ($signed) {
    $thumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') { throw 'CertificateThumbprint must be a 40-character SHA-1 certificate thumbprint.' }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate) {
        $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
    }
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw 'Signing certificate with private key was not found in CurrentUser or LocalMachine Personal store.'
    }
    [xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
    $identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if (-not $identity -or $identity.Publisher -cne $certificate.Subject) {
        throw 'Manifest Publisher must exactly match the signing certificate Subject.'
    }
    if ($identity.Name -match '^[0-9a-f]{8}-[0-9a-f-]{27,}$' -or $identity.Publisher -eq 'CN=excel') {
        throw 'Replace the development package identity before producing a signed release.'
    }
    $arguments += '-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateThumbprint=$thumbprint"
} else {
    $arguments += '-p:AppxPackageSigningEnabled=false'
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner @arguments
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed with exit code $LASTEXITCODE." }
$package = Get-ChildItem -LiteralPath $packageDir -Recurse -Filter '*.msix' -File |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $package) { throw 'MSBuild succeeded but produced no MSIX package.' }
if ($signed) {
    $signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint) {
        throw "Package signature was not valid: $($package.FullName) ($($signature.Status))."
    }
}
$label = if ($signed) { 'SIGNED' } else { 'UNSIGNED PREVIEW' }
Write-Output "${label}: $($package.FullName)"
