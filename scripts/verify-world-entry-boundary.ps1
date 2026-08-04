[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$entryPath = Join-Path $root 'Assets/Scripts/Ontology/Unity/Networking/OntologyWorldAuthorityAccountEntryFlow.cs'
$clientPath = Join-Path $root 'Assets/Scripts/Ontology/Unity/Networking/OntologyWorldAuthorityClient.cs'
$loadingPath = Join-Path $root 'Assets/Scripts/Ontology/UI/OntologyWorldEntryLoadingPanel.cs'

function Get-MethodSource([string]$Source, [string]$Signature) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing required method signature: $Signature" }
    $brace = $Source.IndexOf('{', $start)
    if ($brace -lt 0) { throw "Missing method body: $Signature" }
    $depth = 0
    for ($index = $brace; $index -lt $Source.Length; $index++) {
        if ($Source[$index] -eq '{') { $depth++ }
        elseif ($Source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Source.Substring($start, $index - $start + 1) }
        }
    }
    throw "Unterminated method body: $Signature"
}

$entrySource = Get-Content -LiteralPath $entryPath -Raw -Encoding utf8
$clientSource = Get-Content -LiteralPath $clientPath -Raw -Encoding utf8
$loadingSource = Get-Content -LiteralPath $loadingPath -Raw -Encoding utf8
$entry = Get-MethodSource $entrySource 'private IEnumerator EnterRoutineCore()'
$preflight = Get-MethodSource $clientSource 'public IEnumerator VerifyDevelopmentContentReleaseRoutine('
$launch = Get-MethodSource $loadingSource 'private IEnumerator PrepareThenEnterRoutine()'

# Harness evidence: EntryPathDoesNotAuthorOrMigrateWorld.
# These calls belong to explicit world/content preparation. Checking the entry
# method itself prevents a future refactor from quietly reintroducing one of
# the known transitive durable-authoring paths.
$forbiddenEntryTokens = @(
    'PrepareDevelopmentContentReleaseRoutine',
    'EnsureDevelopmentActionPackageRoutine',
    'EnsureRuntimeZoneRoutine',
    'RegisterAvatarRoutine',
    'PlaceAvatarRoutine',
    'EnsureAvatarRuntimeFoundationRoutine',
    'EnsureAvatarSemanticContractRoutine',
    'PrepareAvatarSemanticContractRoutine',
    'PreparePlayerAvatarRoutine',
    'PrepareSemanticContractRoutine',
    'RepairLegacySemanticContractsRoutine',
    'MigrateLegacyEquipmentRoutine',
    'MigrateLegacyZoneRoutine',
    'CreateCommand(',
    'SendCommandWithRevisionRetryRoutine',
    '/content/packages/',
    '/rules',
    '/actions'
)
foreach ($token in $forbiddenEntryTokens) {
    if ($entry.Contains($token)) {
        throw "World entry contains a durable authoring or migration path: $token"
    }
}

# Harness evidence: EntryReadinessPrecedesRecoveryActivationAndCommit.
$orderedStages = [ordered]@{
    'immutable content preflight' = 'VerifyDevelopmentContentReleaseRoutine'
    'read-only world projection' = 'LoadWorldForEntryWithRetryRoutine'
    'read-only avatar semantic verification' = 'VerifyAvatarSemanticContractRoutine'
    'pending-command recovery' = 'ReplayPendingCommandsRoutine'
    'ephemeral runtime activation' = 'ActivatePlayerRuntimeRoutine'
    'final world-entry commit' = 'EnterWorldRoutine'
}
$previousIndex = -1
foreach ($stage in $orderedStages.GetEnumerator()) {
    $index = $entry.IndexOf($stage.Value, [StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "World entry is missing required stage: $($stage.Key)"
    }
    if ($index -le $previousIndex) {
        throw "World entry stage is out of order: $($stage.Key)"
    }
    $previousIndex = $index
}

$forbiddenPreflightTokens = @(
    'SendCommandRoutine',
    'CreateCommand(',
    'EnsureDevelopmentActionPackageRoutine',
    '/rules',
    '/actions'
)
foreach ($token in $forbiddenPreflightTokens) {
    if ($preflight.Contains($token)) {
        throw "Content preflight contains a mutation/publication token: $token"
    }
}
if (-not $preflight.Contains('/content/preflight')) {
    throw 'Content preflight must use the dedicated read-only Authority endpoint.'
}
if (-not $preflight.Contains('payloadJson')) {
    throw 'Content preflight must send canonicalizable payload identities.'
}

# Existing editable worlds are explicitly prepared by the launch orchestrator,
# outside the admission routine. This keeps legacy worlds enterable without
# reintroducing hidden authoring into EnterRoutineCore.
$prepareIndex = $launch.IndexOf('PrepareSelectedDevelopmentWorld', [StringComparison]::Ordinal)
$enterIndex = $launch.IndexOf('EnterSelectedCharacterInCurrentWorld', [StringComparison]::Ordinal)
if ($prepareIndex -lt 0 -or $enterIndex -lt 0 -or $prepareIndex -ge $enterIndex) {
    throw 'World launch must prepare an editable world before invoking read-only admission.'
}

Write-Host 'World entry preflights immutable content and prepared semantics before recovery, ephemeral activation, and final admission.'
