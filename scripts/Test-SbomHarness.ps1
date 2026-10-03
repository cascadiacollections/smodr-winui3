$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('shoutkit-sbom-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $valid = @{
        spdxVersion = 'SPDX-2.2'; dataLicense = 'CC0-1.0'; name = 'Shoutkit 1.0.0'
        documentNamespace = 'https://github.com/cascadiacollections/smodr-winui3/test'
        creationInfo = @{ created = '2026-01-01T00:00:00Z'; creators = @('Tool: fixture') }
        files = @(@{ SPDXID = 'SPDXRef-File-a' })
        packages = @(@{ SPDXID = 'SPDXRef-Package-app' }, @{ SPDXID = 'SPDXRef-Package-dependency' })
    }
    $cases = @(
        @{ Name = 'valid'; Mutate = {}; Fails = $false },
        @{ Name = 'no-files'; Mutate = { param($x) $x.files = @() }; Fails = $true },
        @{ Name = 'no-dependencies'; Mutate = { param($x) $x.packages = @(@{ SPDXID = 'SPDXRef-Package-app' }) }; Fails = $true },
        @{ Name = 'wrong-namespace'; Mutate = { param($x) $x.documentNamespace = 'https://example.invalid/test' }; Fails = $true },
        @{ Name = 'duplicate-ids'; Mutate = { param($x) $x.packages[1].SPDXID = $x.packages[0].SPDXID }; Fails = $true }
    )
    foreach ($case in $cases) {
        $document = $valid | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable
        & $case.Mutate $document
        $path = Join-Path $root ($case.Name + '.json')
        $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
        $failed = $false
        try { & "$PSScriptRoot/Test-Sbom.ps1" -ManifestPath $path } catch { $failed = $true }
        if ($failed -ne $case.Fails) { throw "Incorrect SBOM verdict: $($case.Name)" }
    }
    Write-Host "Passed $($cases.Count) SBOM validation fixtures."
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
