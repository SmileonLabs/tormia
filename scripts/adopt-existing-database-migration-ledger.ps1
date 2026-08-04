[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$FingerprintOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $root 'infrastructure/docker-compose.yml'
$envFile = Join-Path $root 'infrastructure/.env'
$fingerprintPath = Join-Path $root 'infrastructure/postgres/verification/legacy-schema-through-016.sql'

function Get-EnvValue([string]$Name) {
    $match = Get-Content -LiteralPath $envFile -Encoding utf8 |
        Where-Object { $_ -match "^${Name}=(.*)$" } |
        Select-Object -First 1
    if ($null -eq $match) { throw "Missing $Name in infrastructure/.env." }
    return ([regex]::Match($match, "^${Name}=(.*)$")).Groups[1].Value.Trim()
}

function Invoke-DatabaseScalarLines([string]$Sql) {
    $database = Get-EnvValue 'POSTGRES_DB'
    $user = Get-EnvValue 'POSTGRES_USER'
    $output = @(& docker compose --env-file $envFile -f $compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $user -d $database -tA -c $Sql 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($output -join ' ') }
    return @($output | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ })
}

foreach ($path in @($compose, $envFile, $fingerprintPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing ledger adoption input: $path" }
}

$ledgerRows = @(Invoke-DatabaseScalarLines 'SELECT version FROM platform_schema_migrations ORDER BY version;')
if ($ledgerRows.Count -gt 0 -and -not $FingerprintOnly) {
    throw "Ledger adoption is allowed only for a completely empty legacy ledger. Found: $($ledgerRows -join ', '). Use the normal migrate job or repair the partial ledger explicitly."
}

$fingerprintSql = Get-Content -LiteralPath $fingerprintPath -Raw -Encoding utf8
$missing = @(Invoke-DatabaseScalarLines $fingerprintSql)
if ($missing.Count -gt 0) {
    throw "Legacy schema is not safely compatible through migration 016. Missing fingerprints: $($missing -join ', '). No ledger rows were written."
}

if ($FingerprintOnly) {
    Write-Host 'Database schema and migration-016 ownership backfill match the legacy adoption fingerprint.'
    Write-Host "Ledger currently contains $($ledgerRows.Count) row(s); no rows were written."
    exit 0
}

$baselineVersions = @(Get-ChildItem -LiteralPath (Join-Path $root 'infrastructure/postgres/migrations') -Filter '*.sql' -File |
    Sort-Object Name | ForEach-Object { $_.Name } | Where-Object { $_ -le '016_meaning_package_ownership.sql' })
if ($baselineVersions.Count -ne 16) {
    throw "Expected exactly migrations 001 through 016; found $($baselineVersions.Count)."
}

if (-not $Apply) {
    Write-Host 'Legacy database schema is fingerprint-compatible through migration 016.'
    Write-Host 'No rows were written. Migration 017 remains intentionally pending because it is an idempotent data repair.'
    Write-Host 'After reviewing/backing up the local database, run:'
    Write-Host '  powershell -ExecutionPolicy Bypass -File scripts/adopt-existing-database-migration-ledger.ps1 -Apply'
    Write-Host 'Then run the normal migrate job to apply migration 017.'
    exit 0
}

# Harness evidence: CompatibleLegacySchemaCanBeAdoptedOnlyExplicitly.
$values = ($baselineVersions | ForEach-Object { "('$($_.Replace("'", "''"))')" }) -join ','
$adoptSql = @"
BEGIN;
DO `$`$
BEGIN
    IF EXISTS (SELECT 1 FROM platform_schema_migrations) THEN
        RAISE EXCEPTION 'migration ledger changed during adoption';
    END IF;
END
`$`$;
INSERT INTO platform_schema_migrations(version) VALUES $values;
COMMIT;
"@
Invoke-DatabaseScalarLines $adoptSql | Out-Null
Write-Host "Adopted verified legacy schema ledger through migration 016 ($($baselineVersions.Count) rows)."
Write-Host 'Migration 017 remains pending. Run the normal migrate job after reviewing the data repair.'
