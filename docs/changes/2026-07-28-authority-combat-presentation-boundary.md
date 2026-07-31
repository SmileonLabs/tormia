# Authority combat and presentation boundary

## Summary

- **Date:** 2026-07-28
- **Status:** Implemented and verified
- **Scope:** Unity input/targeting, World Authority combat, animation,
  attachment, and world-entry presentation

## Intent

Make attack behavior repeatable without adding a new Unity exception for each
weapon, monster, or animation. Keep candidate selection responsive in Unity
while World Authority remains the only owner of gameplay acceptance and
durable effects.

## Data classification

- Durable world data: equipped relations, health, life state, loot state
- Ephemeral observation: pointer candidate, local collision grounding, movement
- Unity presentation: animation, VFX, attachment pose
- Authority: action package version, conditions, range, damage, mutations

## Decisions and boundaries

- The authored `Player/Attack` action is separate from camera look-hold.
- Unity submits only actor, candidate target, equipped tool, and immutable
  action identity. It does not duplicate hostility, range, damage, or death.
- The accepted action's canonical animation intent is resolved through the
  actor repertoire. No rejected or incomplete action has a fallback attack
  clip.
- Required weapon grip metadata is prefab-owned. Missing metadata disables
  attachment presentation instead of attaching to the actor root.
- Entry grounding is a one-time bounded collision presentation before input.
  It does not become a durable Fact or checkpoint rewrite.

## Enabled and removed cases

| Case | Expected result |
| --- | --- |
| Authored attack input selects a projected entity | Authority evaluates the exact versioned action |
| Target is out of range or not hostile | Authority rejects with no damage or attack presentation |
| Accepted action has an available repertoire intent | The approved transient animation plays |
| Animation intent or repertoire is removed | No fallback clip plays; a diagnostic stage is recorded |
| Required calibrated grip exists | Weapon aligns its own grip to the actor socket |
| Required grip or actor socket is removed | Attachment presentation is disabled |
| Grounded first entry | Avatar settles once before input |
| Swimming or mounted entry | Ground snap is skipped |

## Verification

- Unity EditMode combat, animation-production, semantic-validator tests
- Unity PlayMode attachment enabled/removed tests
- World Authority evaluator and runtime-distance unit tests
- Development harness manifest, server build, and Unity Console

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
