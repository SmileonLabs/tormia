[CmdletBinding()]
param(
    [int]$StartupTimeoutSec = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $root 'infrastructure\docker-compose.yml'
$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$projectName = "tormia-smoke-$suffix"
$authorityPort = Get-Random -Minimum 55272 -Maximum 56272
$postgresPort = Get-Random -Minimum 56430 -Maximum 57430
$redisPort = Get-Random -Minimum 57431 -Maximum 58431
$baseUri = "http://127.0.0.1:$authorityPort"

$environmentNames = @(
    'POSTGRES_PORT',
    'REDIS_PORT',
    'WORLD_AUTHORITY_PORT',
    'TORMIA_NETWORK_NAME'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] =
        [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-Compose {
    param([string[]]$ComposeArguments)
    & docker compose -f $composeFile -p $projectName @ComposeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose failed: $($ComposeArguments -join ' ')"
    }
}

try {
    $env:POSTGRES_PORT = [string]$postgresPort
    $env:REDIS_PORT = [string]$redisPort
    $env:WORLD_AUTHORITY_PORT = [string]$authorityPort
    $env:TORMIA_NETWORK_NAME = "${projectName}_backend"

    Write-Host "Starting isolated Authority smoke stack: $projectName" `
        -ForegroundColor Cyan
    Invoke-Compose -ComposeArguments @('up', '-d', 'postgres', 'redis')
    Invoke-Compose -ComposeArguments @(
        '--profile', 'tools', 'run', '--rm', 'migrate')
    Invoke-Compose -ComposeArguments @(
        'up', '-d', '--build', 'world-authority')

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSec)
    $healthy = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri "$baseUri/health" `
                -Method Get -TimeoutSec 3
            if ($null -ne $health) {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $healthy) {
        throw "Isolated World Authority did not become healthy at $baseUri."
    }

    & (Join-Path $PSScriptRoot 'run-combat-authority-smoke.ps1') `
        -BaseUri $baseUri `
        -TimeoutSec 10 `
        -UseExistingServices
    if ($LASTEXITCODE -ne 0) {
        throw 'Isolated combat smoke failed.'
    }
}
finally {
    try {
        Invoke-Compose -ComposeArguments @(
            'down', '--volumes', '--remove-orphans')
    }
    catch {
        Write-Warning "Isolated smoke cleanup failed: $($_.Exception.Message)"
    }
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable(
            $name,
            $previousEnvironment[$name],
            'Process')
    }
}
