# Target-Gated Primary Melee Click

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Verified
- **Related issue or request:** Keep left-click movement, and attack only when
  the clicked monster is within weapon range

## Intent

Prevent equipped weapons from stealing ordinary movement clicks. Left click
remains click-to-move unless the pointer identifies a projected living combat
target. The later target-locked approach contract defines how an out-of-range
combat target is approached without leaking into terrain navigation.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The weapon's `attack_range` Triple, assigned attack and swing Rule Blocks, and
the target's Authority-owned hostile/alive Facts decide whether the shared
click routes to combat. Unity reports only a projected pointer candidate and
the action IDs authored on the equipped tool. World Authority evaluates both
actions through a side-effect-free preview that shares the execution
preparation path. Preview applies no mutations, acquires no cooldown, and
writes no revision, event, or Fact. Accepted execution re-evaluates the same
contract before presentation or durable damage.

Empty ground, friendly or dead entities, missing Rule Blocks, and missing or
ambiguous range data leave the click available to movement. A projected living
combat target outside range is reserved by the target-locked approach contract
and navigated using authored range. No mesh, prefab, template, or object name
is used.

## Ontology expression

- Trigger: shared left mouse click
- Tool Triple: `tool attack_range positive-number`
- Tool actions: `tool attack_action attack`,
  `tool swing_action swing_weapon`
- Tool bindings: `MeleeAttackOnPrimaryIntent/?tool` and
  `SwingWeaponOnPrimaryIntent/?tool`
- Target Facts: `target has_concept Damageable`,
  `target combat_disposition Hostile`,
  `target is_alive True`
- Runtime observation: pointer target; Authority runtime actor and target positions
- Authority route: side-effect-free preview of both assigned Rule Blocks
- Authority result: approved presentation, then revisioned rule-owned damage

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | A living hostile inside the authored range is accepted by Authority preview and follows the Authority attack path | `AttackInputPublishesAuthoredActionsWithoutLocalRuleEvaluation`; `PresentationOnlySwingRuleAcceptsWithoutDurableMutation` |
| Disabled / removed | Removing a required Rule Block or hostile Triple makes Authority preview reject and releases the click without a fallback | `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`; `PrimaryAttackRejectsFriendlyOrDefeatedTarget` |
| Regression / edge case | Empty ground creates no preview; an out-of-range living combat target is reserved for the target-locked approach path | `AttackInputPublishesAuthoredActionsWithoutLocalRuleEvaluation`; `OntologyPlayerInputPriorityTests`; `AttackRangeAndCooldownResolveFromToolFacts`; `CombatRejection_OutOfRangeApproachesButCooldownDoesNotMove` |

Verification completed on 2026-07-29:

- TOV development harness, Docker Authority build, service status, and health:
  passed
- Current Unity runtime assembly and Unity EditMode test assembly Roslyn
  compilation: passed
- Unity EditMode combat and input-priority suites: 50 passed, 0 failed
- World Authority unit suite: 40 passed, 0 failed
- Isolated combat Authority smoke, including revision/health-neutral attack
  preview and Rule-Block removal/reapply: passed
- Unity Console errors after script refresh: 0

## Performance and multiplayer impact

The input adapter performs one existing pointer raycast on a click. A projected
candidate causes one attack preview and, only after it succeeds, one swing
preview. Empty ground creates no request. Target-locked approach may retry
preview after arrival or a bounded busy/cooldown interval; it creates no
per-frame database event, Fact, cooldown lease, or revision. World Authority
remains the multiplayer-consistent evaluator and execution verifier.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, if needed
- [x] Test scenario manifest
