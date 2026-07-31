[CmdletBinding()]
param(
    [string]$BaseUri = 'http://127.0.0.1:5272',
    [int]$TimeoutSec = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Json(
    [string]$Method,
    [string]$Path,
    [object]$Body = $null,
    [string]$Token = $null
) {
    $parameters = @{
        Uri = "$($BaseUri.TrimEnd('/'))$Path"
        Method = $Method
        TimeoutSec = $TimeoutSec
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $parameters.Headers = @{ Authorization = "Bearer $Token" }
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    Invoke-RestMethod @parameters
}

function Send-Command(
    [guid]$WorldId,
    [string]$Token,
    [ref]$Revision,
    [string]$Kind,
    [object]$Payload
) {
    $result = Invoke-Json -Method Post `
        -Path "/v1/worlds/$WorldId/commands" -Token $Token -Body @{
            contractVersion = 1
            commandId = [guid]::NewGuid()
            expectedRevision = $Revision.Value
            commandType = $Kind
            payload = $Payload
        }
    Assert-True $result.accepted `
        "Command '$Kind' was rejected: $($result.rejectionCode)"
    $Revision.Value = [long]$result.revision
}

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 12)
$ruleId = "FloatAnyObject_$suffix"
$siblingRuleId = "FloatAnyObjectSibling_$suffix"
$packageId = "meaning_$suffix"
$targetId = [guid]::NewGuid()
$applicationId = [guid]::NewGuid()
$bindingId = [guid]::NewGuid()

$registration = Invoke-Json -Method Post -Path '/v1/auth/register' -Body @{
    email = "meaning-$suffix@tormia.local"
    displayName = "Meaning $suffix"
    password = "Tormia-$suffix-A9!"
}
$token = [string]$registration.accessToken
$world = Invoke-Json -Method Post -Path '/v1/worlds' -Token $token -Body @{
    slug = "meaning-$suffix"
    title = "Meaning World $suffix"
    visibility = 'private'
}
$worldId = [guid]$world.worldId
$projection = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$revision = [long]$projection.revision

$definition = @{
    id = $ruleId
    catalogVersion = 1
    description = 'Any authored object with this package may float.'
    conditions = @(
        @{
            kind = 2
            subject = '?object'
            predicate = ''
            obj = 'FloatableObject'
        }
    )
    effects = @(
        @{
            kind = 0
            subject = '?object'
            predicate = 'physical_state'
            obj = 'Floating'
        }
    )
}
$definitionV2 = $definition.Clone()
$definitionV2.catalogVersion = 2
$siblingDefinition = $definition.Clone()
$siblingDefinition.id = $siblingRuleId
$published = Invoke-Json -Method Post `
    -Path "/v1/content/packages/$packageId/rules" `
    -Token $token -Body @{
        packageVersion = '1.0.0'
        rules = @(
            @{
                ruleId = $ruleId
                definitionVersion = 1
                payloadJson = $definition |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $ruleId
                definitionVersion = 2
                payloadJson = $definitionV2 |
                    ConvertTo-Json -Depth 20 -Compress
            }
            @{
                ruleId = $siblingRuleId
                definitionVersion = 1
                payloadJson = $siblingDefinition |
                    ConvertTo-Json -Depth 20 -Compress
            }
        )
    }
Assert-True $published.accepted 'Meaning-package Rule Block was not published.'

Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'place_entity' -Payload @{
        entityId = $targetId
        templateId = 'ArbitraryVisualObject'
        templateVersion = 1
        displayName = 'Arbitrary object'
        zoneKey = $null
        transform = @{
            positionX = 0.0; positionY = 1.0; positionZ = 0.0
            rotationX = 0.0; rotationY = 0.0; rotationZ = 0.0
            scaleX = 1.0; scaleY = 1.0; scaleZ = 1.0
        }
        initialFacts = @(
            @{
                predicateId = 'has_concept'
                objectKind = 'canonical'
                objectEntityId = $null
                objectCanonicalId = 'Rock'
                objectValueJson = $null
            }
            @{
                predicateId = 'physical_profile'
                objectKind = 'canonical'
                objectEntityId = $null
                objectCanonicalId = 'HeavySinking'
                objectValueJson = $null
            }
            @{
                predicateId = 'maximum_health'
                objectKind = 'number'
                objectEntityId = $null
                objectCanonicalId = $null
                objectValueJson = '25'
            }
        )
    }

$meaningPayload = @{
    operation = 'apply'
    applicationId = $applicationId
    targetEntityId = $targetId
    slotId = 'primary_physical_meaning'
    packageId = 'rule_preset_water_buoyancy'
    replacePredicateIds = @('physical_profile', 'physical_state')
    requiredConceptIds = @('FloatableObject')
    authoredFacts = @(
        @{
            predicateId = 'physical_profile'
            objectKind = 'canonical'
            objectEntityId = $null
            objectCanonicalId = 'LightBuoyant'
            objectValueJson = $null
        }
    )
    ruleBlocks = @(
        @{
            bindingId = $bindingId
            ruleId = $ruleId
            ruleVersion = 1
            parameterValuesJson = '{"bindingVariable":"?object"}'
        }
    )
}
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' `
    -Payload $meaningPayload

# A deterministic template/package request may be retried after a lost response.
# Reapplying the same immutable application identity must converge without
# replacing its owned rows or producing an application identity conflict.
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' `
    -Payload $meaningPayload

$applied = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$targetFacts = @($applied.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString()
})
Assert-True ($targetFacts.objectCanonicalId -contains 'LightBuoyant') `
    'Applied projection did not contain LightBuoyant.'
Assert-True ($targetFacts.objectCanonicalId -contains 'FloatableObject') `
    'Applied projection did not contain FloatableObject.'
