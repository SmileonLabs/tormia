[CmdletBinding()]
param(
    [string]$ExpectedProjectPath = '',
    [string]$RelayPath = '',
    [string]$CodexServerName = 'unityOfficial',
    [string]$RelayClientName = 'TOV',
    [ValidateRange(1, 120)]
    [int]$WaitSeconds = 20,
    [switch]$SkipCodexConfigCheck,
    [switch]$LiveToolProbe,
    [switch]$CheckLegacyFallback,
    [string]$LegacyBaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
if ([string]::IsNullOrWhiteSpace($ExpectedProjectPath)) {
    $ExpectedProjectPath = $root
}
else {
    $ExpectedProjectPath = (Resolve-Path $ExpectedProjectPath).Path
}

if ([string]::IsNullOrWhiteSpace($RelayPath)) {
    $RelayPath = Join-Path $env:USERPROFILE '.unity\relay\relay_win.exe'
}

function Write-Pass([string]$Message) {
    Write-Host "[PASS] $Message" -ForegroundColor Green
}

function Write-Warn([string]$Message) {
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Fail([string]$Message) {
    throw $Message
}

function Read-JsonRpcResponse(
    [System.Diagnostics.Process]$Process,
    [int]$ExpectedId,
    [datetime]$Deadline
) {
    while ((Get-Date) -lt $Deadline) {
        $remainingMilliseconds = [Math]::Max(1, [int](($Deadline - (Get-Date)).TotalMilliseconds))
        $readTask = $Process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMilliseconds)) {
            return $null
        }

        $line = $readTask.Result
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($Process.HasExited) {
                return $null
            }
            continue
        }

        try {
            $message = $line | ConvertFrom-Json
        }
        catch {
            continue
        }

        if ($null -ne $message.id -and [int]$message.id -eq $ExpectedId) {
            return $message
        }
    }

    return $null
}

Write-Host 'TOV official Unity MCP verification' -ForegroundColor Cyan
Write-Host "Project: $ExpectedProjectPath"
Write-Host "Relay:   $RelayPath"

if (-not (Test-Path -LiteralPath $RelayPath -PathType Leaf)) {
    Fail "Unity official relay was not found: $RelayPath"
}
Write-Pass 'Unity official relay binary exists'

$manifestPath = Join-Path $root 'Packages\manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Fail "Unity package manifest was not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$assistantVersion = $manifest.dependencies.'com.unity.ai.assistant'
if ([string]::IsNullOrWhiteSpace([string]$assistantVersion)) {
    Fail 'The project does not declare com.unity.ai.assistant.'
}
Write-Pass "Unity Assistant package declared ($assistantVersion)"

if (-not $SkipCodexConfigCheck) {
    $configPath = Join-Path $env:USERPROFILE '.codex\config.toml'
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        Fail "Codex config was not found: $configPath"
    }

    $configText = Get-Content -LiteralPath $configPath -Raw -Encoding utf8
    $sectionPattern = '(?ms)^\[mcp_servers\.' + [regex]::Escape($CodexServerName) + '\]\s*(?<body>.*?)(?=^\[|\z)'
    $sectionMatch = [regex]::Match($configText, $sectionPattern)
    if (-not $sectionMatch.Success) {
        Fail "Codex config has no [mcp_servers.$CodexServerName] section."
    }

    $sectionBody = $sectionMatch.Groups['body'].Value
    if ($sectionBody -notmatch '(?m)^\s*command\s*=') {
        Fail "Codex MCP server '$CodexServerName' has no relay command."
    }
    if ($sectionBody -notmatch '--mcp') {
        Fail "Codex MCP server '$CodexServerName' does not launch the relay with --mcp."
    }
    if ($sectionBody -notmatch '--project-path') {
        Fail "Codex MCP server '$CodexServerName' does not pin a Unity project with --project-path."
    }

    $normalizedSection = $sectionBody.Replace('\\', '\')
    if ($normalizedSection -notlike "*$ExpectedProjectPath*") {
        Fail "Codex MCP server '$CodexServerName' is not pinned to '$ExpectedProjectPath'."
    }

    Write-Pass "Codex config uses the official project-pinned relay ($CodexServerName)"
}

$connectionRoot = Join-Path $env:USERPROFILE '.unity\mcp\connections'
$activeConnection = $null
if (Test-Path -LiteralPath $connectionRoot -PathType Container) {
    $activeConnection = Get-ChildItem -LiteralPath $connectionRoot -Filter 'bridge-*.json' -File |
        Sort-Object LastWriteTime -Descending |
        ForEach-Object {
            try {
                $candidate = Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8 | ConvertFrom-Json
                if ($candidate.project_path -eq $ExpectedProjectPath -and
                    $candidate.connection_type -eq 'named_pipe' -and
                    $null -ne (Get-Process -Id ([int]$candidate.editor_pid) -ErrorAction SilentlyContinue)) {
                    $candidate
                }
            }
            catch {}
        } |
        Select-Object -First 1
}

