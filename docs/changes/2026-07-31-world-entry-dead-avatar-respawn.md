# Dead Avatar World-Entry Respawn

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV team
- **Status:** Implemented
- **Related issue or request:** A saved dead avatar could not enter its world.

## Intent

Remove the circular dependency where a dead avatar needed an accepted
locomotion contract to enter the world, while automatic respawn was allowed
only after the session had already entered the world.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

After the world projection is loaded, entry resolves the avatar's authored
life state before requesting locomotion approval. If the avatar is dead, Unity
submits the canonical `respawn_action`; the assigned
`RespawnPlayerOnDeath` Rule Block restores health and life through World
Authority. Entry continues only after reloading and verifying `is_alive=true`.
Unity does not locally revive the avatar or synthesize health.

Legacy semantic repair commands encountered during the same entry transaction
use the client's revision-retry transport. An autonomous world revision can no
longer make a valid meaning-package migration abort entry after one stale
revision rejection.

When death presentation disables the player `CharacterController`, continuous
gravity presentation also stops at that component boundary. This prevents
Unity from issuing invalid `Move` calls while the durable dead state remains
owned by Authority.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | A dead avatar is revived through its enabled Authority respawn contract before locomotion preparation. | `DeadAvatarRequiresAuthorityRespawnBeforeWorldEntry`; `WorldEntryRestoresLifeBeforeLocomotionHandshake` |
| Disabled / removed | Missing, conflicting, or disabled respawn semantics fail world entry without a hidden Unity fallback. | `ConflictingAliveProjectionDoesNotTriggerRespawn`; `RemovingPlayerRespawnRuleBlockRemovesRespawnBehavior` |
| Concurrent revision | Entry semantic repair retries one Authority-reported stale revision with a new command envelope. | `EntrySemanticRepairUsesRevisionRetryTransport` |

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
