# Authority Optional Effect Wire Semantics

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV Authority evaluation boundary
- **Status:** Implemented and deployed locally
- **Related issue:** `equip_weapon` reached Authority but was rejected with `invalid_action_effect_guard`.

## Cause

The assigned `EquipItemOnInteractionIntent` Rule Block was present and the
weapon authored the required triples. Unity serialization nevertheless emitted
empty default instances for optional `valueFrom` and `when` fields. The server
interpreted the empty guard as a configured guard with comparison `None` and
rejected the otherwise valid Rule Block.

## Decision

An optional numeric source or guard is configured only when at least one of its
semantic fields differs from the empty default. A completely empty object is
wire-format noise and is treated as absent. A partially configured object
continues to fail strict validation.

This is generic effect semantics and contains no weapon, predicate, Rule Block,
prefab, mesh, or object-name exception. Existing immutable catalog payloads and
checksums are not rewritten.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Empty Unity defaults | Invoked equipment Rule Block evaluates and sets `item equipped_by actor` | `EquipWeapon_IgnoresUnitySerializedEmptyOptionalEffectObjects` |
| Configured numeric metadata | Existing strict numeric validation and sequential guard evaluation remain active | Authority evaluator numeric tests |
| Missing Rule Block | Equipment remains rejected without a Unity fallback | `EquipWeapon_WithoutRuleBlockIsRejected` |

Authority server tests pass 31/31 and the local Docker service was rebuilt and
restarted with the corrected evaluator.

