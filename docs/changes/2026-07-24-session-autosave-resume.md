# Session Gate, Autosave, and Resume

## Summary

- **Date:** 2026-07-24
- **Owner:** Tormia team
- **Status:** Verified
- **Related request:** Complete the account-to-world loop and resume without a separate save action

## Intent

Keep world runtime presentation disabled before authentication and confirmed
entry, then restore the selected character's durable state and automatically
save coarse progress during play.

## Data classification

- [x] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

`OntologyGameSessionCoordinator` owns the client presentation lifecycle. World
Authority owns durable avatar checkpoints and world revisions. Account
appearance stays in the account character profile. Per-frame input, motion,
presence, observations, and inference remain ephemeral.

The Unity gate must not infer authentication from scene object presence, create
durable facts, or provide a hidden local substitute for an Authority failure.

## Ontology expression

This change adds no mesh, prefab, or localized-name gameplay conditions.
Checkpoint data is transport-owned avatar state, not an authored ontology Fact.
Quest/action/inventory progress is durable only through an explicit
account-profile or revisioned Authority contract.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Confirmed entry enables runtime and checkpoint save/load resumes transform | `OntologyGameSessionCoordinatorTests`; live Authority checkpoint HTTP smoke |
| Disabled / removed | Signed-out and failed-entry states hide player, HUD, toggles, input, and editors | `TormiaMainSceneSmokeTests`; `AccountEntryRuntimeGate_v2.png` |
| Regression / edge case | Local paths are scoped; revision conflicts reload and retry; transport commands retain IDs | Unity EditMode/PlayMode suites |
| Repeatable integration | Registration through logout/login resume remains executable | `scripts/run-account-world-loop-smoke.ps1` |

## Performance and multiplayer impact

Checkpoint writes are bounded to a 30-second interval after meaningful movement
plus pause/logout. Motion remains Redis/runtime traffic. Pending commands replay
only after entry and are scoped by account/world. Migration
`010_avatar_checkpoints.sql` stores one checkpoint row per world/avatar.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Localization CSV / migration, if needed
- [x] Test scenario manifest, if needed
