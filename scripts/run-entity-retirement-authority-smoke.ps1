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

function New-Transform([double]$X) {
    @{
        positionX = $X; positionY = 1.0; positionZ = 0.0
        rotationX = 0.0; rotationY = 0.0; rotationZ = 0.0
        scaleX = 1.0; scaleY = 1.0; scaleZ = 1.0
    }
}

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 12)
$targetId = [guid]::NewGuid()
$observerId = [guid]::NewGuid()
$bindingId = [guid]::NewGuid()
$applicationId = [guid]::NewGuid()
$ruleId = "RetirementEvidence_$suffix"

$registration = Invoke-Json -Method Post -Path '/v1/auth/register' -Body @{
    email = "retirement-$suffix@tormia.local"
    displayName = "Retirement $suffix"
    password = "Tormia-$suffix-A9!"
}
$token = [string]$registration.accessToken
$world = Invoke-Json -Method Post -Path '/v1/worlds' -Token $token -Body @{
    slug = "retirement-$suffix"
    title = "Retirement World $suffix"
    visibility = 'private'
}
$worldId = [guid]$world.worldId
$projection = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
$revision = [long]$projection.revision

$published = Invoke-Json -Method Post `
    -Path "/v1/content/packages/retirement-$suffix/rules" `
    -Token $token -Body @{
        packageVersion = '1.0.0'
        rules = @(
            @{
                ruleId = $ruleId
                definitionVersion = 1
                payloadJson = @{
                    id = $ruleId
                    catalogVersion = 1
                    description = 'Retirement smoke evidence only.'
                    conditions = @(
                        @{
                            kind = 2
                            subject = '?target'
                            predicate = ''
                            obj = 'ArbitraryEntity'
                        }
                    )
                    effects = @(
                        @{
                            kind = 0
                            subject = '?target'
                            predicate = 'retirement_smoke_result'
                            obj = 'Enabled'
                        }
                    )
                } | ConvertTo-Json -Depth 10 -Compress
            }
        )
    }
Assert-True $published.accepted 'Retirement evidence Rule Block was not published.'

Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'place_entity' -Payload @{
        entityId = $targetId
        templateId = 'ArbitraryTarget'
        templateVersion = 1
        displayName = 'Retirement target'
        zoneKey = $null
        transform = New-Transform 0.0
        initialFacts = @(
            @{
                predicateId = 'has_concept'
                objectKind = 'canonical'
                objectEntityId = $null
                objectCanonicalId = 'ArbitraryEntity'
                objectValueJson = $null
            }
        )
    }
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'place_entity' -Payload @{
        entityId = $observerId
        templateId = 'ArbitraryObserver'
        templateVersion = 1
        displayName = 'Retirement observer'
        zoneKey = $null
        transform = New-Transform 2.0
        initialFacts = @(
            @{
                predicateId = 'tracks'
                objectKind = 'entity'
                objectEntityId = $targetId
                objectCanonicalId = $null
                objectValueJson = $null
            }
        )
    }
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'add_rule_block' -Payload @{
        bindingId = $bindingId
        targetEntityId = $targetId
        ruleId = $ruleId
        ruleVersion = 1
        parameterValuesJson = '{}'
    }
Send-Command -WorldId $worldId -Token $token `
    -Revision ([ref]$revision) -Kind 'apply_meaning_package' -Payload @{
        operation = 'apply'
        applicationId = $applicationId
        targetEntityId = $targetId
        slotId = 'retirement_smoke'
        packageId = 'retirement_smoke'
        replacePredicateIds = @()
        requiredConceptIds = @('RetirementMeaning')
        authoredFacts = @()
        ruleBlocks = @()
    }

$retireCommandId = [guid]::NewGuid()
$retirement = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/commands" -Token $token -Body @{
        contractVersion = 1
        commandId = $retireCommandId
        expectedRevision = $revision
        commandType = 'retire_entity'
        payload = @{ entityId = $targetId }
    }
Assert-True $retirement.accepted `
    "Retirement was rejected: $($retirement.rejectionCode)"
$revision = [long]$retirement.revision

$retired = Invoke-Json -Method Get `
    -Path "/v1/worlds/$worldId" -Token $token
Assert-True (@($retired.entities | Where-Object {
    $_.entityId -eq $targetId.ToString()
}).Count -eq 0) 'Retired entity remained in the projection.'
Assert-True (@($retired.facts | Where-Object {
    $_.subjectEntityId -eq $targetId.ToString() -or
    $_.objectEntityId -eq $targetId.ToString()
}).Count -eq 0) 'A retired entity left an active Triple in the projection.'
Assert-True (@($retired.ruleBindings | Where-Object {
    $_.targetEntityId -eq $targetId.ToString()
}).Count -eq 0) 'A retired entity left an active Rule Block in the projection.'

$replay = Invoke-Json -Method Post `
    -Path "/v1/worlds/$worldId/commands" -Token $token -Body @{
        contractVersion = 1
        commandId = $retireCommandId
        expectedRevision = $revision - 1
        commandType = 'retire_entity'
        payload = @{ entityId = $targetId }
    }
Assert-True ($replay.accepted -and $replay.isReplay) `
    'The original retirement command was not replayed idempotently.'

try {
    Invoke-Json -Method Post `
        -Path "/v1/worlds/$worldId/commands" -Token $token -Body @{
            contractVersion = 1
            commandId = [guid]::NewGuid()
            expectedRevision = $revision
            commandType = 'retire_entity'
            payload = @{ entityId = $targetId }
        } | Out-Null
    throw 'A second retirement with a new command ID was unexpectedly accepted.'
}
catch {
    $response = ''
    $httpResponse = $_.Exception.Response
    if ($null -ne $httpResponse) {
        $reader = New-Object System.IO.StreamReader(
            $httpResponse.GetResponseStream())
        try {
            $response = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    Assert-True ($response -like '*entity_not_found*') `
        "Second retirement did not reject as entity_not_found: $response"
}

Write-Host `
    'Entity retirement projection/replay/disabled-path smoke passed.' `
    -ForegroundColor Green
