# Rule-Block-First Gameplay Policy

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Implemented
- **Related request:** Prevent new gameplay logic from bypassing reusable Rule Blocks

## Intent

Every new reusable gameplay behavior must become an assignable production-line
component. A user can give the same behavior to another compatible object by
authoring its triples and assigning the Rule Block.

## Data classification

- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The Rule Definition and assigned Rule Block own reusable gameplay logic. Input
and transport own only the trigger and canonical intent/observation. An
Authority action that checks `has_rule_block` and then writes the block's final
result directly is a duplicate hidden rule and is not accepted.

## Ontology expression

- Trigger: canonical input intent or runtime observation
- Assignment: `entity has_rule_block RuleBlockId`
- Result: produced by the assigned Rule Definition
- Presentation/physics: consumes the evaluated result

The first failing guard targets weapon equipment: `equip_weapon` must not write
`equipped_by` directly. A reusable `EquipItemOnInteractionIntent` Rule Block
must own the equipment result before the F path becomes a production baseline.

The production migration now implements that boundary:

- `equip_weapon` transports only an ephemeral `interaction_intent`.
- World Authority resolves the exact active target binding and evaluates the
  immutable published Rule Definition.
- Durable mutations record `source_rule_binding_id`; removing the binding or
  package retracts the results that it owns.
- Existing catalog weapons advance through semantic contract version 2 using a
  data-authored `AutoCarryNearbyCarryable/?object` to
  `EquipItemOnInteractionIntent/?target` migration.

Every gameplay-pipeline harness scenario also records owners for trigger,
intent/observation, triples, Rule Block, evaluation, result, conditional
Authority persistence/projection, Meaning/Profile, adapter, and player
experience. Optional Authority or physical branches require an explicit N/A
reason rather than silent omission.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Intent plus assigned Rule Block produces the result for any compatible object | `ReusableEquipmentLogicMustBeOwnedByRuleBlock`, `EquipWeapon_CoexistsWithEquipmentInAnotherSlot` |
| Disabled / removed | Missing or removed block produces no result; owned durable results are retractable by binding provenance | `EquipWeapon_WithoutRuleBlockIsRejected`, `DerivedCleanupPreservesMatchingAuthorityDurableResult`, harness scenario `rule-block-first-reusable-gameplay` |
| Regression / edge case | Transport produces no mutation, cannot mix invocation with direct effects, and no pipeline stage is omitted | `EquipWeaponTransportCarriesIntentWithoutCreatingResult`, `RuleInvocationCannotHideADirectEquipmentEffect`, `CanonicalGameplayPipelineStagesHaveExplicitOwners` |

## Performance and multiplayer impact

The policy adds no polling or per-frame database writes. Intent transport must
remain ephemeral where appropriate; durable results remain revisioned and
idempotent at the Authority boundary.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
- [x] `AGENTS.md`
