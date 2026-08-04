[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$adoptionPath = Join-Path $root 'scripts/adopt-existing-database-migration-ledger.ps1'
$fingerprintPath = Join-Path $root 'infrastructure/postgres/verification/legacy-schema-through-016.sql'
$source = Get-Content -LiteralPath $adoptionPath -Raw -Encoding utf8
$fingerprint = Get-Content -LiteralPath $fingerprintPath -Raw -Encoding utf8

# Harness evidence: LegacyLedgerAdoptionIsExplicitAndFingerprintGated.
$required = @(
    '[switch]$Apply',
    '$ledgerRows.Count -gt 0',
    '$missing.Count -gt 0',
    'if (-not $Apply)',
    'INSERT INTO platform_schema_migrations',
    "'016_meaning_package_ownership.sql'"
)
foreach ($token in $required) {
    if (-not $source.Contains($token)) { throw "Ledger adoption safety token is missing: $token" }
}
if ($source.Contains("'017_retract_misrestored_adopted_facts.sql'")) {
    throw 'Data-repair migration 017 must never be silently adopted from a schema fingerprint.'
}
foreach ($token in @(
    '016.fact_ownership_backfill',
    '016.binding_ownership_backfill',
    'ux_content_action_definitions_package',
    'ck_world_facts_rule_result_lifetime')) {
    if (-not $fingerprint.Contains($token)) { throw "Legacy schema fingerprint is incomplete: $token" }
}

$dryRunIndex = $source.IndexOf('if (-not $Apply)', [StringComparison]::Ordinal)
$insertIndex = $source.IndexOf('INSERT INTO platform_schema_migrations', [StringComparison]::Ordinal)
if ($dryRunIndex -lt 0 -or $insertIndex -lt 0 -or $dryRunIndex -gt $insertIndex) {
    throw 'Explicit dry-run exit must precede the ledger INSERT.'
}

Write-Host 'Legacy migration-ledger adoption is explicit, fingerprint-gated, and excludes data repair 017.'
