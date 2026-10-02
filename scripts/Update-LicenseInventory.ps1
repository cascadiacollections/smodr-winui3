#requires -Version 7.2
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string[]] $PackageFolders,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$lockPath = Join-Path $repository 'smodr/packages.lock.json'
$assetsPath = Join-Path $repository 'smodr/obj/project.assets.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
if ($lock.version -ne 2) { throw 'Unsupported app lockfile schema; review the generator.' }
if (-not $PackageFolders) { $PackageFolders = @($assets.packageFolders.Keys) }
if (-not $PackageFolders) { throw 'No restored package folders. Run a locked restore first.' }
$PackageFolders = @($PackageFolders | ForEach-Object { [IO.Path]::GetFullPath($_) })
$packages = @{}

function Add-Package([string] $Id, [string] $Version, [string] $Hash, [string] $Origin) {
    if ($Id -notmatch '^[A-Za-z0-9_.-]+$' -or $Version -notmatch '^[A-Za-z0-9.+-]+$') {
        throw "Invalid package identity: $Id $Version"
    }
    $key = "$Id/$Version".ToLowerInvariant()
    if ($packages.ContainsKey($key)) {
        if ($Hash -and $packages[$key].ContentHash -and $packages[$key].ContentHash -cne $Hash) {
            throw "Conflicting content hashes for $Id $Version"
        }
        return
    }
    $packages[$key] = [ordered]@{ Id = $Id; Version = $Version; ContentHash = $Hash; Origin = $Origin }
}

foreach ($framework in $lock.dependencies.Values) {
    foreach ($id in $framework.Keys) {
        $entry = $framework[$id]
        if ($entry.type -eq 'Project') { continue }
        if ($entry.type -notin @('Direct', 'Transitive', 'CentralTransitive') -or -not $entry.contentHash) {
            throw "Unsupported or incomplete locked package: $id"
        }
        Add-Package $id $entry.resolved $entry.contentHash 'App lockfile (includes build-time dependencies)'
    }
}

# SDK runtime packs are not written to packages.lock.json. Include both app RIDs,
# but not host tools or unrelated downloaded frameworks. No SDK installation path
# or machine-specific timestamp is emitted into the inventory.
foreach ($framework in $assets.project.frameworks.Values) {
    if (-not $framework.ContainsKey('downloadDependencies')) { continue }
    foreach ($pack in $framework.downloadDependencies) {
        if ($pack.name -notmatch '^Microsoft\.NETCore\.App\.Runtime\.win-(arm64|x64)$') { continue }
        if ($pack.version -notmatch '^\[([^,]+), \1\]$') { throw "Runtime pack must have an exact version: $($pack.name)" }
        Add-Package $pack.name $Matches[1] '' 'Self-contained .NET runtime pack'
    }
}
if (-not ($packages.Values | Where-Object { $_.Origin -eq 'Self-contained .NET runtime pack' })) {
    throw 'Restored assets have no Windows .NET runtime packs. Restore the app with its runtime identifiers first.'
}

function Read-Text([string] $Path) {
    if ((Get-Item -LiteralPath $Path).Length -gt 4MB) { throw "Notice exceeds 4 MiB: $Path" }
    $value = [IO.File]::ReadAllText($Path, $utf8).Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd()
    if ($value.Contains([char]0)) { throw "Notice is not text: $Path" }
    return $value
}

function Get-Hash([string] $Value) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($Value))).ToLowerInvariant()
}

function Resolve-PackageFile([string] $Directory, [string] $Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw "Absolute notice path: $Relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $Relative))
    $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Notice escapes package: $Relative" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing declared license file: $Relative" }
    # Restored NuGet files are ordinary files; refuse links rather than reading
    # outside the package through an intermediate directory junction/symlink.
    $part = Get-Item -LiteralPath $path
    while ($part.FullName -ne [IO.Path]::GetFullPath($Directory)) {
        if ($part.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked package notice: $Relative" }
        $part = Get-Item -LiteralPath (Split-Path -Parent $part.FullName)
    }
    return $path
}

