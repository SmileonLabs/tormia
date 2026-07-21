[CmdletBinding()]
param(
    [switch]$RequireServices,
    [switch]$SkipServerBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Write-Check([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        Write-Host "[PASS] $Name" -ForegroundColor Green
    }
    catch {
        $script:failures.Add("$Name :: $($_.Exception.Message)")
        Write-Host "[FAIL] $Name" -ForegroundColor Red
    }
}

function Require-Path([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required file: $RelativePath"
    }
    return $path
}

Write-Host "Tormia development harness verification" -ForegroundColor Cyan
Write-Host "Root: $root"

Write-Check "Shared project context (English + Korean)" {
    Require-Path 'docs/PROJECT_CONTEXT.md' | Out-Null
    Require-Path 'docs/PROJECT_CONTEXT.ko.md' | Out-Null
    Require-Path 'AGENTS.md' | Out-Null
}

Write-Check "Core regression manifest" {
    $manifestPath = Require-Path 'tests/harness/core-regression-scenarios.json'
    # Windows PowerShell 5 defaults to the system ANSI code page for UTF-8 files
    # without a BOM. Explicit UTF-8 keeps Korean scenario text valid JSON.
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.scenarios.Count -eq 0) {
        throw 'Scenario manifest has no valid scenarios.'
    }

    foreach ($scenario in $manifest.scenarios) {
        if ([string]::IsNullOrWhiteSpace($scenario.id) -or
            [string]::IsNullOrWhiteSpace($scenario.evidenceFile) -or
            [string]::IsNullOrWhiteSpace($scenario.evidenceTest)) {
            throw 'Scenario is missing id, evidenceFile, or evidenceTest.'
        }
        $evidencePath = Require-Path $scenario.evidenceFile
        if ($scenario.kind -like 'unity-*') {
            $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding utf8
            if (-not $evidence.Contains([string]$scenario.evidenceTest)) {
                throw "Scenario '$($scenario.id)' does not match its recorded Unity test."
            }
        }
    }
}

if (-not $SkipServerBuild) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $hasDotnetSdk = $false
    if ($null -ne $dotnet) {
        $sdkOutput = & dotnet --list-sdks 2>$null
        $hasDotnetSdk = $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($sdkOutput -join ''))
    }

    if ($hasDotnetSdk) {
        Write-Check "World Authority build (.NET SDK)" {
            $project = Require-Path 'server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj'
            & dotnet build $project --nologo
            if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE" }
        }
    }
    elseif ($null -ne (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Check "World Authority build (Docker fallback)" {
            $compose = Require-Path 'infrastructure/docker-compose.yml'
            $envFile = Require-Path 'infrastructure/.env'
            & docker compose --env-file $envFile -f $compose build world-authority
            if ($LASTEXITCODE -ne 0) { throw "docker compose build exited with $LASTEXITCODE" }
        }
    }
    else {
        $warnings.Add('No .NET SDK or Docker was found; World Authority build was skipped.')
    }
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $docker) {
    $warnings.Add('Docker was not found; service checks were skipped.')
}
elseif ($RequireServices) {
    Write-Check "Local Docker service status" {
        $compose = Require-Path 'infrastructure/docker-compose.yml'
        $envFile = Require-Path 'infrastructure/.env'
        & docker compose --env-file $envFile -f $compose ps
        if ($LASTEXITCODE -ne 0) { throw "docker compose ps exited with $LASTEXITCODE" }
    }

    Write-Check "World Authority health" {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5272/health' -TimeoutSec 5
        if ($health.status -ne 'healthy') { throw 'World Authority did not report healthy.' }
    }
}
else {
    $warnings.Add('Docker exists but service checks are opt-in. Run with -RequireServices when the local stack should be running.')
}

foreach ($warning in $warnings) {
    Write-Host "[WARN] $warning" -ForegroundColor Yellow
}

if ($failures.Count -gt 0) {
    Write-Host "`nHarness verification failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "`nHarness verification passed." -ForegroundColor Green
