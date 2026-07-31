# Persistent defeated presentation

## Summary

- Date: 2026-07-29
- Status: Implemented and verified
- Scope: Unity presentation of Authority-owned durable combat state

## Problem

The Authority correctly retained `is_alive=false` and `current_health=0`, but
the one-shot death clip returned to the base idle pose when it ended. The
entity looked alive while correctly rejecting further attacks as defeated.

## Decision

- A projected defeated state selects the canonical death animation intent.
- The generic animation adapter treats that intent as persistent until the
  owning projection clears defeat or removes the entity.
- The validated clip's own duration and frame rate select its final held pose.
- No Animator state name, prefab, monster type, mesh, or object name is used.
- Durable health, target eligibility, damage, and retirement remain owned by
  World Authority; this change owns presentation only.

## Verification

- Runtime inspection: `is_alive=false`, `current_health=0`, collider disabled,
  persistent `Death` intent, `Die` clip held at its final frame with speed 0.
- `DefeatedStateKeepsItsPersistentDeathPresentation` proves clip completion and
  movement cannot release the defeated pose, while an ordinary transient still
  returns to its base presentation.