function Read-Nuspec([string] $Path) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    } finally { $reader.Dispose() }
}

function Metadata-Text($Metadata, [string] $Name) {
    $node = $Metadata.SelectSingleNode("*[local-name()='$Name']")
    if ($null -eq $node) { return '' }
    return $node.InnerText.Trim()
}

$inventory = [Collections.Generic.List[object]]::new()
$notices = [ordered]@{}
$text = [Text.StringBuilder]::new()
[void]$text.AppendLine('Shoutkit for Windows - software licenses')
[void]$text.AppendLine('Generated by scripts/Update-LicenseInventory.ps1. Do not edit this file directly.')
[void]$text.AppendLine()
[void]$text.AppendLine('APPLICATION SOURCE - declared repository license')
[void]$text.AppendLine((Read-Text (Join-Path $repository 'LICENSE.txt')))
[void]$text.AppendLine()
[void]$text.AppendLine('Upstream Shoutkit iOS is separately GPL-3.0 licensed. This Windows repository declares MIT. Code provenance and GPL-derived portions require review before redistribution; this inventory does not resolve license compatibility.')
[void]$text.AppendLine()
[void]$text.AppendLine('DEPENDENCY INVENTORY')
[void]$text.AppendLine('All app-lockfile packages plus restored self-contained .NET runtime packs for x64 and ARM64. This conservative inventory includes build-time components, not proof that every listed package ships. Test-only project dependencies are excluded. Vendor declarations are recorded without inferring licensing approval. Included license/notice text is reproduced below; missing text requires release review.')

