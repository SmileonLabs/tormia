# Authored Support and Planar Reconciliation

Date: 2026-07-31

## Decision

World Authority grounding is derived only from durable entities with authored
`WalkableSupport` Box proxy semantics. The former checkpoint-Y ground fallback
is removed. Fresh owned server snapshots may create a bounded X/Z correction,
but Unity consumes that correction through the sole
`OntologyCharacterMotionCoordinator` `CharacterController.Move` call.

## Ownership

- Durable world data: support entity transform and collision/physical facts.
- Authored player data: maximum step height and ground clearance.
- Ephemeral Authority state: grounded state, support entity identity, velocity,
  and fixed-tick pose.
- Unity presentation: local collision prediction and bounded planar smoothing.

Passive support geometry has no trigger or action result, so a new Rule Block
is not applicable. Existing locomotion and jump actions still invoke their
assigned Rule Blocks.

## Removed path

Retiring the support or removing its role/proxy semantics removes Authority
grounding. Checkpoint Y, Unity meshes, object names, static colliders, direct
Transform writes, and a second `CharacterController.Move` do not restore it.

## Verification

- Server policy proves authored clearance and support-removal behavior.
- EditMode tests prove deterministic support identity and bounded/stale
  reconciliation behavior.
- PlayMode proves correction is deferred until and consumed by the single
  coordinator Move.
