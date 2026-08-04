# Unity-owned character collision presentation

## Summary

- Date: 2026-08-01
- Status: Implemented, verification in progress
- Scope: Unity presentation, Physical Meaning, player collision harness

## Decision

Ontology data selects and tunes physics. Unity Physics resolves actual local
collision, slopes, and steps. The custom step solver that sampled an obstacle
and injected an upward coordinate is removed.

`maximum_step_height` remains canonical authored tuning and maps to Unity
`CharacterController.stepOffset`. The same Physical Profile also owns slope
limit, skin width, and minimum move distance. Removing the profile clears the
controller tuning and movement lease.

## Boundaries

- Triple/Rule Block/Physical Meaning: permission and tuning.
- Unity CharacterController: one collision-resolved Move per frame.
- Support Probe: ephemeral Unity cast observation only.
- World Authority: authored lightweight proxies and fixed-tick shared motion;
  it does not inspect Unity meshes or colliders.
- One-time world-entry grounding remains readiness initialization.

## Verification

- Enabled: a WalkableSupport step is resolved by CharacterController using the
  profile-authored step offset.
- Removed: no `TryResolveStep`, sampled rise, or hidden Transform fallback
  remains in runtime locomotion.
- Regression: slopes do not receive a sampled upward or backward displacement.
