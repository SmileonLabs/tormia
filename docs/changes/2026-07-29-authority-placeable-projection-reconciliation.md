# Authority Placeable Projection Reconciliation

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV world/runtime pipeline
- **Status:** Implemented
- **Related issue:** Visible weapons could be absent from Authority and an older automatic carry Rule Block remained beside the F-interaction block.

## Intent

Make a visible runtime placeable and its gameplay contract traceable to the
same Authority projection. Eliminate local ghost objects and complete the
one-time Rule Block transition without overwriting later user authoring.

## Data classification

- [x] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority

## Decision and boundary

The current Authority projection owns membership of runtime placeable
presentations after world entry. Unity removes a GUID-bearing placeable absent
from that projection, except while its Authority-first placement command is in
flight. No prefab, mesh, definition, or object name participates in this
decision.

Catalog semantic transitions now distinguish:

1. a versioned replacement, which may add its replacement only when crossing
   the version that introduced it; and
2. a versioned retirement, which removes an obsolete binding only when crossing
   its declared version.

Weapon contract version 3 retires
`AutoCarryNearbyCarryable/?object`. Version-1 weapons migrate to
`EquipItemOnInteractionIntent/?target`; version-2 weapons only remove the stale
binding. If the user removed the replacement at version 2, entry does not
recreate it.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Projected placeable remains visible and receives its current contract | `AuthorityProjectionMembershipRemovesOnlyConfirmedGhostPresentation`, `WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract` |
| Disabled / removed | Non-projected runtime placeable is removed; stale automatic carry binding is retired | Same tests plus live projection inspection |
| In flight / user authored | Pending Authority-first placement remains; later Rule Block removal or re-add is preserved | Membership test and version-gated migration/retirement data |

## Performance and multiplayer impact

Reconciliation runs only when an Authority projection arrives. It performs no
per-frame query and creates no durable command for presentation cleanup.
Rule-binding retirement uses the existing revisioned Authority command path.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Harness scenario manifest

