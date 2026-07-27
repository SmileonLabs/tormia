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
        $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress
    }
    return Invoke-RestMethod @parameters
}

function Assert-HttpStatus(
    [int]$ExpectedStatus,
    [string]$Method,
    [string]$Path,
    [object]$Body = $null,
    [string]$AccessToken = $null
) {
    try {
        Invoke-Json -Method $Method -Path $Path -Body $Body -AccessToken $AccessToken | Out-Null
        throw "Expected HTTP $ExpectedStatus for $Method $Path, but the request succeeded."
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        $actualStatus = [int]$response.StatusCode
        if ($actualStatus -ne $ExpectedStatus) {
            throw "Expected HTTP $ExpectedStatus for $Method $Path, received $actualStatus."
        }
    }
}

function New-WorldCommand(
    [long]$ExpectedRevision,
    [string]$CommandType,
    [object]$Payload,
    [guid]$CommandId = [guid]::NewGuid()
) {
    return @{
        contractVersion = 1
        commandId = $CommandId
        expectedRevision = $ExpectedRevision
        commandType = $CommandType
        payload = $Payload
    }
}

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 12)
$email = "loop-$suffix@tormia.local"
$password = "Tormia-$suffix-A9!"
$displayName = "Loop $suffix"
$slug = "loop-$suffix"
$avatarId = [guid]::NewGuid()

Write-Host 'TOV account/world resume smoke' -ForegroundColor Cyan

$health = Invoke-Json -Method Get -Path '/health'
Assert-True ($health.status -eq 'healthy') 'World Authority is not healthy.'

$registration = Invoke-Json -Method Post -Path '/v1/auth/register' -Body @{
    email = $email
    displayName = $displayName
    password = $password
}
$token = [string]$registration.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Registration did not return an access token.'

$character = Invoke-Json -Method Post -Path '/v1/account/characters' -AccessToken $token -Body @{
    displayName = 'Loop Character'
    templateId = 'PlayerAvatar'
    equippedPartIds = @('Hair_Default')
}
$characterId = [guid]$character.characterId
Assert-True ($character.profileRevision -ge 1) 'Character profile revision was not initialized.'

$profileCommandId = [guid]::NewGuid()
$profileUpdate = @{
    commandId = $profileCommandId
    expectedRevision = [long]$character.profileRevision
    displayName = 'Loop Character'
    templateId = 'PlayerAvatar'
    equippedPartIds = @('Hair_Default', 'Tops_Default')
    profileRelations = @()
}
$profileResult = Invoke-Json -Method Put -Path "/v1/account/characters/$characterId" -AccessToken $token -Body $profileUpdate
Assert-True ($profileResult.accepted -and -not $profileResult.isReplay) 'Appearance profile update was not accepted.'
$profileReplay = Invoke-Json -Method Put -Path "/v1/account/characters/$characterId" -AccessToken $token -Body $profileUpdate
Assert-True ($profileReplay.accepted -and $profileReplay.isReplay) 'Appearance profile command was not idempotent.'

$world = Invoke-Json -Method Post -Path '/v1/worlds' -AccessToken $token -Body @{
    slug = $slug
    title = "Loop World $suffix"
    visibility = 'private'
}
$worldId = [guid]$world.worldId
$projection = Invoke-Json -Method Get -Path "/v1/worlds/$worldId" -AccessToken $token
$revision = [long]$projection.revision

$entryBody = @{ characterId = $characterId; avatarEntityId = $avatarId }
Assert-HttpStatus -ExpectedStatus 404 -Method Post -Path "/v1/worlds/$worldId/entry" -Body $entryBody -AccessToken $token

