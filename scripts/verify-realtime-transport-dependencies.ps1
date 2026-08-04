[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

function Read-Json([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required file: $RelativePath"
    }

    return Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
}

function Assert-LiteNetLibDependencyPin {
    $manifest = Read-Json 'Packages/manifest.json'
    $lock = Read-Json 'Packages/packages-lock.json'
    $projectPath = Join-Path $root 'server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj'
    $project = Get-Content -LiteralPath $projectPath -Raw -Encoding utf8

    $registry = @($manifest.scopedRegistries | Where-Object {
        $_.url -eq 'https://package.openupm.com' -and
        @($_.scopes) -contains 'com.revenantx'
    })
    if ($registry.Count -ne 1) {
        throw 'OpenUPM must be scoped to com.revenantx exactly once.'
    }

    if ([string]$manifest.dependencies.'com.revenantx.litenetlib' -ne '2.1.4') {
        throw 'Unity LiteNetLib dependency must be pinned to 2.1.4.'
    }

    $locked = $lock.dependencies.'com.revenantx.litenetlib'
    if ($null -eq $locked -or [string]$locked.version -ne '2.1.4' -or
        [string]$locked.url -ne 'https://package.openupm.com') {
        throw 'Unity package lock must resolve LiteNetLib 2.1.4 from OpenUPM.'
    }

    if ($project -notmatch '<PackageReference Include="LiteNetLib" Version="2\.1\.4"\s*/>') {
        throw 'Authority LiteNetLib NuGet dependency must be pinned to 2.1.4.'
    }
}

Assert-LiteNetLibDependencyPin
Write-Host '[PASS] LiteNetLib 2.1.4 is pinned consistently for Unity and Authority.' -ForegroundColor Green