foreach ($key in ($packages.Keys | Sort-Object -Culture 'en-US')) {
    $package = $packages[$key]
    $directory = $null
    foreach ($folder in $PackageFolders) {
        $candidate = Join-Path $folder $key
        if (Test-Path -LiteralPath (Join-Path $candidate ($package.Id.ToLowerInvariant() + '.nuspec')) -PathType Leaf) {
            $directory = $candidate
            break
        }
    }
    if (-not $directory) { throw "Missing restored package: $key. Run locked restore; generation never downloads packages." }
    # NuGet's normalized lock hash can differ from the signed .nupkg SHA512.
    # Use the restore metadata's contentHash, just as locked restore does.
    $restoreMetadata = Get-Content -LiteralPath (Join-Path $directory '.nupkg.metadata') -Raw | ConvertFrom-Json -AsHashtable
    $actualHash = $restoreMetadata.contentHash
    if (-not $actualHash) { throw "Missing restored content hash: $key" }
    if ($package.ContentHash -and $package.ContentHash -cne $actualHash) { throw "Restored package hash differs from lockfile: $key" }
    $package.ContentHash = $actualHash
    $nuspecPath = Join-Path $directory ($package.Id.ToLowerInvariant() + '.nuspec')
    $xml = Read-Nuspec $nuspecPath
    $metadata = $xml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if (-not $metadata -or (Metadata-Text $metadata 'id') -ine $package.Id -or (Metadata-Text $metadata 'version') -ine $package.Version) {
        throw "Nuspec identity differs from restored package: $key"
    }
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if ($null -eq $license) {
        $licenseKind = 'legacy-url'
        $licenseValue = Metadata-Text $metadata 'licenseUrl'
        if (-not $licenseValue -or $licenseValue -eq 'https://aka.ms/deprecateLicenseUrl') {
            throw "Missing license declaration for $key; review manually before updating the inventory."
        }
    } else {
        $licenseKind = $license.GetAttribute('type')
        $licenseValue = $license.InnerText.Trim()
        if ($licenseKind -notin @('file', 'expression') -or -not $licenseValue) {
            throw "Unsupported license declaration for $key; review manually before updating the inventory."
        }
    }
    $files = [Collections.Generic.List[string]]::new()
    if ($licenseKind -eq 'file') { $files.Add((Resolve-PackageFile $directory $licenseValue)) }
    foreach ($file in (Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName -Culture 'en-US')) {
        if ($file.Name -match '^(licen[cs]e|notice|third[-_ ]?party[-_ ]?notices)([._ -].*)?$' -and $file.Extension -in @('', '.txt', '.md', '.html', '.htm', '.rtf')) {
            $files.Add((Resolve-PackageFile $directory ([IO.Path]::GetRelativePath($directory, $file.FullName))))
        }
    }
    $references = [Collections.Generic.List[object]]::new()
    foreach ($file in ($files | Sort-Object -Unique -Culture 'en-US')) {
        $body = Read-Text $file
        if (-not $body) { throw "Empty notice: $file" }
        $sha = Get-Hash $body
        $relative = [IO.Path]::GetRelativePath($directory, $file).Replace('\', '/')
        if (-not $notices.Contains($sha)) { $notices[$sha] = $body }
        $references.Add([ordered]@{ File = $relative; Sha256 = $sha })
    }
    $buildOnly = $package.Id -in @('Microsoft.Windows.SDK.BuildTools', 'Microsoft.Windows.SDK.BuildTools.MSIX')
    $record = [ordered]@{
        Id = $package.Id; Version = $package.Version; ContentHash = $package.ContentHash
        Origin = $package.Origin; BuildOnly = $buildOnly
        Authors = (Metadata-Text $metadata 'authors'); Copyright = (Metadata-Text $metadata 'copyright')
        LicenseKind = $licenseKind; License = $licenseValue
        PackageUrl = "https://www.nuget.org/packages/$($package.Id)/$($package.Version)"
        NoticeFiles = @($references.ToArray()); MetadataSha256 = (Get-Hash (Read-Text $nuspecPath))
    }
    $inventory.Add($record)
    [void]$text.AppendLine()
    [void]$text.AppendLine("$($record.Id) $($record.Version)$(if ($buildOnly) { ' [build-only]' })")
    [void]$text.AppendLine("Declared license ($licenseKind): $licenseValue")
    [void]$text.AppendLine("Authors: $($record.Authors)")
    if ($record.Copyright) { [void]$text.AppendLine($record.Copyright) }
    [void]$text.AppendLine($record.PackageUrl)
    if ($references.Count -eq 0) { [void]$text.AppendLine('No bundled license/notice text found; review the declared license before redistribution.') }
    foreach ($reference in $references) { [void]$text.AppendLine("Notice: $($reference.File) -> SHA256 $($reference.Sha256)") }
}

[void]$text.AppendLine()
[void]$text.AppendLine('BUNDLED LICENSE AND THIRD-PARTY NOTICE TEXTS (deduplicated by normalized-text SHA256)')
foreach ($sha in $notices.Keys) {
    [void]$text.AppendLine()
    [void]$text.AppendLine("===== SHA256 $sha =====")
    [void]$text.AppendLine($notices[$sha])
}
[void]$text.AppendLine()
[void]$text.AppendLine('Radio Browser is an external directory service, not bundled software: https://www.radio-browser.info/')
[void]$text.AppendLine('Source: https://github.com/cascadiacollections/smodr-winui3')
$outputs = [ordered]@{
    'SoftwareLicenses.txt' = $text.ToString().Replace("`r`n", "`n")
    'SoftwareLicenseInventory.json' = (([ordered]@{ SchemaVersion = 1; Packages = @($inventory.ToArray()) } | ConvertTo-Json -Depth 8) + "`n").Replace("`r`n", "`n")
}
foreach ($name in $outputs.Keys) {
    $path = Join-Path $repository "smodr/Assets/$name"
    if ($utf8.GetByteCount($outputs[$name]) -gt 32MB) { throw "Generated inventory exceeds 32 MiB: $name" }
    if ($Check) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or [IO.File]::ReadAllText($path, $utf8).Replace("`r`n", "`n") -cne $outputs[$name]) {
            throw "Stale $name. Run pwsh -File scripts/Update-LicenseInventory.ps1 after locked restore and commit both generated assets."
        }
    } else {
        [IO.File]::WriteAllText($path, $outputs[$name], $utf8)
    }
}
Write-Output "License inventory $(if ($Check) { 'verified' } else { 'updated' }): $($inventory.Count) packages, $($notices.Count) unique notice texts."
