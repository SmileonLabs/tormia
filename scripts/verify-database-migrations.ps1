[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $root 'infrastructure/docker-compose.yml'
$envFile = Join-Path $root 'infrastructure/.env'
$migrationRoot = Join-Path $root 'infrastructure/postgres/migrations'

foreach ($path in @($compose, $envFile, $migrationRoot)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing migration verification input: $path" }
}

function Get-EnvValue([string]$Name) {
    $match = Get-Content -LiteralPath $envFile -Encoding utf8 |
        Where-Object { $_ -match "^${Name}=(.*)$" } |
        Select-Object -First 1
    if ($null -eq $match) { throw "Missing $Name in infrastructure/.env." }
    return ([regex]::Match($match, "^${Name}=(.*)$")).Groups[1].Value.Trim()
}

$expected = @(Get-ChildItem -LiteralPath $migrationRoot -Filter '*.sql' -File |
    Sort-Object Name | ForEach-Object { $_.Name })
if ($expected.Count -eq 0) { throw 'No ordered SQL migrations were found.' }

$database = Get-EnvValue 'POSTGRES_DB'
$user = Get-EnvValue 'POSTGRES_USER'
$output = @(& docker compose --env-file $envFile -f $compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U $user -d $database -tA -c 'SELECT version FROM platform_schema_migrations ORDER BY version;' 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Could not read platform_schema_migrations: $($output -join ' ')"
}
$applied = @($output | ForEach-Object { $_.ToString().Trim() } |
    Where-Object { $_ -match '^\d+_.+\.sql$' })
$missing = @($expected | Where-Object { $applied -notcontains $_ })
# Harness evidence: PendingDatabaseMigrationFailsReadinessVerification.
if ($missing.Count -gt 0) {
    throw "Database has pending migrations: $($missing -join ', '). Run: docker compose --env-file infrastructure/.env -f infrastructure/docker-compose.yml run --rm migrate"
}

Write-Host "Database schema is current ($($expected.Count) migrations applied)."
