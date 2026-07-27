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

if (Test-Path -LiteralPath $resultPath) {
    Remove-Item -LiteralPath $resultPath -Force
}

Write-Host "Running Unity $Mode harness tests..." -ForegroundColor Cyan
$arguments = @(
    '-batchmode'
    '-nographics'
    '-quit'
    '-projectPath'
    ('"{0}"' -f $root)
    '-runTests'
    '-testPlatform'
    $Mode
    '-testResults'
    ('"{0}"' -f $resultPath)
    '-logFile'
    ('"{0}"' -f $logPath)
)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $UnityEditorPath
$startInfo.Arguments = $arguments -join ' '
$startInfo.UseShellExecute = $true
$unityProcess = [System.Diagnostics.Process]::Start($startInfo)
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity $Mode tests failed with exit code $unityExitCode. See $logPath"
}

if (-not (Test-Path -LiteralPath $resultPath)) {
    throw "Unity $Mode did not produce a test result file. See $logPath"
}

[xml]$results = Get-Content -LiteralPath $resultPath -Raw
$testRun = $results.'test-run'
if ($null -eq $testRun -or [int]$testRun.total -le 0 -or $testRun.result -ne 'Passed') {
    throw "Unity $Mode test results are missing, empty, or not passed. See $resultPath"
}

Write-Host "Unity $Mode tests passed. Results: $resultPath" -ForegroundColor Green
