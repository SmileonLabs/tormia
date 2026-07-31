# Authority runtime foundation provisioning

## Summary

- **Date:** 2026-07-28
- **State:** Implemented and integration-tested
- **Scope:** World creation, avatar entry, ephemeral motion, equipment actions,
  legacy relation migration

## Intent

Prevent a world from appearing playable while proximity-constrained Authority
actions cannot resolve the player's server position.

## Data ownership

- Zone definitions, avatar checkpoint Zone assignment, `movement_speed`, and
  equipment relations remain durable World Authority data.
- Player motion remains an expiring runtime state in Redis.
- Unity sends commands and presents results; it does not own final distance
  validation.

## Decision and boundaries

An editable Zone-less world is provisioned with the project-configured starter
Zone by `define_zone`. A registered avatar receives `movement_speed` only when
missing and is assigned by `save_avatar_checkpoint`. Entry waits for the
ephemeral motion projection.

Existing checkpoint transforms are preserved. A valid saved Zone wins; when
the saved Zone is missing or dormant, the replacement is selected from the
saved position and only the Zone assignment is repaired.

Equip candidate discovery reads the canonical `has_concept -> Weapon` row from
the current Authority projection when the Unity `OntologyObject` presentation
has not finished refreshing. It does not infer weapon meaning from a prefab or
template name.

Combat buttons subscribe to the serialized Input System actions' `performed`
events while enabled. Equip and attack no longer depend on a single
`WasPressedThisFrame` poll lining up with the controller's `Update` frame.

`migrate_legacy_equipment_relations` retracts only active, action-produced
`equips` facts. It does not convert them into a current equipment relation
because historical truth cannot prove that an item remains equipped.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Enabled | Zone, avatar motion, canonical equip, attack and animation intent complete in order | `scripts/run-combat-authority-smoke.ps1` |
| Removed | Missing runtime position prevents proximity equip submission | `EquipCommandRequiresAuthorityRuntimePosition` |
| Legacy | Action-produced `equips` is absent after migration | combat Authority smoke |
| Resume | Runtime repair preserves the checkpoint transform and valid Zone | `RuntimeZoneResolutionPreservesCheckpointZoneAndPosition` |
| Presentation refresh | A projected canonical Weapon remains selectable while its Unity semantic component refreshes | `EquipCandidateUsesCanonicalAuthorityConceptDuringPresentationRefresh` |

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
