[CmdletBinding()]
param(
    [switch]$RequireServices,
    [switch]$SkipServerBuild,
    [switch]$RunAccountWorldLoopSmoke,
    [switch]$RequireUnityMcp
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

Write-Host "TOV development harness verification" -ForegroundColor Cyan
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

        if ($scenario.kind -like '*gameplay-pipeline*') {
            if ($scenario.PSObject.Properties.Name -notcontains 'pipelineStages' -or
                $null -eq $scenario.pipelineStages) {
                throw "Gameplay pipeline scenario '$($scenario.id)' has no pipelineStages ownership record."
            }

            $requiredStages = @(
                'trigger',
                'intentOrObservation',
                'triples',
                'ruleBlock',
                'evaluation',
                'result',
                'authorityBoundary',
                'meaning',
                'adapter',
                'experience'
            )
            foreach ($stage in $requiredStages) {
                if ($scenario.pipelineStages.PSObject.Properties.Name -notcontains $stage -or
                    [string]::IsNullOrWhiteSpace([string]$scenario.pipelineStages.$stage)) {
                    throw "Gameplay pipeline scenario '$($scenario.id)' has no owner or N/A reason for stage '$stage'."
                }
            }
        }

        $evidencePath = Require-Path $scenario.evidenceFile
        if ($scenario.kind -like 'unity-*') {
            $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding utf8
            if (-not $evidence.Contains([string]$scenario.evidenceTest)) {
                throw "Scenario '$($scenario.id)' does not match its recorded Unity test."
            }
        }
    }

    $requiredProductionContracts = @{
        'player-ontology-production' = 'player-ground-combat-ontology-contract'
        'weapon-ontology-production' = 'rule-block-owned-primary-melee-attack'
        'monster-ontology-production' = 'portable-autonomous-monster-ontology-contract'
    }
    $requiredProductionStages = @(
        'resource',
        'triples',
        'ruleBlocks',
        'actions',
        'physicalMeaning',
        'animationManifest',
        'authority',
        'unityPresentation',
        'removedPath'
    )

    foreach ($contractId in $requiredProductionContracts.Keys) {
        $matches = @($manifest.scenarios | Where-Object {
            $_.PSObject.Properties.Name -contains 'productionContract' -and
            $null -ne $_.productionContract -and
            [string]$_.productionContract.contractId -eq $contractId
        })

        if ($matches.Count -ne 1) {
            throw "Required production contract '$contractId' must exist exactly once; found $($matches.Count)."
        }

        $scenario = $matches[0]
        if ([string]$scenario.id -ne [string]$requiredProductionContracts[$contractId]) {
            throw "Production contract '$contractId' must remain attached to canonical scenario '$($requiredProductionContracts[$contractId])'."
        }
        if ($scenario.kind -notlike '*gameplay-pipeline*') {
            throw "Production contract '$contractId' must use a gameplay-pipeline scenario."
        }
        if ([int]$scenario.productionContract.version -lt 1) {
            throw "Production contract '$contractId' has no valid contract version."
        }
        if ([string]::IsNullOrWhiteSpace([string]$scenario.productionContract.appliesTo)) {
            throw "Production contract '$contractId' does not declare what content it governs."
        }
        if ($scenario.productionContract.PSObject.Properties.Name -notcontains 'stages' -or
            $null -eq $scenario.productionContract.stages) {
            throw "Production contract '$contractId' has no production stages."
        }

        foreach ($stage in $requiredProductionStages) {
            if ($scenario.productionContract.stages.PSObject.Properties.Name -notcontains $stage -or
                [string]::IsNullOrWhiteSpace([string]$scenario.productionContract.stages.$stage)) {
                throw "Production contract '$contractId' is missing required stage '$stage'."
            }
        }

        $forbidden = @($scenario.productionContract.forbiddenImplementations)
        if ($forbidden.Count -lt 5 -or
            @($forbidden | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
            throw "Production contract '$contractId' must declare at least five non-empty forbidden implementation classes."
        }
        if ([string]::IsNullOrWhiteSpace([string]$scenario.enabledExpectation) -or
            [string]::IsNullOrWhiteSpace([string]$scenario.disabledExpectation)) {
            throw "Production contract '$contractId' must record both enabled and removed expectations."
        }

        $contractEvidence = @($scenario.productionContract.evidence)
        if ($contractEvidence.Count -lt 2) {
            throw "Production contract '$contractId' must record executable evidence across at least two system boundaries."
        }
        foreach ($evidenceRecord in $contractEvidence) {
            if ([string]::IsNullOrWhiteSpace([string]$evidenceRecord.file) -or
                [string]::IsNullOrWhiteSpace([string]$evidenceRecord.tests)) {
                throw "Production contract '$contractId' contains an incomplete evidence record."
            }

            $contractEvidencePath = Require-Path $evidenceRecord.file
            $contractEvidenceText = Get-Content -LiteralPath $contractEvidencePath -Raw -Encoding utf8
            $contractEvidenceNames = @([string]$evidenceRecord.tests -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            foreach ($contractEvidenceName in $contractEvidenceNames) {
                if (-not $contractEvidenceText.Contains($contractEvidenceName)) {
                    throw "Production contract '$contractId' does not match evidence '$contractEvidenceName' in '$($evidenceRecord.file)'."
                }
            }
        }

        $evidencePath = Require-Path $scenario.evidenceFile
        $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding utf8
        $evidenceNames = @([string]$scenario.evidenceTest -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        foreach ($evidenceName in $evidenceNames) {
            if (-not $evidence.Contains($evidenceName)) {
                throw "Production contract '$contractId' does not match evidence '$evidenceName' in '$($scenario.evidenceFile)'."
            }
        }
    }
}

Write-Check "Immutable development content release manifest" {
    $verifier = Require-Path 'scripts/verify-content-release-manifest.ps1'
    & $verifier
}

Write-Check "World entry content boundary" {
    $verifier = Require-Path 'scripts/verify-world-entry-boundary.ps1'
    & $verifier
}

Write-Check "Legacy migration ledger adoption safety" {
    $verifier = Require-Path 'scripts/verify-migration-adoption-safety.ps1'
    & $verifier
}

Write-Check "Realtime transport dependency pins" {
    $verifier = Require-Path 'scripts/verify-realtime-transport-dependencies.ps1'
    & $verifier
}

if ($RequireUnityMcp) {
    Write-Check "Unity official MCP relay and Editor bridge" {
        $mcpVerifier = Require-Path 'scripts/verify-unity-mcp.ps1'
        & $mcpVerifier
    }
}
else {
    $warnings.Add('Unity MCP check is opt-in. Run with -RequireUnityMcp while the Unity Editor is open.')
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
    Write-Check "World Authority deployment sync" {
        $compose = Require-Path 'infrastructure/docker-compose.yml'
        $envFile = Require-Path 'infrastructure/.env'
        & docker compose --env-file $envFile -f $compose up -d --build world-authority
        if ($LASTEXITCODE -ne 0) { throw "docker compose up exited with $LASTEXITCODE" }
    }

    Write-Check "Local Docker service status" {
        $compose = Require-Path 'infrastructure/docker-compose.yml'
        $envFile = Require-Path 'infrastructure/.env'
        & docker compose --env-file $envFile -f $compose ps
        if ($LASTEXITCODE -ne 0) { throw "docker compose ps exited with $LASTEXITCODE" }
    }

    Write-Check "World Authority health" {
        $health = $null
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try {
                $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5272/health' -TimeoutSec 2
                if ($health.status -eq 'healthy') { break }
            }
            catch {
                Start-Sleep -Seconds 1
            }
        }
        if ($null -eq $health -or $health.status -ne 'healthy') {
            throw 'World Authority did not report healthy after deployment.'
        }
    }

    Write-Check "World Authority entry API contract" {
        $statusCode = $null
        try {
            Invoke-WebRequest `
                -UseBasicParsing `
                -Method Post `
                -Uri 'http://127.0.0.1:5272/v1/worlds/00000000-0000-0000-0000-000000000000/content/preflight' `
                -ContentType 'application/json' `
                -Body '{"packageId":"harness-probe","packageVersion":1,"rules":[],"actions":[]}' `
                -TimeoutSec 5 | Out-Null
            $statusCode = 200
        }
        catch {
            if ($null -ne $_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            else {
                throw
            }
        }

        # The probe is intentionally invalid and unauthenticated. Depending on
        # minimal-API binding order, a current server rejects it with 400 or 401;
        # a stale deployment that does not contain the route returns 404.
        if ($statusCode -ne 400 -and $statusCode -ne 401) {
            throw "World Authority content preflight route contract mismatch (HTTP $statusCode)."
        }
    }

    Write-Check "PostgreSQL migration readiness" {
        $migrationVerifier = Require-Path 'scripts/verify-database-migrations.ps1'
        & $migrationVerifier
    }

    if ($RunAccountWorldLoopSmoke) {
        Write-Check "Account/world autosave and resume smoke" {
            $smoke = Require-Path 'scripts/run-account-world-loop-smoke.ps1'
            & $smoke
            if ($LASTEXITCODE -ne 0) { throw "account/world smoke exited with $LASTEXITCODE" }
        }
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