Assert-True (-not ($targetFacts.objectCanonicalId -contains 'HeavySinking')) `
    'Replaced HeavySinking remained active.'
Assert-True (@($applied.ruleBindings | Where-Object {
    $_.targetEntityId -eq $targetId.ToString() -and $_.ruleId -eq $ruleId
}).Count -eq 1) 'Applied projection did not contain the Rule Block.'

Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' -Payload @{
        operation = 'remove'
        applicationId = $applicationId
        targetEntityId = $targetId
        slotId = 'primary_physical_meaning'
        packageId = ''
        replacePredicateIds = @()
        requiredConceptIds = @()
        authoredFacts = @()
        ruleBlocks = @()
    }

$removed = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$targetFacts = @($removed.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString()
})
Assert-True ($targetFacts.objectCanonicalId -contains 'HeavySinking') `
    'Removing the package did not restore the displaced baseline.'
Assert-True (-not ($targetFacts.objectCanonicalId -contains 'LightBuoyant')) `
    'Removing the package left its physical profile active.'
Assert-True (-not ($targetFacts.objectCanonicalId -contains 'FloatableObject')) `
    'Removing the package left its contributed concept active.'
Assert-True (@($removed.ruleBindings | Where-Object {
    $_.targetEntityId -eq $targetId.ToString() -and $_.ruleId -eq $ruleId
}).Count -eq 0) 'Removing the package left its Rule Block active.'

# Applying again and deleting the contributed Rule Block must take the same
# package-owned removal path; it may not leave hidden triples or physics behind.
$applicationId = [guid]::NewGuid()
$bindingId = [guid]::NewGuid()
$meaningPayload.applicationId = $applicationId
$meaningPayload.ruleBlocks[0].bindingId = $bindingId
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' `
    -Payload $meaningPayload
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'remove_rule_block' `
    -Payload @{ bindingId = $bindingId }
# A repeated removal of an already completed binding remains idempotent.
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'remove_rule_block' `
    -Payload @{ bindingId = $bindingId }
$ruleRemoved = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$targetFacts = @($ruleRemoved.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString()
})
Assert-True ($targetFacts.objectCanonicalId -contains 'HeavySinking') `
    'Rule Block removal did not restore the package baseline.'
Assert-True (-not ($targetFacts.objectCanonicalId -contains 'LightBuoyant')) `
    'Rule Block removal left package-authored physical meaning active.'

