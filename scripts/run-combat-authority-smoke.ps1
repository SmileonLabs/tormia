[CmdletBinding()]
param(
    [string]$BaseUri = 'http://127.0.0.1:5272',
    [int]$TimeoutSec = 10,
    [switch]$UseExistingServices
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $UseExistingServices) {
    & (Join-Path $PSScriptRoot 'run-isolated-combat-authority-smoke.ps1')
    exit $LASTEXITCODE
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Json(
    [string]$Method,
    [string]$Path,
    [object]$Body = $null,
    [string]$AccessToken = $null
) {
    $parameters = @{
        Uri = "$($BaseUri.TrimEnd('/'))$Path"
        Method = $Method
        TimeoutSec = $TimeoutSec
    }
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        $parameters.Headers = @{ Authorization = "Bearer $AccessToken" }
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        $reader =
            New-Object System.IO.StreamReader(
                $response.GetResponseStream())
        try {
            $responseBody = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        throw "$Method $Path failed: $responseBody"
    }
}

function Invoke-RejectedCommand(
    [string]$Path,
    [object]$Body,
    [string]$AccessToken
) {
    try {
        Invoke-Json -Method Post -Path $Path -Body $Body -AccessToken $AccessToken | Out-Null
        throw 'Expected the command to be rejected.'
    }
    catch {
        if ($_.Exception.PSObject.Properties.Name -contains 'Response') {
            $response = $_.Exception.Response
            if ($null -eq $response) { throw }
            $reader =
                New-Object System.IO.StreamReader(
                    $response.GetResponseStream())
            try {
                $body = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        else {
            $message = [string]$_.Exception.Message
            $separator = $message.IndexOf(' failed: ')
            if ($separator -lt 0) { throw }
            $body = $message.Substring($separator + 9)
        }
        if ([string]::IsNullOrWhiteSpace($body)) { throw }
        return $body | ConvertFrom-Json
    }
}

function New-WorldCommand(
    [long]$Revision,
    [string]$CommandType,
    [object]$Payload,
    [guid]$CommandId = [guid]::NewGuid()
) {
    return @{
        contractVersion = 1
        commandId = $CommandId
        expectedRevision = $Revision
        commandType = $CommandType
        payload = $Payload
    }
}

function New-Transform([double]$X, [double]$Z) {
    return @{
        positionX = $X; positionY = 1.0; positionZ = $Z
        rotationX = 0.0; rotationY = 0.0; rotationZ = 0.0
        scaleX = 1.0; scaleY = 1.0; scaleZ = 1.0
    }
}

function Send-Command(
    [guid]$WorldId,
    [string]$Token,
    [ref]$Revision,
    [string]$Kind,
    [object]$Payload,
    [guid]$CommandId = [guid]::NewGuid()
) {
    $command = New-WorldCommand -Revision $Revision.Value `
        -CommandType $Kind -Payload $Payload -CommandId $CommandId
    $result = Invoke-Json -Method Post `
        -Path "/v1/worlds/$WorldId/commands" `
        -AccessToken $Token -Body $command
    Assert-True $result.accepted "Command '$Kind' was rejected: $($result.rejectionCode)"
    $Revision.Value = [long]$result.revision
    return @{ command = $command; result = $result }
}

function Add-CanonicalFact(
    [guid]$WorldId,
    [string]$Token,
    [ref]$Revision,
    [guid]$Subject,
    [string]$Predicate,
    [string]$Value
) {
    Send-Command -WorldId $WorldId -Token $Token -Revision $Revision `
        -Kind 'set_authored_fact' -Payload @{
            subjectEntityId = $Subject
            predicateId = $Predicate
            objectKind = 'canonical'
            objectEntityId = $null
            objectCanonicalId = $Value
            objectValueJson = $null
        } | Out-Null
}

function Add-NumberFact(
    [guid]$WorldId,
    [string]$Token,
    [ref]$Revision,
    [guid]$Subject,
    [string]$Predicate,
    [long]$Value
) {
    Send-Command -WorldId $WorldId -Token $Token -Revision $Revision `
        -Kind 'set_authored_fact' -Payload @{
            subjectEntityId = $Subject
            predicateId = $Predicate
            objectKind = 'number'
            objectEntityId = $null
            objectCanonicalId = $null
            objectValueJson = $Value.ToString(
                [Globalization.CultureInfo]::InvariantCulture)
        } | Out-Null
}

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 12)
$email = "combat-$suffix@tormia.local"
$password = "Tormia-$suffix-A9!"
$packageId = "combat_$suffix"
$reusePackageId = "combat_reuse_$suffix"
$packageVersion = '1.0.0'
$attackActionId = "attack_$suffix"
$attackRuleId = "MeleeAttackOnPrimaryIntent_$suffix"
$swingActionId = "swing_weapon_$suffix"
$swingRuleId = "SwingWeaponOnPrimaryIntent_$suffix"
$equipActionId = "equip_$suffix"
$equipRuleId = "EquipItemOnInteractionIntent_$suffix"
$lootRuleId = "LootBecomesAvailableOnDefeat_$suffix"
$moveActionId = "move_avatar_$suffix"
$moveRuleId = "MovePlayerFromIntent_$suffix"
$legacyEquipActionId = "legacy_equip_$suffix"
$avatarId = [guid]::NewGuid()
$weaponId = [guid]::NewGuid()
$monsterId = [guid]::NewGuid()
$attackRuleApplicationId = [guid]::NewGuid()
$attackRuleBindingId = [guid]::NewGuid()
$swingRuleBindingId = [guid]::NewGuid()
$equipRuleBindingId = [guid]::NewGuid()
$lootRuleApplicationId = [guid]::NewGuid()
$lootRuleBindingId = [guid]::NewGuid()
$moveRuleApplicationId = [guid]::NewGuid()
$moveRuleBindingId = [guid]::NewGuid()

Write-Host 'TOV Authority combat vertical-slice smoke' -ForegroundColor Cyan

$registration = Invoke-Json -Method Post -Path '/v1/auth/register' -Body @{
    email = $email
    displayName = "Combat $suffix"
    password = $password
}
$token = [string]$registration.accessToken
$character = Invoke-Json -Method Post -Path '/v1/account/characters' `
    -AccessToken $token -Body @{
        displayName = 'Combat Character'
        templateId = 'PlayerAvatar'
        equippedPartIds = @()
    }
$world = Invoke-Json -Method Post -Path '/v1/worlds' -AccessToken $token -Body @{
    slug = "combat-$suffix"
    title = "Combat World $suffix"
    visibility = 'private'
}
$worldId = [guid]$world.worldId
$projection = Invoke-Json -Method Get -Path "/v1/worlds/$worldId" -AccessToken $token
$revision = [long]$projection.revision

$attackDefinition = @{
    actionVerb = $attackActionId
    requiresTool = $true
    ruleInvocation = @{
        ruleId = $attackRuleId
        bindingVariable = '?tool'
        bindingEntityPattern = '?tool'
        intentSubjectPattern = '?actor'
        intentPredicate = 'primary_attack_intent'
        intentObjectPattern = '?target'
    }
    runtimeConstraints = @{
        maxActorTargetDistanceFrom = @{
            subject = '?tool'
            predicate = 'attack_range'
            multiplier = 1
        }
        cooldownSecondsFrom = @{
            subject = '?tool'
            predicate = 'attack_cooldown'
            multiplier = 1
        }
    }
    presentation = @{
        actorAnimationIntent = 'AttackLight'
    }
    postRuleInvocations = @(
        @{
            ruleId = $lootRuleId
            bindingVariable = '?target'
            bindingEntityPattern = '?target'
            intentSubjectPattern = '?target'
            intentPredicate = 'defeat_resolved_intent'
            intentObjectPattern = '?target'
            required = $false
        }
    )
}
$attackRuleDefinition = @{
    id = $attackRuleId
    catalogVersion = 1
    description = 'An equipped melee weapon owns primary attack behavior.'
    conditions = @(
        @{ kind = 0; subject = '?actor'; predicate = 'primary_attack_intent'; obj = '?target' }
        @{ kind = 2; subject = '?actor'; predicate = ''; obj = 'Actor' }
        @{ kind = 2; subject = '?tool'; predicate = ''; obj = 'Weapon' }
        @{ kind = 0; subject = '?tool'; predicate = 'has_rule_block'; obj = $attackRuleId }
        @{ kind = 0; subject = '?tool'; predicate = 'equipped_by'; obj = '?actor' }
        @{ kind = 0; subject = '?tool'; predicate = 'grants_capability'; obj = 'MeleeAttack' }
        @{ kind = 2; subject = '?target'; predicate = ''; obj = 'Damageable' }
        @{ kind = 0; subject = '?target'; predicate = 'combat_disposition'; obj = 'Hostile' }
        @{ kind = 0; subject = '?target'; predicate = 'is_alive'; obj = 'True' }
    )
    effects = @(
        @{ kind = 3; subject = '?target'; predicate = 'current_health'; obj = '0'; minimum = '0'; maximum = ''
           valueFrom = @{ subject = '?tool'; predicate = 'attack_damage'; multiplier = -1 } }
        @{ kind = 2; subject = '?target'; predicate = 'is_alive'; obj = 'False'
           when = @{ subject = '?target'; predicate = 'current_health'; comparison = 2; value = '0' } }
    )
}
$lootRuleDefinition = @{
    id = $lootRuleId
    catalogVersion = 1
    description = 'Defeat exposes authored loot through a separate lifecycle Rule Block.'
    conditions = @(
        @{ kind = 0; subject = '?target'; predicate = 'defeat_resolved_intent'; obj = '?target' }
        @{ kind = 2; subject = '?target'; predicate = ''; obj = 'Damageable' }
        @{ kind = 0; subject = '?target'; predicate = 'has_rule_block'; obj = $lootRuleId }
        @{ kind = 0; subject = '?target'; predicate = 'is_alive'; obj = 'False' }
        @{ kind = 0; subject = '?target'; predicate = 'current_health'; obj = '0' }
        @{ kind = 0; subject = '?target'; predicate = 'loot_item'; obj = '?lootItem' }
        @{ kind = 1; subject = '?target'; predicate = 'loot_status'; obj = 'Available' }
    )
    effects = @(
        @{ kind = 2; subject = '?target'; predicate = 'loot_status'; obj = 'Available' }
    )
}
$swingDefinition = @{
    actionVerb = $swingActionId
    requiresTool = $true
    ruleInvocation = @{
        ruleId = $swingRuleId
        bindingVariable = '?tool'
        bindingEntityPattern = '?tool'
        intentSubjectPattern = '?actor'
        intentPredicate = 'primary_swing_intent'
        intentObjectPattern = '?actor'
    }
    runtimeConstraints = @{
        cooldownSecondsFrom = @{
            subject = '?tool'
            predicate = 'attack_cooldown'
            multiplier = 1
        }
    }
}
$swingRuleDefinition = @{
    id = $swingRuleId
    catalogVersion = 1
    description = 'An equipped melee weapon owns one ephemeral swing.'
    conditions = @(
        @{ kind = 0; subject = '?actor'; predicate = 'primary_swing_intent'; obj = '?actor' }
        @{ kind = 2; subject = '?actor'; predicate = ''; obj = 'Actor' }
        @{ kind = 2; subject = '?tool'; predicate = ''; obj = 'Weapon' }
        @{ kind = 0; subject = '?tool'; predicate = 'has_rule_block'; obj = $swingRuleId }
        @{ kind = 0; subject = '?tool'; predicate = 'equipped_by'; obj = '?actor' }
        @{ kind = 0; subject = '?tool'; predicate = 'grants_capability'; obj = 'MeleeAttack' }
    )
    effects = @()
    runtimePresentation = @{
        actorAnimationIntent = 'AttackLight'
    }
}
$equipDefinition = @{
    actionVerb = $equipActionId
    subjectPattern = '?actor'
    predicate = ''
    objectPattern = '?target'
    requiresTool = $false
    ruleInvocation = @{
        ruleId = $equipRuleId
        bindingVariable = '?target'
        bindingEntityPattern = '?target'
        intentSubjectPattern = '?actor'
        intentPredicate = 'interaction_intent'
        intentObjectPattern = '?target'
    }
    runtimeConstraints = @{
        maxActorTargetDistanceFrom = @{
            subject = '?target'
            predicate = 'interaction_range'
            multiplier = 1
        }
    }
}
$equipRuleDefinition = @{
    id = $equipRuleId
    catalogVersion = 1
    description = 'An interaction intent equips an authored item while this Rule Block is assigned.'
    conditions = @(
        @{ kind = 0; subject = '?actor'; predicate = 'interaction_intent'; obj = '?target' }
        @{ kind = 2; subject = '?actor'; predicate = ''; obj = 'Actor' }
        @{ kind = 2; subject = '?target'; predicate = ''; obj = 'Weapon' }
        @{ kind = 0; subject = '?target'; predicate = 'has_rule_block'; obj = $equipRuleId }
        @{ kind = 0; subject = '?target'; predicate = 'can_equip'; obj = 'True' }
        @{ kind = 0; subject = '?target'; predicate = 'has_slot'; obj = '?slot' }
        @{ kind = 5; subject = '?actor'; predicate = ''; obj = '?target' }
    )
    effects = @(
        @{ kind = 2; subject = '?target'; predicate = 'equipped_by'; obj = '?actor' }
        @{ kind = 2; subject = '?actor'; predicate = 'equipped_item'; obj = '?target' }
    )
}
$moveDefinition = @{
    actionVerb = $moveActionId
    objectPattern = '?actor'
    requiresTool = $false
    ruleInvocation = @{
        ruleId = $moveRuleId
        bindingVariable = '?actor'
        bindingEntityPattern = '?actor'
        intentSubjectPattern = '?actor'
        intentPredicate = 'locomotion_intent'
        intentObjectPattern = '?actor'
    }
}
$moveRuleDefinition = @{
    id = $moveRuleId
    catalogVersion = 1
    description = 'Authority approves ephemeral player locomotion through an assigned Rule Block.'
    conditions = @(
        @{ kind = 0; subject = '?actor'; predicate = 'locomotion_intent'; obj = '?actor' }
        @{ kind = 2; subject = '?actor'; predicate = ''; obj = 'Actor' }
        @{ kind = 2; subject = '?actor'; predicate = ''; obj = 'PlayerControlled' }
        @{ kind = 0; subject = '?actor'; predicate = 'has_rule_block'; obj = $moveRuleId }
        @{ kind = 0; subject = '?actor'; predicate = 'grants_capability'; obj = 'Locomotion' }
        @{ kind = 0; subject = '?actor'; predicate = 'physical_profile'; obj = 'AuthorityKinematic' }
        @{ kind = 0; subject = '?actor'; predicate = 'is_alive'; obj = 'True' }
        @{ kind = 0; subject = '?actor'; predicate = 'movement_speed'; obj = '?speed' }
    )
    effects = @()
    runtimePresentation = @{
        actorAnimationIntent = 'Locomotion'
    }
}
$legacyEquipDefinition = @{
    actionVerb = $legacyEquipActionId
    subjectPattern = '?actor'
    predicate = 'equips'
    objectPattern = '?target'
    requiresTool = $false
    conditions = @(
        @{ kind = 2; subject = '?target'; predicate = ''; obj = 'Weapon' }
    )
    effects = @(
        @{ kind = 2; subject = '?actor'; predicate = 'equips'; obj = '?target' }
    )
}
$published = Invoke-Json -Method Post `
    -Path "/v1/content/packages/$packageId/actions" `
    -AccessToken $token -Body @{
        packageVersion = $packageVersion
        actions = @(
            @{
                actionId = $attackActionId
                definitionVersion = 1
                payloadJson = $attackDefinition | ConvertTo-Json -Depth 20 -Compress
            }
            @{
                actionId = $swingActionId
                definitionVersion = 1
                payloadJson = $swingDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                actionId = $equipActionId
                definitionVersion = 1
                payloadJson = $equipDefinition | ConvertTo-Json -Depth 20 -Compress
            }
            @{
                actionId = $legacyEquipActionId
                definitionVersion = 1
                payloadJson = $legacyEquipDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                actionId = $moveActionId
                definitionVersion = 1
                payloadJson = $moveDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
        )
    }
Assert-True $published.accepted 'Combat action package was not published.'

$publishedRule = Invoke-Json -Method Post `
    -Path "/v1/content/packages/$packageId/rules" `
    -AccessToken $token -Body @{
        packageVersion = $packageVersion
        rules = @(
            @{
                ruleId = $attackRuleId
                definitionVersion = 1
                payloadJson = $attackRuleDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $swingRuleId
                definitionVersion = 1
                payloadJson = $swingRuleDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $equipRuleId
                definitionVersion = 1
                payloadJson = $equipRuleDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $lootRuleId
                definitionVersion = 1
                payloadJson = $lootRuleDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $moveRuleId
                definitionVersion = 1
                payloadJson = $moveRuleDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
        )
    }
Assert-True $publishedRule.accepted `
    'Primary attack Rule Block was not published.'

# One content release owns one transaction across Rule and Action definitions.
# An Action conflict must retract the Rule inserted earlier in that transaction.
$atomicRuleAfterActionConflictId = 'AtomicRuleAfterActionConflict'
$atomicRuleAfterActionConflict = $moveRuleDefinition.Clone()
$atomicRuleAfterActionConflict.id = $atomicRuleAfterActionConflictId
$atomicRuleAfterActionConflict.description =
    'Must not survive an Action conflict in the same release.'
$conflictingEquipDefinition = $equipDefinition.Clone()
$conflictingEquipDefinition.requiresTool = $true
$actionConflictRelease = Invoke-RejectedCommand `
    -Path "/v1/content/packages/$packageId/releases" `
    -AccessToken $token -Body @{
        packageVersion = $packageVersion
        rules = @(@{
            ruleId = $atomicRuleAfterActionConflictId
            definitionVersion = 1
            payloadJson = $atomicRuleAfterActionConflict |
                ConvertTo-Json -Depth 20 -Compress
        })
        actions = @(@{
            actionId = $equipActionId
            definitionVersion = 1
            payloadJson = $conflictingEquipDefinition |
                ConvertTo-Json -Depth 20 -Compress
        })
    }
Assert-True ($actionConflictRelease.rejectionCode -like `
        'action_definition_version_conflict:*') `
    "Atomic release did not report its Action conflict: $($actionConflictRelease.rejectionCode)"
$rulesAfterActionConflict = Invoke-Json -Method Get `
    -Path "/v1/content/packages/$packageId/rules" -AccessToken $token
Assert-True (-not ($rulesAfterActionConflict.rules | Where-Object {
        $_.ruleId -eq $atomicRuleAfterActionConflictId
    })) 'Action conflict left a partially published Rule.'

# Conversely, a Rule conflict must leave no Action from the rejected release.
$atomicActionAfterRuleConflictId = 'atomic_action_after_rule_conflict'
$atomicActionAfterRuleConflict = $equipDefinition.Clone()
$atomicActionAfterRuleConflict.actionVerb = $atomicActionAfterRuleConflictId
$conflictingEquipRule = $equipRuleDefinition.Clone()
$conflictingEquipRule.description =
    'Changed immutable payload that must reject the complete release.'
$ruleConflictRelease = Invoke-RejectedCommand `
    -Path "/v1/content/packages/$packageId/releases" `
    -AccessToken $token -Body @{
        packageVersion = $packageVersion
        rules = @(@{
            ruleId = $equipRuleId
            definitionVersion = 1
            payloadJson = $conflictingEquipRule |
                ConvertTo-Json -Depth 20 -Compress
        })
        actions = @(@{
            actionId = $atomicActionAfterRuleConflictId
            definitionVersion = 1
            payloadJson = $atomicActionAfterRuleConflict |
                ConvertTo-Json -Depth 20 -Compress
        })
    }
Assert-True ($ruleConflictRelease.rejectionCode -like `
        'rule_definition_version_conflict:*') `
    'Atomic release did not report its Rule conflict.'
$actionsAfterRuleConflict = Invoke-Json -Method Get `
    -Path "/v1/content/packages/$packageId/actions" -AccessToken $token
Assert-True (-not ($actionsAfterRuleConflict.actions | Where-Object {
        $_.actionId -eq $atomicActionAfterRuleConflictId
    })) 'Rule conflict left a partially published Action.'

$reusedDefinition = Invoke-Json -Method Post `
    -Path "/v1/content/packages/$reusePackageId/actions" `
    -AccessToken $token -Body @{
        packageVersion = $packageVersion
        actions = @(
            @{
                actionId = $equipActionId
                definitionVersion = 1
                payloadJson = $equipDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
        )
    }
Assert-True $reusedDefinition.accepted `
    'The same immutable action identity could not be reused in another package.'

Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'define_zone' -Payload @{
        zoneKey = 'world_main'
        minX = -100.0
        minZ = -100.0
        maxX = 100.0
        maxZ = 100.0
        simulationMode = 'active'
    } | Out-Null

foreach ($entity in @(
    @{ id = $avatarId; template = 'PlayerAvatar'; name = 'Combat Avatar'; x = 0.0; z = 0.0; facts = @(
        @{ predicateId = 'has_concept'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Actor'; objectValueJson = $null }
        @{ predicateId = 'has_concept'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'PlayerControlled'; objectValueJson = $null }
        @{ predicateId = 'grants_capability'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Locomotion'; objectValueJson = $null }
        @{ predicateId = 'physical_profile'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'AuthorityKinematic'; objectValueJson = $null }
        @{ predicateId = 'is_alive'; objectKind = 'boolean'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = 'true' }
        @{ predicateId = 'locomotion_action'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = $moveActionId; objectValueJson = $null }
        @{ predicateId = 'movement_speed'; objectKind = 'number'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = '5' }
    ) }
    @{ id = $weaponId; template = 'Sword01Bronze'; name = 'Bronze Sword'; x = 1.0; z = 0.0; facts = @(
        @{ predicateId = 'has_concept'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Weapon'; objectValueJson = $null }
        @{ predicateId = 'has_concept'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Sword'; objectValueJson = $null }
        @{ predicateId = 'grants_capability'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'MeleeAttack'; objectValueJson = $null }
        @{ predicateId = 'attack_action'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = $attackActionId; objectValueJson = $null }
        @{ predicateId = 'swing_action'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = $swingActionId; objectValueJson = $null }
        @{ predicateId = 'attack_range'; objectKind = 'number'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = '3' }
        @{ predicateId = 'attack_cooldown'; objectKind = 'number'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = '0.05' }
        @{ predicateId = 'interaction_range'; objectKind = 'number'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = '3' }
        @{ predicateId = 'can_equip'; objectKind = 'boolean'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = 'true' }
        @{ predicateId = 'has_slot'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'RightHand'; objectValueJson = $null }
    ) }
    @{ id = $monsterId; template = 'BeholderBasic'; name = 'Beholder'; x = 2.0; z = 0.0; facts = @(
        @{ predicateId = 'has_concept'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Damageable'; objectValueJson = $null }
        @{ predicateId = 'combat_disposition'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'Hostile'; objectValueJson = $null }
        @{ predicateId = 'is_alive'; objectKind = 'boolean'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = 'true' }
        @{ predicateId = 'loot_item'; objectKind = 'canonical'; objectEntityId = $null; objectCanonicalId = 'OntologyDataFragment'; objectValueJson = $null }
        @{ predicateId = 'current_health'; objectKind = 'number'; objectEntityId = $null; objectCanonicalId = $null; objectValueJson = '30' }
    ) }
)) {
    Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
        -Kind 'place_entity' -Payload @{
            entityId = $entity.id
            templateId = $entity.template
            templateVersion = 1
            displayName = $entity.name
            zoneKey = 'world_main'
            transform = New-Transform $entity.x $entity.z
            initialFacts = $entity.facts
        } | Out-Null
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'register_player_avatar' -Payload @{ entityId = $avatarId } | Out-Null

Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'set_content_package' -Payload @{
        packageId = $packageId; packageVersion = $packageVersion; enabled = $true
    } | Out-Null

$semanticContractApplicationId = [guid]::NewGuid()
$semanticContractBindingId = [guid]::NewGuid()
$semanticContract = @{
    applicationId = $semanticContractApplicationId
    targetEntityId = $avatarId
    slotId = 'smoke_semantic_contract'
    contractId = 'smoke_avatar_contract'
    contractVersion = 1
    packageId = $packageId
    packageVersion = $packageVersion
    versionPredicateId = 'smoke_contract_version'
    checksumPredicateId = 'smoke_contract_checksum'
    adoptExistingContributions = $false
    replacePredicateIds = @('smoke_contract_capability')
    requiredConceptIds = @()
    authoredFacts = @(@{
        predicateId = 'smoke_contract_capability'
        objectKind = 'canonical'
        objectEntityId = $null
        objectCanonicalId = 'Enabled'
        objectValueJson = $null
    })
    ruleBlocks = @(@{
        bindingId = $semanticContractBindingId
        ruleId = $moveRuleId
        ruleVersion = 1
        parameterValuesJson = '{"bindingVariable":"?actor"}'
    })
    requiresOwnedBinding = $true
}
$semanticBefore = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/semantic-contracts/preflight" `
    -AccessToken $token -Body $semanticContract
Assert-True (-not $semanticBefore.ready) `
    'An unapplied semantic contract was reported ready.'
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'prepare_semantic_contract' -Payload $semanticContract | Out-Null
$semanticAfter = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/semantic-contracts/preflight" `
    -AccessToken $token -Body $semanticContract
Assert-True $semanticAfter.ready `
    'Atomic semantic contract preparation did not converge.'

$revisionBeforeSemanticNoOp = $revision
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'prepare_semantic_contract' -Payload $semanticContract | Out-Null
Assert-True ($revision -eq $revisionBeforeSemanticNoOp) `
    'Already-ready semantic contract advanced the world revision.'

$semanticUpgrade = $semanticContract.Clone()
$semanticUpgrade.applicationId = [guid]::NewGuid()
$semanticUpgrade.contractVersion = 2
$semanticUpgrade.authoredFacts = @(
    $semanticContract.authoredFacts[0]
    @{
        predicateId = 'smoke_contract_upgrade'
        objectKind = 'canonical'
        objectEntityId = $null
        objectCanonicalId = 'Complete'
        objectValueJson = $null
    }
)
$semanticUpgrade.replacePredicateIds = @(
    'smoke_contract_capability', 'smoke_contract_upgrade')
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'prepare_semantic_contract' -Payload $semanticUpgrade | Out-Null
$semanticUpgradeReady = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/semantic-contracts/preflight" `
    -AccessToken $token -Body $semanticUpgrade
Assert-True $semanticUpgradeReady.ready `
    'Partial old semantic contract did not upgrade atomically.'

$invalidSemanticContract = $semanticUpgrade.Clone()
$invalidSemanticContract.applicationId = [guid]::NewGuid()
$invalidSemanticContract.contractVersion = 3
$invalidSemanticContract.ruleBlocks = @(@{
    bindingId = [guid]::NewGuid()
    ruleId = 'UnpublishedSemanticContractRule'
    ruleVersion = 1
    parameterValuesJson = '{"bindingVariable":"?actor"}'
})
$invalidSemanticResult = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" -AccessToken $token -Body @{
        contractVersion = 1
        commandId = [guid]::NewGuid()
        expectedRevision = $revision
        commandType = 'prepare_semantic_contract'
        payload = $invalidSemanticContract
    }
Assert-True ($invalidSemanticResult.rejectionCode -eq `
        'rule_definition_not_published') `
    'Unpublished semantic Rule did not fail closed.'
$stillVersionTwo = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/semantic-contracts/preflight" `
    -AccessToken $token -Body $semanticUpgrade
Assert-True $stillVersionTwo.ready `
    'Rejected semantic upgrade damaged the previously active contract.'

$attackRuleMeaning = @{
    operation = 'apply'
    applicationId = $attackRuleApplicationId
    targetEntityId = $weaponId
    slotId = 'primary_attack'
    packageId = $packageId
    replacePredicateIds = @()
    requiredConceptIds = @()
    authoredFacts = @()
    ruleBlocks = @(
        @{
            bindingId = $attackRuleBindingId
            ruleId = $attackRuleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?tool"}'
        }
        @{
            bindingId = $swingRuleBindingId
            ruleId = $swingRuleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?tool"}'
        }
        @{
            bindingId = $equipRuleBindingId
            ruleId = $equipRuleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?target"}'
        }
    )
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'apply_meaning_package' -Payload $attackRuleMeaning | Out-Null

$lootRuleMeaning = @{
    operation = 'apply'
    applicationId = $lootRuleApplicationId
    targetEntityId = $monsterId
    slotId = 'defeat_loot'
    packageId = $packageId
    replacePredicateIds = @()
    requiredConceptIds = @()
    authoredFacts = @()
    ruleBlocks = @(
        @{
            bindingId = $lootRuleBindingId
            ruleId = $lootRuleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?target"}'
        }
    )
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'apply_meaning_package' -Payload $lootRuleMeaning | Out-Null

$moveRuleMeaning = @{
    operation = 'apply'
    applicationId = $moveRuleApplicationId
    targetEntityId = $avatarId
    slotId = 'player_locomotion'
    packageId = $packageId
    replacePredicateIds = @()
    requiredConceptIds = @()
    authoredFacts = @()
    ruleBlocks = @(
        @{
            bindingId = $moveRuleBindingId
            ruleId = $moveRuleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?actor"}'
        }
    )
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'apply_meaning_package' -Payload $moveRuleMeaning | Out-Null

$oldSessionIntent = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/runtime/intents" `
    -AccessToken $token -Body @{
        avatarEntityId = $avatarId
        zoneKey = 'world_main'
        sequence = 900
        moveX = 1.0
        moveZ = 0.0
        moveSpeed = 4.0
        jump = $false
        packageId = $packageId
        packageVersion = $packageVersion
        actionId = $moveActionId
        definitionVersion = 1
    }
Assert-True $oldSessionIntent.accepted `
    'Authority did not accept the synthetic prior-session intent.'

$activation = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/runtime/avatars/$avatarId/activate" `
    -AccessToken $token -Body @{ zoneKey = 'world_main' }
Assert-True ($activation.accepted -and
    $activation.state.zoneKey -eq 'world_main' -and
    [math]::Abs([double]$activation.state.positionX) -lt 0.0001 -and
    [math]::Abs([double]$activation.state.positionZ) -lt 0.0001) `
    'World entry did not reset runtime motion to the durable avatar transform.'

$newSessionIntent = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/runtime/intents" `
    -AccessToken $token -Body @{
        avatarEntityId = $avatarId
        zoneKey = 'world_main'
        sequence = 1
        moveX = 0.0
        moveZ = 0.0
        moveSpeed = 0.0
        jump = $false
        packageId = $packageId
        packageVersion = $packageVersion
        actionId = $moveActionId
        definitionVersion = 1
    }
Assert-True $newSessionIntent.accepted `
    'World entry did not clear the prior transient intent sequence.'

$motion = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId/runtime/avatars/$avatarId" `
    -AccessToken $token
Assert-True ($null -ne $motion -and $motion.zoneKey -eq 'world_main') `
    'Authority did not publish the activated avatar runtime position.'

$legacyEquip = @{
    actorEntityId = $avatarId
    targetEntityId = $weaponId
    toolEntityId = $null
    packageId = $packageId
    packageVersion = $packageVersion
    actionId = $legacyEquipActionId
    definitionVersion = 1
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $legacyEquip | Out-Null
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'migrate_legacy_equipment_relations' -Payload @{} | Out-Null
$afterLegacyMigration = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
Assert-True (@($afterLegacyMigration.facts | Where-Object {
    $_.predicateId -eq 'equips'
}).Count -eq 0) 'Legacy action-owned equips relations were not retracted.'

$equip = @{
    actorEntityId = $avatarId
    targetEntityId = $weaponId
    toolEntityId = $null
    packageId = $packageId
    packageVersion = $packageVersion
    actionId = $equipActionId
    definitionVersion = 1
}
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $equip | Out-Null

$attack = @{
    actorEntityId = $avatarId
    targetEntityId = $monsterId
    toolEntityId = $weaponId
    packageId = $packageId
    packageVersion = $packageVersion
    actionId = $attackActionId
    definitionVersion = 1
}

Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'remove_rule_block' -Payload @{
        bindingId = $attackRuleBindingId
    } | Out-Null
$missingRuleCommand = New-WorldCommand -Revision $revision `
    -CommandType 'execute_action' -Payload $attack
$missingRule = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" `
    -Body $missingRuleCommand -AccessToken $token
Assert-True ($missingRule.rejectionCode -eq 'action_rule_block_not_assigned') `
    'Attack still worked after its Rule Block was removed.'

$attackRuleApplicationId = [guid]::NewGuid()
$attackRuleBindingId = [guid]::NewGuid()
$swingRuleBindingId = [guid]::NewGuid()
$equipRuleBindingId = [guid]::NewGuid()
$attackRuleMeaning.applicationId = $attackRuleApplicationId
$attackRuleMeaning.ruleBlocks[0].bindingId = $attackRuleBindingId
$attackRuleMeaning.ruleBlocks[1].bindingId = $swingRuleBindingId
$attackRuleMeaning.ruleBlocks[2].bindingId = $equipRuleBindingId
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'apply_meaning_package' -Payload $attackRuleMeaning | Out-Null

# Removing a package-owned attack binding removes the package's complete
# contribution, including equip, and retracts its rule-produced equipment
# relation. Reapplying definitions does not replay the user's equip intent.
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $equip | Out-Null

$beforeSwing = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
$equippedBeforeSwing = @($beforeSwing.facts | Where-Object {
    $_.subjectEntityId -eq $weaponId.ToString() -and
    $_.predicateId -eq 'equipped_by' -and
    $_.objectEntityId -eq $avatarId.ToString()
})
Assert-True ($equippedBeforeSwing.Count -eq 1) `
    ("The Rule-Block-owned equipment relation was missing before swing " +
     "preview: " +
     (@($beforeSwing.facts | Where-Object {
         $_.subjectEntityId -eq $weaponId.ToString() -and
         $_.predicateId -in @(
             'equipped_by',
             'has_rule_block',
             'can_equip',
             'has_slot')
     }) | ConvertTo-Json -Depth 10 -Compress))
$swingPreview = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/actions/preview" `
    -AccessToken $token -Body @{
        actorEntityId = $avatarId
        targetEntityId = $avatarId
        toolEntityId = $weaponId
        packageId = $packageId
        packageVersion = $packageVersion
        actionId = $swingActionId
        definitionVersion = 1
    }
Assert-True ($swingPreview.accepted -and
    $swingPreview.actorAnimationIntent -eq 'AttackLight' -and
    [int]$swingPreview.mutationCount -eq 0) `
    ("Authority preview did not evaluate the assigned swing Rule Block: " +
     ($swingPreview | ConvertTo-Json -Depth 10 -Compress))
$afterSwingPreview = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
Assert-True ([long]$afterSwingPreview.revision -eq [long]$beforeSwing.revision) `
    'The swing preview incorrectly advanced the durable world revision.'
$swing = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/runtime/actions" `
    -AccessToken $token -Body @{
        actorEntityId = $avatarId
        targetEntityId = $avatarId
        toolEntityId = $weaponId
        packageId = $packageId
        packageVersion = $packageVersion
        actionId = $swingActionId
        definitionVersion = 1
    }
Assert-True ($swing.accepted -and
    $swing.actorAnimationIntent -eq 'AttackLight') `
    'Authority did not approve the Rule-Block-owned empty swing presentation.'
$afterSwing = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
Assert-True ([long]$afterSwing.revision -eq [long]$beforeSwing.revision) `
    'The presentation-only swing incorrectly advanced the durable world revision.'
$swingCooldown = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/runtime/actions" `
    -AccessToken $token -Body @{
        actorEntityId = $avatarId
        targetEntityId = $avatarId
        toolEntityId = $weaponId
        packageId = $packageId
        packageVersion = $packageVersion
        actionId = $swingActionId
        definitionVersion = 1
    }
Assert-True (-not $swingCooldown.accepted -and
    $swingCooldown.rejectionCode -eq 'action_cooldown_active') `
    'The empty swing did not enforce the weapon-authored ephemeral cooldown.'

$missingDamageCommand = New-WorldCommand -Revision $revision `
    -CommandType 'execute_action' -Payload $attack
$missingDamage = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" `
    -Body $missingDamageCommand -AccessToken $token
Assert-True ($missingDamage.rejectionCode -eq 'action_numeric_source_missing') `
    'Attack still worked after the weapon damage fact was removed.'
Add-NumberFact $worldId $token ([ref]$revision) $weaponId 'attack_damage' 10
$beforeAttackPreview = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
$healthBeforePreview = @($beforeAttackPreview.facts | Where-Object {
    $_.subjectEntityId -eq $monsterId -and
    $_.predicateId -eq 'current_health'
})[0].objectValueJson
$attackPreview = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/actions/preview" `
    -AccessToken $token -Body $attack
Assert-True ($attackPreview.accepted -and
    [int]$attackPreview.mutationCount -gt 0) `
    'Authority preview did not evaluate the assigned attack Rule Block.'
$afterAttackPreview = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -AccessToken $token
$healthAfterPreview = @($afterAttackPreview.facts | Where-Object {
    $_.subjectEntityId -eq $monsterId -and
    $_.predicateId -eq 'current_health'
})[0].objectValueJson
Assert-True ([long]$afterAttackPreview.revision -eq
    [long]$beforeAttackPreview.revision) `
    'The attack preview incorrectly advanced the durable world revision.'
Assert-True ([string]$healthAfterPreview -eq [string]$healthBeforePreview) `
    'The attack preview incorrectly applied its calculated damage mutation.'
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $attack | Out-Null
$cooldownCommand = New-WorldCommand -Revision $revision `
    -CommandType 'execute_action' -Payload $attack
$cooldownRejection = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" `
    -Body $cooldownCommand -AccessToken $token
Assert-True ($cooldownRejection.rejectionCode -eq 'action_cooldown_active') `
    'The tool-authored attack cooldown was not enforced.'
Start-Sleep -Milliseconds 100
Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $attack | Out-Null
Start-Sleep -Milliseconds 100
$thirdId = [guid]::NewGuid()
$third = Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'execute_action' -Payload $attack -CommandId $thirdId
$deathRevision = $revision
$replay = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/commands" `
    -AccessToken $token -Body $third.command
Assert-True ($replay.accepted -and $replay.isReplay) `
    'The final attack command was not replay-safe.'
Assert-True ([long]$replay.revision -eq $deathRevision) `
    'A replay changed the combat revision.'

$afterDeath = Invoke-Json -Method Get -Path "/v1/worlds/$worldId" -AccessToken $token
$projectedAttack = @($afterDeath.actions | Where-Object {
    $_.packageId -eq $packageId -and
    $_.packageVersion -eq $packageVersion -and
    $_.actionId -eq $attackActionId -and
    [int]$_.definitionVersion -eq 1
})
Assert-True ($projectedAttack.Count -eq 1 -and
    $projectedAttack[0].actorAnimationIntent -eq 'AttackLight') `
    'The exact accepted action version did not project its actor animation intent.'
$monsterFacts = @($afterDeath.facts | Where-Object {
    $_.subjectEntityId -eq $monsterId.ToString()
})
Assert-True (@($monsterFacts | Where-Object {
    $_.predicateId -eq 'current_health' -and [long]$_.objectValueJson -eq 0
}).Count -eq 1) 'Monster health was not persisted at zero.'
Assert-True (@($monsterFacts | Where-Object {
    $_.predicateId -eq 'is_alive' -and $_.objectValueJson -eq 'false'
}).Count -eq 1) 'The guarded death transition was not persisted.'
Assert-True (@($monsterFacts | Where-Object {
    $_.predicateId -eq 'loot_status' -and $_.objectCanonicalId -eq 'Available'
}).Count -eq 1) 'Loot availability was not persisted.'
Assert-True (@($monsterFacts | Where-Object {
    $_.predicateId -eq 'loot_item' -and $_.objectCanonicalId -eq 'OntologyDataFragment'
}).Count -eq 1) 'The monster-authored ontology data drop was not preserved.'

$deadAttackCommand = New-WorldCommand -Revision $deathRevision `
    -CommandType 'execute_action' -Payload $attack
$deadAttack = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" `
    -Body $deadAttackCommand -AccessToken $token
Assert-True ($deadAttack.rejectionCode -eq 'action_conditions_not_met') `
    'A defeated monster accepted another attack.'

Send-Command -WorldId $worldId -Token $token -Revision ([ref]$revision) `
    -Kind 'set_content_package' -Payload @{
        packageId = $packageId; packageVersion = $packageVersion; enabled = $false
    } | Out-Null
$disabledCommand = New-WorldCommand -Revision $revision `
    -CommandType 'execute_action' -Payload $attack
$disabled = Invoke-RejectedCommand `
    -Path "/v1/worlds/$worldId/commands" `
    -Body $disabledCommand -AccessToken $token
Assert-True ($disabled.rejectionCode -eq 'action_definition_not_enabled') `
    'Removing the package did not remove the matching combat behavior.'

Invoke-Json -Method Post -Path '/v1/auth/logout' -AccessToken $token | Out-Null
$login = Invoke-Json -Method Post -Path '/v1/auth/login' -Body @{
    email = $email; password = $password
}
$restored = Invoke-Json -Method Get -Path "/v1/worlds/$worldId" `
    -AccessToken ([string]$login.accessToken)
$restoredMonsterFacts = @($restored.facts | Where-Object {
    $_.subjectEntityId -eq $monsterId.ToString()
})
Assert-True (@($restoredMonsterFacts | Where-Object {
    $_.predicateId -eq 'loot_status' -and $_.objectCanonicalId -eq 'Available'
}).Count -eq 1) 'Combat state was not restored after a new login.'

Write-Host '[PASS] runtime Zone, session activation, motion convergence, legacy equipment migration, package-scoped definition reuse, equip, Rule Block remove/reapply, side-effect-free action preview, revision-free empty swing, Fact-owned cooldown, target damage, animation intent, bounded damage, death, drop, replay, disable, and resume' `
    -ForegroundColor Green
[pscustomobject]@{
    WorldId = $worldId
    AvatarId = $avatarId
    WeaponId = $weaponId
    MonsterId = $monsterId
    DeathRevision = $deathRevision
}
