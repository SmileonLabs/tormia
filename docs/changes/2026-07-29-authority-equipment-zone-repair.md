# Authority equipment and zone repair

## Summary

- **Date:** 2026-07-29
- **State:** Implemented and verified
- **Scope:** Equipment-state cleanup, legacy Zone assignment, durable command transport

## Decision

`equipped_by` remains the item-owned source of current equipment truth. World
entry requests an idempotent Authority repair when an action-produced
`attacks_with` relation has no matching `equipped_by` relation. Repair retracts
the stale history and never re-equips an item from it.

Legacy entities without a Zone are assigned only when their durable X/Z
position belongs to exactly one authored Zone. Ambiguous or out-of-bounds
entities remain unassigned.

Unity serializes durable commands per Authority client. A server-reported stale
revision is retried once with a new command ID. This is transport coordination,
not gameplay authorization; Triple data and assigned Rule Blocks still govern
equipment and attack behavior.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Orphan equipment history | Stale `attacks_with` is detected and retracted | `OrphanedAttackToolRelationRequiresEquipmentRepair` |
| Valid equipment | Matching `equipped_by` does not trigger repair | `OrphanedAttackToolRelationRequiresEquipmentRepair` |
| Unique Zone | Legacy entity is eligible for Authority Zone migration | `LegacyUnzonedEntityIsMigratedOnlyForOneMatchingZone` |
| Ambiguous Zone | Entity is not assigned by guesswork | `LegacyUnzonedEntityIsMigratedOnlyForOneMatchingZone` |