if ($null -eq $activeConnection) {
    Fail 'Unity official MCP bridge has no active project-matched connection record. Open Unity and start Project Settings > AI > Unity MCP Server.'
}

$pipeNames = @([System.IO.Directory]::GetFiles('\\.\pipe\'))
$expectedPipe = [string]$activeConnection.connection_path
if ($pipeNames -notcontains $expectedPipe) {
    Fail "Unity official MCP bridge record is stale; named pipe is not open: $expectedPipe"
}
Write-Pass "Unity Editor bridge is active on its project-scoped named pipe (PID $($activeConnection.editor_pid))"

if (-not $LiveToolProbe) {
    Write-Host 'Official Unity MCP transport verification passed. Restart Codex to validate the approved client tool session.' -ForegroundColor Green
    return
}

$process = $null
try {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $RelayPath
    # Use the same stable client name as Codex. The Unity bridge approval is
    # client-scoped, so an ephemeral verifier-only name would request approval
    # again on every process launch and make an automated health check hang.
    $startInfo.Arguments = "--mcp --name `"$RelayClientName`" --project-path `"$ExpectedProjectPath`""
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        Fail 'Unity official relay process did not start.'
    }

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $initializeRequest = @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{ name = 'tov-unity-mcp-verifier'; version = '1.0' }
        }
    } | ConvertTo-Json -Depth 8 -Compress
    $process.StandardInput.WriteLine($initializeRequest)
    $process.StandardInput.Flush()

    $initializeResponse = Read-JsonRpcResponse -Process $process -ExpectedId 1 -Deadline $deadline
    if ($null -eq $initializeResponse -or $null -ne $initializeResponse.PSObject.Properties['error']) {
        Fail "Official Unity MCP initialize failed within $WaitSeconds second(s). Open Unity and confirm AI > Unity MCP Server is Running."
    }
    Write-Pass "Official relay initialized MCP protocol $($initializeResponse.result.protocolVersion)"

    $initializedNotification = @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } |
        ConvertTo-Json -Depth 4 -Compress
    $process.StandardInput.WriteLine($initializedNotification)

    $toolsRequest = @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} } |
        ConvertTo-Json -Depth 4 -Compress
    $process.StandardInput.WriteLine($toolsRequest)
    $process.StandardInput.Flush()

    $toolsResponse = Read-JsonRpcResponse -Process $process -ExpectedId 2 -Deadline $deadline
    if ($null -eq $toolsResponse -or $null -ne $toolsResponse.PSObject.Properties['error']) {
        Fail "Official Unity MCP tool discovery failed within $WaitSeconds second(s). Confirm '$RelayClientName' is approved under Pending Connections."
    }

    $toolNames = @($toolsResponse.result.tools | ForEach-Object { [string]$_.name })
    foreach ($requiredTool in @('Unity.ManageScene', 'Unity.ManageGameObject', 'Unity.ReadConsole')) {
        if ($toolNames -notcontains $requiredTool) {
            Fail "Official Unity MCP did not expose required tool '$requiredTool'."
        }
    }
    Write-Pass "Unity Bridge exposed $($toolNames.Count) tool(s), including scene, GameObject, and console tools"
}
finally {
    if ($null -ne $process) {
        try { $process.StandardInput.Close() } catch {}
        if (-not $process.HasExited) {
            try { $process.Kill() } catch {}
        }
        try { $process.WaitForExit(3000) | Out-Null } catch {}
        $process.Dispose()
    }
}

if ($CheckLegacyFallback) {
    $legacyBase = $LegacyBaseUrl.TrimEnd('/')
    try {
        $legacyHealth = Invoke-RestMethod -Uri "$legacyBase/health" -Method Get -TimeoutSec 3
        if ($legacyHealth.status -eq 'healthy') {
            Write-Pass 'Legacy Streamable HTTP MCP fallback is healthy'
        }
        else {
            Write-Warn 'Legacy Streamable HTTP MCP fallback responded without healthy status'
        }
    }
    catch {
        Write-Warn 'Legacy Streamable HTTP MCP fallback is unavailable; official MCP verification still passed'
    }
}

Write-Host 'Official Unity MCP verification passed.' -ForegroundColor Green
