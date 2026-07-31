# Authority entity retirement pipeline

## Summary

- Date: 2026-07-29
- Status: Implemented
- Scope: Durable world data, authority transport, Unity presentation

## Intent

Deleting a Unity presentation must not pretend that a durable world entity was
deleted. Defeat and retirement are separate lifecycle states.

## Decision and boundary

- Add the revisioned `retire_entity` World Authority command.
- Soft-delete the entity and close active Triples, Rule Blocks, and meaning
  package applications that describe or reference it in the same revision.
- Preserve historical rows plus command/event evidence.
- Reject generic retirement of account-owned player avatars.
- Remove autonomous ephemeral state after the durable transaction commits.
- Let Unity remove the presentation only after the accepted projection omits
  the entity. Keep local deletion only for previews not owned by Authority.
- Do not infer retirement from `is_alive=false`, a template, prefab, mesh, or
  name. A future automatic cleanup policy must be an explicit lifecycle Rule
  Block/action.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Authority retirement | Entity and active semantic contributions leave the projection | `scripts/run-entity-retirement-authority-smoke.ps1` |
| Idempotent replay | Replaying the accepted command ID returns its original result | Authority smoke |
| Removed/already retired | A new command cannot retire the absent entity | Authority smoke |
| Unity presentation | Claimed requests wait for projection removal | `DeleteSelectionUsesAuthorityRetirementWhenClaimed` |
| Local preview | Unclaimed deletion still removes only the local preview | `DeleteSelectionFallsBackOnlyWhenNoAuthorityHandlerClaimsIt` |

## Performance and multiplayer

Retirement is a single coarse authoring transaction, never a per-frame event.
The world lock, expected revision, command ID, and Authority projection keep all
clients on one durable result. Redis/in-memory autonomous motion for the
retired entity is removed only after the database commit.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
