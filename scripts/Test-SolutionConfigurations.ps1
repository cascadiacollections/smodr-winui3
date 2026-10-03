#requires -Version 7.2
[CmdletBinding()]
param([string] $DotnetPath = 'dotnet', [switch] $SelfTest)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
$projectProperties = @{}
function Normalize-Platform([string] $Value) { return $Value.Replace(' ', '').ToUpperInvariant() }
function Get-ProjectConfiguration([string] $Path) {
    $fullPath = Join-Path $repository $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Unknown project: $Path" }
    if (-not $projectProperties.ContainsKey($fullPath)) {
        $output = & $DotnetPath msbuild $fullPath -nologo -verbosity:quiet -getProperty:Platforms,Configurations
        if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate configuration properties: $Path" }
        $properties = ($output -join [Environment]::NewLine | ConvertFrom-Json).Properties
        $projectProperties[$fullPath] = @{
            Platforms = @($properties.Platforms.Split(';') | ForEach-Object { Normalize-Platform $_ })
            Configurations = @($properties.Configurations.Split(';'))
        }
    }
    return $projectProperties[$fullPath]
}
function Assert-ProjectMapping([string] $Path, [string] $Configuration, [string] $Platform) {
    $properties = Get-ProjectConfiguration $Path
    if ($Configuration -notin $properties.Configurations -or (Normalize-Platform $Platform) -notin $properties.Platforms) {
        throw "Unsupported project configuration: $Path -> $Configuration|$Platform"
    }
}
function Test-XmlSolution([xml] $Solution) {
    $platforms = @($Solution.SelectNodes('/Solution/Configurations/Platform') | ForEach-Object { $_.GetAttribute('Name') })
    $types = @($Solution.SelectNodes('/Solution/Configurations/BuildType') | ForEach-Object { $_.GetAttribute('Name') })
    if ($types.Count -eq 0) { $types = @('Debug', 'Release') }
    foreach ($project in $Solution.SelectNodes('/Solution/Project')) {
        $path = $project.GetAttribute('Path')
        foreach ($type in $types) {
            foreach ($platform in $platforms) {
                $projectPlatform = $null
                $projectType = $type
                foreach ($rule in $project.SelectNodes('Platform|BuildType')) {
                    $pattern = $rule.GetAttribute('Solution')
                    if (-not $pattern) { $pattern = '*|*' }
                    if ("$type|$platform" -like $pattern) {
                        if ($rule.Name -eq 'Platform') { $projectPlatform = $rule.GetAttribute('Project') }
                        else { $projectType = $rule.GetAttribute('Project') }
                    }
                }
                if (-not $projectPlatform) { throw "Missing explicit project platform mapping: $path ($type|$platform)" }
                Assert-ProjectMapping $path $projectType $projectPlatform
            }
        }
    }
}
function Test-LegacySolution([string] $Text) {
    $projects = @{}
    foreach ($entry in [regex]::Matches($Text, '(?m)^Project\("[^"]+"\) = "[^"]+", "([^"]+\.csproj)", "([^"]+)"')) {
        $projects[$entry.Groups[2].Value] = $entry.Groups[1].Value
    }
    $section = [regex]::Match($Text, '(?s)GlobalSection\(SolutionConfigurationPlatforms\).*?\r?\n(.*?)EndGlobalSection').Groups[1].Value
    $configurations = @([regex]::Matches($section, '(?m)^\s*([^=\r\n]+?)\s*=') | ForEach-Object { $_.Groups[1].Value.Trim() })
    if ($configurations.Count -eq 0) { throw 'Solution has no configurations.' }
    $active = @{}
    foreach ($entry in [regex]::Matches($Text, '(?m)^\s*(\{[^}]+\})\.([^\r\n]+?)\.(ActiveCfg|Build\.0|Deploy\.0)\s*=\s*([^\r\n]+)')) {
        $guid = $entry.Groups[1].Value
        $solutionConfiguration = $entry.Groups[2].Value.Trim()
        if (-not $projects.ContainsKey($guid)) { throw "Unknown project GUID in mapping: $guid" }
        if ($solutionConfiguration -notin $configurations) { throw "Unknown solution configuration in mapping: $solutionConfiguration" }
        $target = $entry.Groups[4].Value.Trim().Split('|')
        if ($target.Count -ne 2) { throw 'Malformed project configuration.' }
        Assert-ProjectMapping $projects[$guid] $target[0] $target[1]
        if ($entry.Groups[3].Value -eq 'ActiveCfg') { $active["$guid/$solutionConfiguration"] = $true }
    }
    foreach ($guid in $projects.Keys) {
        foreach ($configuration in $configurations) {
            if (-not $active.ContainsKey("$guid/$configuration")) { throw "Missing ActiveCfg mapping: $guid/$configuration" }
        }
    }
}
Push-Location $repository
try {
    $xml = [xml](Get-Content smodr.slnx -Raw)
    $legacy = Get-Content smodr.sln -Raw
    Test-XmlSolution $xml
    Test-LegacySolution $legacy
    foreach ($solution in @('smodr.slnx', 'smodr.sln')) {
        foreach ($type in @('Debug', 'Release')) {
            foreach ($platform in @('ARM64', 'x64')) {
                & $DotnetPath msbuild $solution -nologo -verbosity:quiet -t:ValidateSolutionConfiguration "-p:Configuration=$type" "-p:Platform=$platform" -warnaserror
                if ($LASTEXITCODE -ne 0) { throw "MSBuild rejected $solution $type|$platform" }
            }
        }
    }
    if ($SelfTest) {
        $brokenXml = [xml]$xml.OuterXml
        $brokenXml.SelectSingleNode('/Solution/Project[@Path="smodr/smodr.csproj"]/Platform').SetAttribute('Project', 'Any CPU')
        try { Test-XmlSolution $brokenXml; throw 'Fixture did not reject an unsupported mapping.' }
        catch { if ($_.Exception.Message -notlike 'Unsupported project configuration:*') { throw } }
        $brokenLegacy = $legacy.Replace('.Debug|ARM64.ActiveCfg = Debug|ARM64', '.Debug|ARM64.ActiveCfg = Debug|x86')
        try { Test-LegacySolution $brokenLegacy; throw 'Fixture did not reject an unsupported mapping.' }
        catch { if ($_.Exception.Message -notlike 'Unsupported project configuration:*') { throw } }
        Write-Host 'Passed two invalid-mapping regression fixtures.'
    }
    Write-Host 'Both solution formats validated for Debug/Release on ARM64/x64 against evaluated project configurations.'
}
finally { Pop-Location }
