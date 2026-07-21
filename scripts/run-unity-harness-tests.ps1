[CmdletBinding()]
param(
    [ValidateSet('EditMode', 'PlayMode')]
    [string]$Mode = 'EditMode',
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe'
}

if (-not (Test-Path -LiteralPath $UnityEditorPath)) {
    throw "Unity editor was not found: $UnityEditorPath. Set UNITY_EDITOR_PATH or pass -UnityEditorPath."
}

$logs = Join-Path $root 'Logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$resultPath = Join-Path $logs ("harness-{0}-results.xml" -f $Mode.ToLowerInvariant())
$logPath = Join-Path $logs ("harness-{0}.log" -f $Mode.ToLowerInvariant())

Write-Host "Running Unity $Mode harness tests..." -ForegroundColor Cyan
$LASTEXITCODE = 0
& $UnityEditorPath `
    -batchmode `
    -nographics `
    -quit `
    -projectPath $root `
    -runTests `
    -testPlatform $Mode `
    -testResults $resultPath `
    -logFile $logPath

if ($LASTEXITCODE -ne 0) {
    throw "Unity $Mode tests failed with exit code $LASTEXITCODE. See $logPath"
}

Write-Host "Unity $Mode tests passed. Results: $resultPath" -ForegroundColor Green
