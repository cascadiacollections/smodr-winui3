param([string] $PublishedDirectory, [string] $MsixDirectory)

# Fails when the app's own assemblies were packaged as IL only. PublishReadyToRun is
# silently skipped for RID-less builds, so check the shipped bits rather than the property.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishedDirectory) -eq [string]::IsNullOrWhiteSpace($MsixDirectory)) {
    throw 'Pass exactly one of -PublishedDirectory or -MsixDirectory.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$required = @('smodr.dll', 'smodr.RadioCore.dll', 'Microsoft.WinUI.dll')

function Test-Image([IO.Stream] $stream) {
    $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $header = $reader.PEHeaders.CorHeader
        return [bool]($header -and $header.ManagedNativeHeaderDirectory.Size -gt 0)
    }
    finally { $reader.Dispose() }
}

function Assert-Images([string] $label, [hashtable] $images) {
    foreach ($name in $required) {
        if (-not $images.ContainsKey($name)) { throw "$label does not contain $name." }
        if (-not $images[$name]) { throw "$label contains $name without ReadyToRun code." }
    }
    $count = @($images.Values | Where-Object { $_ }).Count
    Write-Output "ReadyToRun verified: $label ($count R2R assemblies)."
}

if ($PublishedDirectory) {
    $root = [IO.Path]::GetFullPath($PublishedDirectory)
    $images = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.dll' -File) {
        $stream = [IO.File]::OpenRead($file.FullName)
        try { $images[$file.Name] = Test-Image $stream } finally { $stream.Dispose() }
    }
    Assert-Images $root $images
}
else {
    $packages = @(Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($MsixDirectory)) -Recurse -File -Filter '*.msix')
    if ($packages.Count -eq 0) { throw "No MSIX package found under $MsixDirectory." }
    foreach ($package in $packages) {
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $images = @{}
            foreach ($entry in $archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.dll' }) {
                # PEReader needs a seekable stream; zip entry streams are not.
                $buffer = [IO.MemoryStream]::new()
                $source = $entry.Open()
                try { $source.CopyTo($buffer) } finally { $source.Dispose() }
                $buffer.Position = 0
                $images[$entry.Name] = Test-Image $buffer
            }
            Assert-Images $package.FullName $images
        }
        finally { $archive.Dispose() }
    }
}
