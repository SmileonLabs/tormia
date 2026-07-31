# Rule-Block-owned player combat respawn

## Summary

- Date: 2026-07-29
- Area: durable avatar ontology, World Authority action evaluation, Unity checkpoint presentation
- Status: implemented and verified

## Intent

Recover a dead player through the same reusable Triple -> Rule Block -> World
Authority -> Physical Meaning/presentation contract used by other gameplay,
without a Unity-owned revive fallback.

## Data classification

- Durable world data: `respawn_action`, `current_health`, `maximum_health`,
  `is_alive`, assigned Rule Block, semantic contract marker
- Ephemeral runtime state: input request, grounding reset, motion reconciliation
- Unity presentation: applying an already-confirmed checkpoint pose after the
  accepted Authority transition

## Complete path

The player avatar authors `respawn_action -> respawn_avatar` and has the
`RespawnPlayerOnDeath` Rule Block. The immutable action invokes that block.
Authority requires a dead, damageable, player-controlled actor, restores
`current_health` from `maximum_health`, and sets `is_alive=true`. Unity reloads
the accepted projection and only then presents the confirmed checkpoint pose.

## Removed path

Removing the Rule Block binding makes Authority reject the action. Unity does
not restore health, life state, or position. Semantic contract version 1 is a
one-time introduction marker, so normal refresh does not silently restore the
removed behavior.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Dead avatar returns with maximum health and an alive projection, then moves to the confirmed checkpoint | Server evaluator tests, Unity EditMode contract tests, live MCP projection |
| Removed | Authority rejects the action and Unity performs no checkpoint fallback | Server removed-block test and Unity binding test |
| Live | Existing dead avatar recovered to `current_health=100`, `is_alive=true` with `RespawnPlayerOnDeath` assigned | Unity MCP live probe |

The development action package is `social_village@3.2.1`.
The complete Unity EditMode regression suite passed 206/206. The World
Authority test project exited successfully, and
`verify-development.ps1 -RequireServices -RequireUnityMcp` passed the bilingual
context, regression manifest, server build, Docker services, Authority health,
and persistent Unity MCP checks.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
