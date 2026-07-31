# Authority Combat Data Ownership and Atomic Placement

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development team
- **Status:** Verified
- **Related request:** Align player-to-monster combat with ontology policy

## Intent

Make the first combat loop reusable for future weapons, monsters, and NPCs.
Content authors should change template triples and published action data instead
of adding mesh-, prefab-, object-, or monster-specific gameplay branches.

## Data classification

- [x] Durable authored world data
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

World Authority owns entity existence, typed initial Facts, action validation,
damage, death, and loot availability. Weapon templates own damage values.
Monster templates own vitality, hostility, faction, and loot identity. Unity
owns targeting assistance and accepted-result presentation only.

The Unity combat catalog must not duplicate maximum health or drop identity.
The attack action must not contain a fixed damage value or a monster drop ID.
A configured Authority scene must not temporarily restore a local snapshot
while authentication is incomplete.

## Ontology expression

- Weapon: `has_concept Sword`, `grants_capability MeleeAttack`,
  `damage_profile BasicSwordDamage`, `attack_damage 10`
- Monster: `has_concept Damageable`,
  `vitality_profile BeholderBasicVitality`,
  `combat_disposition Hostile`, `belongs_to_faction WildMonster`,
  `loot_table BeholderBasicLoot`,
  `loot_item OntologyDataFragment`
- Attack effect: adjust target `current_health` from tool
  `attack_damage * -1`, minimum `0`
- Death transition: `is_alive=False`, `loot_status=Available`
- Presentation: accepted `AttackLight`, then projected hit/death/VFX

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Typed entity Facts are placed atomically and weapon damage reduces target health | `run-isolated-combat-authority-smoke.ps1` |
| Disabled / removed | Missing `attack_damage`, missing hostility, dead target, or disabled package prevents damage | `AuthoritativeActionEvaluatorTests` and isolated smoke |
| Regression / edge case | Replay does not apply damage twice; bounded health reaches zero; target-authored loot remains | isolated smoke |
| Projection readiness | A recent equip input and accepted animation intent survive only the configured short readiness window | `OntologyCombatVerticalSliceAssetTests` |
| Expired / replayed | Expired equip input and idempotent action replay do not trigger delayed equipment or animation | `OntologyCombatVerticalSliceAssetTests` |

## Performance and multiplayer impact

Initial concepts and Facts now share the placement command transaction and
revision, reducing projection races and command round trips. Per-frame movement
and targeting remain ephemeral. No new polling or per-frame database writes
were added. Unity retries a pending equip candidate locally for at most the
configured grace window; it does not create a durable command until a canonical
weapon target exists.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Localization CSV
- [x] Test scenario manifest
