# Combat ontology contract convergence

## Summary

- **Date:** 2026-07-30
- **Owner:** TOV development
- **Status:** Implemented and verified
- **Scope:** Player, weapon, autonomous monster, hunting, presentation, and loot

## Intent

Make the hunting loop reusable for arbitrary authored or generated entities.
No Unity component, prefab name, input binding, or Authority endpoint may
silently own behavior that belongs to authored data and an assigned Rule
Block.

## Data classification

- Durable authored world data: capabilities, action IDs, tuning, profiles,
  Rule Block bindings, and lifecycle state.
- Runtime observation: input edges, positions, pointer candidates, and weapon
  contact.
- Inferred/presented state: animation, VFX, target and defeated presentation.
- Authority transport: revisioned idempotent shared/durable results and
  ephemeral motion intent.

## Decision and boundaries

The canonical contract is:
`Triple -> Rule Block -> immutable action -> World Authority -> result ->
Meaning/Profile -> Unity adapter`.

Equip/unequip, locomotion/jump, target/chase/attack, defeat-loot, and
collection are separate reusable contracts. Attack can invoke the loot rule
only as an explicit post-Rule invocation with its own binding. It is
conditional so non-fatal attacks succeed, while removing it removes loot
availability. Unity resolves projected
semantics and presents results; it does not choose eligibility or create
durable state.

## Ontology representation

- Weapon: `equip_action`, `unequip_action`, `interaction_range`, combat tuning,
  attachment and physical profiles.
- Player: `locomotion_action`, `jump_action`, movement/sprint speed,
  `jump_height`, `gravity_acceleration`, `AuthorityKinematic`.
- Monster: `target_concept`, `targeting_profile`, `chase_profile`,
  `target_action`, `chase_action`, attack and lifecycle action IDs, and
  target/chase/attack/loot Rule Blocks. Target and chase are immutable,
  evaluation-only actions: the scheduler supplies only ephemeral candidates
  and positions, while their assigned Rules decide eligibility and movement.
  Rule bindings are resolved from each immutable action definition rather than
  a scheduler-owned list of Rule IDs.
- Loot: `loot_status` and `loot_collected_by` remain target-owned durable
  provenance.
- Presentation: `Damageable` and canonical animation/VFX intents select Unity
  adapters.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Compatible player, weapon, or monster completes the shared Authority pipeline | Unity EditMode/PlayMode and server unit suites |
| Removed | Removing the action Triple, Rule Block, profile, or meaning removes only its behavior | Negative-path tests in the harness manifest |
| Migration | Existing bindings move to immutable versions without restoring user-removed blocks | Asset migration assertions and synchronized data assets |
| Autonomous control | Target and chase action previews pass only with their assigned Rules and create no mutation | `TargetAndChaseExecuteTheirAssignedRuleDefinitions` |

## Performance and multiplayer

Per-frame movement, targeting observations, and presentation remain ephemeral.
Health, equipment, life, and loot provenance remain revisioned and idempotent.
Autonomous motion stays zone-scheduled by World Authority.

## Final verification

- World Authority tests: 52/52 passed.
- Unity EditMode: 242/242 passed.
- Unity PlayMode: 54/54 passed.
- Isolated clean-database combat Authority smoke: passed.
- Development harness with Docker services and persistent Unity MCP: passed.
- Runtime source audit found no weapon/monster/prefab identity branch and no
  fixed autonomous Rule ID list.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] `tests/harness/core-regression-scenarios.json`
- [x] `AGENTS.md`
