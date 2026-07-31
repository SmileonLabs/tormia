# Authored Server Collision Proxy

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV development harness
- **Status:** Implemented and verified
- **Request:** Begin the server-authoritative movement phase with trusted,
  lightweight collision geometry.

## Intent

Give World Authority a reusable collision representation that comes from
ontology data instead of Unity visuals. This creates the prerequisite for
fixed-tick movement validation without prematurely replacing the working local
CharacterController.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decisions and boundaries

The durable entity Transform plus explicit collision proxy Facts are the source.
Unity authoring sends them through the existing revisioned command path. Runtime
clients do not upload mesh-derived authority geometry. World Authority may
project Capsule and Box proxies but does not yet integrate player movement.

## Ontology representation

- `collision_role`
- `collision_proxy_shape`
- `collision_radius`, `collision_height`
- `collision_size_x`, `collision_size_y`, `collision_size_z`
- `collision_center_offset_x`, `collision_center_offset_y`,
  `collision_center_offset_z`

The existing locomotion action and `MovePlayerFromIntent` Rule Block still grant
movement behavior. `LocalCharacterController` remains the current presentation
Physical Meaning; collision proxy Facts add server geometry and do not create a
second Unity movement owner.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Complete player Capsule and generic Box data produce bounded server proxies | `PlayerMotionPolicyTests.AuthoredCapsuleCollisionProxyBuildsWithoutVisualFallback`; `AuthoredBoxProxySupportsReusableUgcCollisionMeaning` |
| Disabled / removed | Removing a required Capsule dimension rejects construction | `PlayerMotionPolicyTests.RemovingRequiredCollisionProxyDimensionFailsClosed` |
| Regression / exception | Zone validation accounts for the whole proxy extent | `PlayerMotionPolicyTests.ZoneValidationUsesWholeProxyInsteadOfOnlyItsCenter` |

## Performance and multiplayer impact

Proxy configurations are queried from durable facts and contain only primitive
numbers. No render mesh or per-frame durable write is introduced. Fixed-tick
integration and snapshot reconciliation remain out of this milestone.

## Updated documentation

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Language-pack CSV / migration, if required
- [x] Harness scenario list, if required
