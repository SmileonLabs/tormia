# Authority Equipment Invariants

## Summary

- **Date:** 2026-07-28
- **Owner:** Codex with Smileon Labs
- **Status:** Implemented and verified
- **Request:** Remove sword-equipment hardcoding and ontology-policy violations.

## Intent

Make weapon equip and unequip complete Authority-owned state transitions.
Distance, exclusive ownership, inverse relations, and removal must not depend
on a trusted Unity client or a weapon-prefab name.

## Data classification

- [x] Durable authored world data
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decisions and boundaries

The published action definition owns equipment semantics. Authority reads the
ephemeral avatar position from the motion registry, reads the durable target
transform from the world entity, evaluates generic runtime constraints, then
applies both equipment relations in one revisioned command. Unity owns only
input targeting and attachment presentation.

Idle motion state is refreshed in Redis without producing world revisions or
durable Facts. Weapon catalog authoring reads an editable content manifest and
does not infer meaning from imported prefab names.

## Ontology representation

```text
avatar equipped_item weapon
weapon equipped_by avatar
weapon can_equip True
weapon has_concept Weapon
```

`equip_weapon` sets the forward relation with the data-declared inverse
predicate. `unequip_weapon` removes both. `maxActorTargetDistance` is a generic
non-durable runtime constraint, not a world Fact.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Nearby unowned weapon creates both relations | `EquipWeapon_ReplacesForwardRelationAndMaintainsInverseRelation` |
| Disabled | Occupied or distant weapon is rejected without mutation | `EquipWeapon_OccupiedByAnotherActorIsRejected`, `RuntimeDistanceConstraint_RequiresAvailableNearbyPositions` |
| Removal | Unequip removes both relations and restores world presentation | `UnequipWeapon_RemovesForwardAndInverseRelations`, Unity attachment tests |
| Authoring | Weapon definitions come from editable manifest and require grip points | `WeaponCatalogIsBackedByEditableContentManifest`, `WeaponProfileRequiresAuthoredGripPoint` |

Final verification passed with 11 Authority server tests, 22 Unity EditMode
tests, 12 Unity PlayMode tests, a clean Unity Console, and
`scripts/verify-development.ps1 -RequireServices`.

## Performance and multiplayer impact

Equip performs one ephemeral motion read and one durable target-transform read
inside the serialized world command. Idle motion refreshes the Redis TTL once
per second and does not broadcast a revision. The world lock prevents two
actors from simultaneously claiming one weapon.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Language pack / migration
- [x] Regression scenario list
