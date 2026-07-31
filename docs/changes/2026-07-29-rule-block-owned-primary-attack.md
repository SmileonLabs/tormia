# Rule-Block-Owned Primary Attack

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Implemented and verified
- **Related request:** Add left-click monster attack through the ontology policy

## Intent

Make the first reusable melee attack a complete production-line contract:
weapon triples select the capability and tuning, an assigned Rule Block owns
the result, Authority evaluates it, and Unity presents only the accepted result.

## Data classification

- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

`MeleeAttackOnPrimaryIntent` owns health, death, and loot mutations. The
`attack` action transports command-scoped intent and identities only. Weapon
Facts own action ID, damage, range, and cooldown. Unity targeting, animation,
and VFX code cannot manufacture damage or bypass a missing Rule Block.

## Ontology expression

- Weapon facts: `attack_action`, `attack_damage`, `attack_range`,
  `attack_cooldown`, `grants_capability -> MeleeAttack`
- Binding: `tool has_rule_block MeleeAttackOnPrimaryIntent`
- Intent: `actor primary_attack_intent target` (ephemeral)
- Equipment guard: `tool equipped_by actor`
- Target guards: `Damageable`, `Hostile`, `is_alive -> true`
- Result: rule-owned health adjustment and guarded death/loot facts
- Meaning/presentation: `HandheldWeapon`, `AttackLight`, combat-catalog VFX

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Bound tool rule evaluates damage and the projected input route is available | `PrimaryAttackRuleBindsToToolAndOwnsDamage`, `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| Disabled / removed | Removing the tool block removes both evaluation and input routing | `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`, `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| Regression / edge case | Range and cooldown come from tool facts; package/manifest/catalog versions agree | `AttackRangeAndCooldownResolveFromToolFacts`, `AttackCooldownRuntimeRejectsImmediateDuplicate`, `PublishedDevelopmentActionsContainEquipAndGuardedDeath` |
| Existing world migration | Version 4 adds missing attack predicates once and preserves an existing or later removed value | `AttackFactIntroductionRunsOnceAndPreservesAuthoredValue` |
| Live Authority path | An isolated world publishes and assigns the rule, rejects its removal, enforces Fact-owned cooldown, and reaches guarded death/loot | `scripts/run-combat-authority-smoke.ps1` |

Verification completed on 2026-07-29:

- World Authority tests: 37 passed
- Unity EditMode combat/animation tests: 36 passed
- Unity EditMode semantic/authority-role tests: 20 passed
- Isolated Authority combat smoke: passed
- Unity Console compilation errors after synchronization: 0

## Performance and multiplayer impact

Target position and cooldown state remain ephemeral. Redis uses a keyed
cross-replica lease; there is no per-frame database write. Only accepted rule
results enter the revisioned world event stream.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
