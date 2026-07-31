[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080',
    [string]$ExpectedProject = '',
    [ValidateRange(1, 120)]
    [int]$WaitSeconds = 20,
    [switch]$SkipCodexConfigCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExpectedProject)) {
    $ExpectedProject = Split-Path -Leaf $root
}

$normalizedBaseUrl = $BaseUrl.TrimEnd('/')
$mcpUrl = "$normalizedBaseUrl/mcp"
$healthUrl = "$normalizedBaseUrl/health"
$instancesUrl = "$normalizedBaseUrl/api/instances"

function Write-Pass([string]$Message) {
    Write-Host "[PASS] $Message" -ForegroundColor Green
}

function Fail([string]$Message) {
    throw $Message
}

Write-Host 'TOV Unity MCP HTTP verification' -ForegroundColor Cyan
Write-Host "Endpoint: $mcpUrl"

$health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 5
if ($health.status -ne 'healthy') {
    Fail "MCP server did not report healthy at $healthUrl."
}
Write-Pass "HTTP server healthy (version $($health.version))"

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$instances = @()
do {
    try {
        $instanceResponse = Invoke-RestMethod -Uri $instancesUrl -Method Get -TimeoutSec 3
        $instances = @($instanceResponse.instances)
    }
    catch {
        $instances = @()
    }

    if ($instances.Count -gt 0) {
        break
    }

    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

if ($instances.Count -eq 0) {
    Fail "No Unity instance connected within $WaitSeconds second(s). Confirm HTTP transport and Auto-Start on Editor Load in the MCP for Unity window."
}

$matchingInstance = $instances |
    Where-Object { $_.project -eq $ExpectedProject } |
    Select-Object -First 1

if ($null -eq $matchingInstance) {
    $available = ($instances | ForEach-Object { "$($_.project)@$($_.hash)" }) -join ', '
    Fail "Expected Unity project '$ExpectedProject' was not connected. Available: $available"
}

Write-Pass "Unity connected as $($matchingInstance.project)@$($matchingInstance.hash)"

if (-not $SkipCodexConfigCheck) {
    $configPath = Join-Path $env:USERPROFILE '.codex\config.toml'
    if (-not (Test-Path -LiteralPath $configPath)) {
        Fail "Codex config was not found: $configPath"
    }

    $configText = Get-Content -LiteralPath $configPath -Raw -Encoding utf8
    $sectionPattern = '(?ms)^\[mcp_servers\.unityMCP\]\s*(?<body>.*?)(?=^\[|\z)'
    $sectionMatch = [regex]::Match($configText, $sectionPattern)
    if (-not $sectionMatch.Success) {
        Fail 'Codex config has no [mcp_servers.unityMCP] section.'
    }

    $sectionBody = $sectionMatch.Groups['body'].Value
    $expectedUrlPattern = '(?m)^\s*url\s*=\s*"' + [regex]::Escape($mcpUrl) + '"\s*$'
    if ($sectionBody -notmatch $expectedUrlPattern) {
        Fail "Codex Unity MCP URL is not '$mcpUrl'."
    }

    if ($sectionBody -match '(?m)^\s*(command|args)\s*=') {
        Fail 'Codex Unity MCP still contains stdio command/args beside the HTTP URL.'
    }

    Write-Pass 'Codex config uses one Streamable HTTP Unity MCP endpoint'
}

Write-Host 'Unity MCP verification passed.' -ForegroundColor Green
