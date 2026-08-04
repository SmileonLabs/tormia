[CmdletBinding()]
param(
    [switch]$PrintCurrent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $root 'Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset'
$ruleDatabasePath = Join-Path $root 'Assets/Data/Ontology/RuleDatabase.asset'
$manifestPath = Join-Path $root 'tests/harness/content-release-manifest.json'

function Read-Utf8Lines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing content release input: $Path"
    }
    return @(Get-Content -LiteralPath $Path -Encoding utf8)
}

function Get-NormalizedBlockHash([string[]]$Lines) {
    # Unity owns YAML formatting. Ignore indentation and trailing whitespace so
    # a reserialization-only change does not become a false content release.
    # Field order and values remain significant because they affect the
    # authored payload Unity publishes.
    $normalized = ($Lines | ForEach-Object { $_.Trim() }) -join "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($normalized)
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hash.Dispose()
    }
}

function Get-YamlListBlocks(
    [string[]]$Lines,
    [string]$StartProperty,
    [string]$EndProperty,
    [string]$ItemProperty) {
    $start = [Array]::FindIndex($Lines, [Predicate[string]] { param($line) $line -eq "  ${StartProperty}:" })
    if ($start -lt 0) { throw "Missing YAML property '$StartProperty'." }
    $end = [Array]::FindIndex($Lines, $start + 1, [Predicate[string]] { param($line) $line -like "  ${EndProperty}:*" })
    if ($end -lt 0) { throw "Missing YAML boundary '$EndProperty'." }

    $blocks = [System.Collections.Generic.List[object]]::new()
    $current = $null
    for ($index = $start + 1; $index -lt $end; $index++) {
        $line = $Lines[$index]
        if ($line -match "^  - ${ItemProperty}:\s*(.+?)\s*$") {
            if ($null -ne $current) { $blocks.Add($current) }
            $current = [pscustomobject]@{
                Id = $Matches[1].Trim()
                Lines = [System.Collections.Generic.List[string]]::new()
            }
        }
        if ($null -ne $current) { $current.Lines.Add($line) }
    }
    if ($null -ne $current) { $blocks.Add($current) }
    return @($blocks)
}

function Get-RuleDatabaseBlocks([string[]]$Lines) {
    $blocks = @{}
    $currentId = $null
    $currentLines = $null
    foreach ($line in $Lines) {
        if ($line -match '^  - id:\s*(.+?)\s*$') {
            if ($null -ne $currentId) { $blocks[$currentId] = @($currentLines) }
            $currentId = $Matches[1].Trim()
            $currentLines = [System.Collections.Generic.List[string]]::new()
        }
        if ($null -ne $currentLines) { $currentLines.Add($line) }
    }
    if ($null -ne $currentId) { $blocks[$currentId] = @($currentLines) }
    return $blocks
}

function Get-Scalar([string[]]$Lines, [string]$Property) {
    foreach ($line in $Lines) {
        if ($line -match "^\s+${Property}:\s*(.*?)\s*$") {
            return $Matches[1].Trim()
        }
    }
    throw "Missing '$Property' in release content block."
}

function New-CurrentManifest {
    $settings = Read-Utf8Lines $settingsPath
    $ruleDatabase = Read-Utf8Lines $ruleDatabasePath
    $ruleSources = Get-RuleDatabaseBlocks $ruleDatabase
    $configuredRules = Get-YamlListBlocks $settings 'developmentRules' 'animationContentManifest' 'ruleId'
    $configuredActions = Get-YamlListBlocks $settings 'developmentActions' 'useRealtimeNotifications' 'actionId'

    $rules = foreach ($configured in $configuredRules) {
        $version = [int](Get-Scalar @($configured.Lines) 'definitionVersion')
        if (-not $ruleSources.ContainsKey($configured.Id)) {
            throw "Configured Rule '$($configured.Id)' is missing from RuleDatabase.asset."
        }
        [ordered]@{
            id = $configured.Id
            definitionVersion = $version
            sourceSha256 = Get-NormalizedBlockHash @($ruleSources[$configured.Id])
        }
    }

    $actions = foreach ($configured in $configuredActions) {
        [ordered]@{
            id = $configured.Id
            definitionVersion = [int](Get-Scalar @($configured.Lines) 'definitionVersion')
            sourceSha256 = Get-NormalizedBlockHash @($configured.Lines)
        }
    }

    return [ordered]@{
        schemaVersion = 1
        packageId = Get-Scalar $settings 'developmentPackageId'
        packageVersion = Get-Scalar $settings 'developmentPackageVersion'
        # Preserve the reviewed Unity settings order. Hashing does not depend
        # on the host's locale-specific sorting rules.
        rules = @($rules)
        actions = @($actions)
    }
}

$current = New-CurrentManifest
$currentJson = $current | ConvertTo-Json -Depth 8
if ($PrintCurrent) {
    $currentJson
    exit 0
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing content release manifest. Run this script with -PrintCurrent and review the generated manifest before committing it."
}

$expected = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$expectedJson = $expected | ConvertTo-Json -Depth 8
# Harness evidence: PayloadChangeWithoutVersionBumpFailsManifestVerification.
if ($expectedJson -cne $currentJson) {
    throw @'
Development content differs from the reviewed immutable release manifest.
If a Rule or Action payload changed, increment its definitionVersion first.
Then run scripts/verify-content-release-manifest.ps1 -PrintCurrent, review the
new identities/checksums, and update tests/harness/content-release-manifest.json.
'@
}

Write-Host "Content release manifest is immutable and current ($($current.rules.Count) rules, $($current.actions.Count) actions)."
