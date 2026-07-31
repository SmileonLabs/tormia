# Authority-Approved Empty Swing

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Superseded for player input routing; runtime contract retained
- **Related request:** Make the standard left-click melee swing play without a
  target while keeping target damage ontology-owned

## Intent

> Superseded on 2026-07-29: the revision-free runtime swing remains reusable,
> but shared left-click input no longer requests it without a living hostile
> target inside the authored attack range. See
> `2026-07-29-target-gated-primary-melee-click.md`.

Give primary melee input the expected action-RPG feel without moving gameplay
authority into Unity. A left click may present a swing in empty space, while
only a separately approved target action may mutate health, death, or loot.

## Data classification

- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The weapon authors `swing_action -> swing_weapon` and receives the reusable
`SwingWeaponOnPrimaryIntent/?tool` Rule Block through semantic contract version
5. That block owns `runtimePresentation.actorAnimationIntent -> AttackLight`
and intentionally owns no durable effects. World Authority evaluates it through
`POST /v1/worlds/{worldId}/runtime/actions`, rejects any mutation-bearing runtime
rule, and does not advance world revision.

`MeleeAttackOnPrimaryIntent/?tool` remains the independent damage owner. Unity
may request it only when an Authority-projected hostile target is valid. Unity
never turns the swing presentation into damage, and the damage transport does
not duplicate animation selection.

## Ontology expression

- Trigger: left mouse through `Player/Attack`
- Weapon Triple: `tool swing_action swing_weapon`
- Swing binding: `tool has_rule_block SwingWeaponOnPrimaryIntent`
- Swing intent: `actor primary_swing_intent actor` (ephemeral)
- Swing result: `AttackLight` runtime presentation, no durable mutation
- Damage binding: `tool has_rule_block MeleeAttackOnPrimaryIntent`
- Damage intent: `actor primary_attack_intent target` (command-scoped)
- Durable result: rule-owned health, guarded death, and loot mutations

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / empty space | Authority approves `AttackLight` without a durable mutation or revision increase | `PresentationOnlySwingRuleAcceptsWithoutDurableMutation`, `scripts/run-combat-authority-smoke.ps1` |
| Enabled / valid target | The swing is approved, then the separate damage Rule Block may mutate the target | `AttackInputResolvesActionAndRuleFromAuthorityProjection`, `scripts/run-combat-authority-smoke.ps1` |
| Disabled / removed | Removing the swing block removes empty-space presentation while the damage block remains independent | `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| Invalid runtime rule | A runtime action carrying mutations is rejected before commit | runtime-action mutation guard in `WorldRepository.EvaluateRuntimeAction` |
| Animation production line | Rule-owned `AttackLight` resolves in the validated Manifest, Database, and ActorProfile projection | `PublishedDevelopmentActionsContainEquipAndGuardedDeath`, `CurrentAnimationAssetsMatchManifestProjection` |

Verification completed on 2026-07-29:

- World Authority tests: 38 passed
- Unity EditMode combat/animation tests: 36 passed
- Isolated Authority combat smoke: passed
- Unity Console compilation errors after synchronization: 0

## Performance and multiplayer impact

Empty swings create only an ephemeral Authority evaluation and Redis cooldown
lease. They create no world event, Fact, or revision. Target damage keeps the
existing revisioned, idempotent command path.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
