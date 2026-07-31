# Target-Locked Combat Approach

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Verified
- **Related request:** Stop the avatar oscillating between attack and movement
  when repeatedly clicking a monster

## Intent

Keep one monster click in the combat route. An out-of-range target should be
approached using authored range, while busy and cooldown responses must not
turn the same click into terrain movement.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority

## Decision and boundary

Unity may retain the observed target as an ephemeral input/navigation intent.
World Authority continues to own action availability, Rule Block evaluation,
range, hostility, life, cooldown, damage, and animation intent. The navigation
stop radius reads the equipped tool's canonical `attack_range`; the small
inset is collision/navigation tolerance only and grants no attack permission.

Only `action_target_out_of_range` starts approach. Busy and cooldown wait
without movement. Missing rules, actions, semantics, or valid range return to
the non-combat click path so Rule Block removal still removes combat behavior.

## Ontology representation

- Triple: `tool attack_range <positive numeric value>`
- Triple: `tool attack_action <canonical action id>`
- Triple: `tool swing_action <canonical action id>`
- Rule Blocks: `MeleeAttackOnPrimaryIntent`,
  `SwingWeaponOnPrimaryIntent`
- Physical Meaning: existing `HandheldWeapon`
- Unity adapters retain no durable Fact and do not evaluate the rules.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Out-of-range combat click approaches to authored range and re-previews once | `CombatApproach_UsesAuthoredRangeWithNavigationInset` |
| Removed | Missing Rule Block does not activate approach or hidden attack behavior | `CombatRejection_RemovedRuleDoesNotFallBackToApproach` |
| Regression | Busy/cooldown never becomes movement; direct input cancels approach | `CombatRejection_OutOfRangeApproachesButCooldownDoesNotMove`, `DirectMovement_CancelsPendingCombatApproach` |

## Performance and multiplayer impact

Approach state is local and ephemeral. Authority preview is retried only after
arrival or a bounded cooldown/busy interval. No per-frame Fact, event, revision,
or database write is added.

## Updated documentation

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Core regression scenario
- [ ] Localization or migration not required