$spawnTransform = @{
    positionX = 0.0; positionY = 1.0; positionZ = 0.0
    rotationX = 0.0; rotationY = 0.0; rotationZ = 0.0
    scaleX = 1.0; scaleY = 1.0; scaleZ = 1.0
}
$placeCommandId = [guid]::NewGuid()
$placeCommand = New-WorldCommand -ExpectedRevision $revision -CommandType 'place_entity' -CommandId $placeCommandId -Payload @{
    entityId = $avatarId
    templateId = 'PlayerAvatar'
    templateVersion = 1
    displayName = 'Loop Avatar'
    zoneKey = $null
    transform = $spawnTransform
}
$placed = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/commands" -AccessToken $token -Body $placeCommand
Assert-True ($placed.accepted -and -not $placed.isReplay) 'Avatar placement was not accepted.'
$placeReplay = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/commands" -AccessToken $token -Body $placeCommand
Assert-True ($placeReplay.accepted -and $placeReplay.isReplay) 'World command did not replay idempotently.'
$revision = [long]$placed.revision

$registerCommand = New-WorldCommand -ExpectedRevision $revision -CommandType 'register_player_avatar' -Payload @{
    entityId = $avatarId
}
$registered = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/commands" -AccessToken $token -Body $registerCommand
Assert-True $registered.accepted 'Avatar registration was not accepted.'
$revision = [long]$registered.revision

$entry = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/entry" -AccessToken $token -Body $entryBody
Assert-True $entry.accepted 'World entry was not accepted after avatar registration.'

$checkpointTransform = @{
    positionX = 11.5; positionY = 4.25; positionZ = -7.75
    rotationX = 0.0; rotationY = 135.0; rotationZ = 0.0
    scaleX = 1.0; scaleY = 1.0; scaleZ = 1.0
}
$checkpointCommand = New-WorldCommand -ExpectedRevision $revision -CommandType 'save_avatar_checkpoint' -Payload @{
    avatarEntityId = $avatarId
    zoneKey = $null
    transform = $checkpointTransform
}
$checkpointSaved = Invoke-Json -Method Post -Path "/v1/worlds/$worldId/commands" -AccessToken $token -Body $checkpointCommand
Assert-True $checkpointSaved.accepted 'Avatar checkpoint was not accepted.'

$checkpoint = Invoke-Json -Method Get -Path "/v1/worlds/$worldId/avatars/$avatarId/checkpoint" -AccessToken $token
Assert-True ($checkpoint.accepted -and [double]$checkpoint.transform.positionX -eq 11.5) 'Checkpoint did not restore the saved position.'

$projected = Invoke-Json -Method Get -Path "/v1/worlds/$worldId" -AccessToken $token
$projectedAvatar = @($projected.entities | Where-Object { $_.entityId -eq $avatarId.ToString() })[0]
Assert-True ($null -ne $projectedAvatar -and [double]$projectedAvatar.transform.positionZ -eq -7.75) 'Durable world projection did not receive the checkpoint transform.'

Invoke-Json -Method Post -Path '/v1/auth/logout' -AccessToken $token | Out-Null
Assert-HttpStatus -ExpectedStatus 401 -Method Get -Path '/v1/account' -AccessToken $token

$login = Invoke-Json -Method Post -Path '/v1/auth/login' -Body @{ email = $email; password = $password }
$resumedToken = [string]$login.accessToken
$dashboard = Invoke-Json -Method Get -Path '/v1/account' -AccessToken $resumedToken
Assert-True (@($dashboard.characters | Where-Object { $_.characterId -eq $characterId.ToString() }).Count -eq 1) 'Character was not restored after login.'
Assert-True (@($dashboard.worlds | Where-Object { $_.worldId -eq $worldId.ToString() }).Count -eq 1) 'World membership was not restored after login.'
$resumedCheckpoint = Invoke-Json -Method Get -Path "/v1/worlds/$worldId/avatars/$avatarId/checkpoint" -AccessToken $resumedToken
Assert-True ($resumedCheckpoint.accepted -and [double]$resumedCheckpoint.transform.rotationY -eq 135.0) 'Checkpoint was not restored with a new session.'

Invoke-Json -Method Post -Path '/v1/auth/logout' -AccessToken $resumedToken | Out-Null

Write-Host '[PASS] registration, profile, world entry, autosave, logout, login, and resume' -ForegroundColor Green
[pscustomobject]@{
    AccountId = [guid]$registration.userId
    CharacterId = $characterId
    WorldId = $worldId
    AvatarId = $avatarId
    CheckpointRevision = [long]$resumedCheckpoint.revision
}