# Preconfigured catalog content historically arrived as independent initial
# Facts and Rule Blocks. A baseline package must adopt typed and canonical
# Facts, migrate an older immutable Rule Definition version, remove one binding
# independently, and clean up its shared meaning when its final binding leaves.
$baselineApplicationId = [guid]::NewGuid()
$baselineBindingId = [guid]::NewGuid()
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'add_rule_block' -Payload @{
        bindingId = $baselineBindingId
        targetEntityId = $targetId
        ruleId = $ruleId
        ruleVersion = 1
        parameterValuesJson = '{"bindingVariable":"?object"}'
    }
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' -Payload @{
        operation = 'apply'
        applicationId = $baselineApplicationId
        targetEntityId = $targetId
        slotId = 'template_semantic_baseline'
        packageId = 'arbitrary_template_semantic_baseline_v2'
        adoptExistingContributions = $true
        replacePredicateIds = @()
        requiredConceptIds = @('Rock')
        authoredFacts = @(
            @{
                predicateId = 'physical_profile'
                objectKind = 'canonical'
                objectEntityId = $null
                objectCanonicalId = 'HeavySinking'
                objectValueJson = $null
            }
            @{
                predicateId = 'maximum_health'
                objectKind = 'number'
                objectEntityId = $null
                objectCanonicalId = $null
                objectValueJson = '25'
            }
        )
        ruleBlocks = @(
            @{
                bindingId = $baselineBindingId
                ruleId = $ruleId
                ruleVersion = 2
                parameterValuesJson = '{"bindingVariable":"?object"}'
            }
            @{
                bindingId = [guid]::NewGuid()
                ruleId = $siblingRuleId
                ruleVersion = 1
                parameterValuesJson = '{"bindingVariable":"?object"}'
            }
        )
    }
$baselineApplied = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$migratedBindings = @($baselineApplied.ruleBindings | Where-Object {
    $_.targetEntityId -eq $targetId.ToString() -and
    $_.ruleId -eq $ruleId
})
Assert-True ($migratedBindings.Count -eq 1) `
    'Legacy Rule Block migration left duplicate active versions.'
Assert-True ($migratedBindings[0].ruleVersion -eq 2) `
    'Legacy Rule Block was not migrated to the requested version.'
$migratedBindingId = [guid]$migratedBindings[0].bindingId
Assert-True ($migratedBindingId -ne $baselineBindingId) `
    'Legacy immutable Rule Block identity was incorrectly reused.'
$siblingBindings = @($baselineApplied.ruleBindings | Where-Object {
    $_.targetEntityId -eq $targetId.ToString() -and
    $_.ruleId -eq $siblingRuleId
})
Assert-True ($siblingBindings.Count -eq 1) `
    'Baseline sibling Rule Block was not applied.'
$siblingBindingId = [guid]$siblingBindings[0].bindingId
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'remove_rule_block' `
    -Payload @{ bindingId = $migratedBindingId }
$baselineRemoved = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$targetFacts = @($baselineRemoved.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString()
})
Assert-True (@($targetFacts | Where-Object {
    $_.objectCanonicalId -eq 'Rock'
}).Count -eq 1) `
    'Removing one Rule Block incorrectly removed the shared catalog concept.'
Assert-True (@($targetFacts | Where-Object {
    $_.objectCanonicalId -eq 'HeavySinking'
}).Count -eq 1) `
    'Removing one Rule Block incorrectly removed shared physical meaning.'
Assert-True (@($targetFacts | Where-Object {
    $_.predicateId -eq 'maximum_health' -and $_.objectKind -eq 'number'
}).Count -eq 1) `
    'Removing one Rule Block incorrectly removed a shared typed Triple.'
Assert-True (@($baselineRemoved.ruleBindings | Where-Object {
    $_.bindingId -eq $migratedBindingId.ToString()
}).Count -eq 0) 'Removing an adopted baseline left its Rule Block active.'
Assert-True (@($baselineRemoved.ruleBindings | Where-Object {
    $_.bindingId -eq $siblingBindingId.ToString()
}).Count -eq 1) 'Removing one Rule Block removed its package sibling.'

Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'remove_rule_block' `
    -Payload @{ bindingId = $siblingBindingId }
$baselineCompleted = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$targetFacts = @($baselineCompleted.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString()
})
Assert-True (@($targetFacts | Where-Object {
    $_.objectCanonicalId -eq 'Rock' -or
    $_.objectCanonicalId -eq 'HeavySinking' -or
    $_.predicateId -eq 'maximum_health'
}).Count -eq 0) `
    'Removing the final Rule Block left package-owned Triples active.'
Assert-True (@($baselineCompleted.ruleBindings | Where-Object {
    $_.bindingId -eq $siblingBindingId.ToString()
}).Count -eq 0) 'Removing the final Rule Block left the binding active.'

Write-Host `
    'Meaning package partial/final removal, typed ownership, adoption, and migration smoke passed.' `
    -ForegroundColor Green
