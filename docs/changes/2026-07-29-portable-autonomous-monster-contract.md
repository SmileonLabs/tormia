# Portable autonomous monster contract

## Summary

- Date: 2026-07-29
- Area: durable world ontology, World Authority runtime, Unity presentation
- Status: implemented and verified across server, development harness, and
  live Unity EditMode execution

## Intent

Make monster behavior a reusable ontology contract that can be attached to any
placed entity instead of a behavior tied to the Beholder prefab.

## Ownership decision

Authored concepts and Facts declare autonomous combat capability and tuning.
An assigned Rule Block enables attack behavior. `AuthorityKinematic` Physical
Meaning selects the server-owned movement adapter. World Authority evaluates
targets, movement, cooldown, damage and death. Unity only presents accepted
motion and animation intents.

## Complete path

`autonomous_melee_monster` authors the concepts, faction/hostility/life/tuning
Facts, `attack_action`, canonical animation intents, Rule Block binding and
Physical Meaning. The selected action definition must invoke the same Rule
Block assigned to the actor. Authority schedules the actor and applies accepted
damage through revisioned idempotent world commands. Unity interpolates the
ephemeral actor pose and resolves animation through the manifest/database/profile
production line.

## Removed path

Removing the Rule Block, required authored semantics, action-to-rule link, or
Physical Meaning excludes the entity from autonomous simulation. No prefab,
mesh, display name, or Unity adapter fallback restores it.

## Existing placed instances

Beholder starter content advances to semantic contract version 3. Unmarked
legacy instances receive the complete current catalog contract once, while
versioned instances receive only the Fact and Rule Block introductions or
retirements crossed by the transition. Version 3 retires the obsolete authored
`current_health=30` and `is_alive=true` defaults only when another active value
for the same predicate exists. This normalizes conflicting legacy state without
reviving a dead monster or deleting the only valid state. Once advanced, a
user's later removal is preserved and is not repaired as a missing default.

Contract introduction respects canonical relation cardinality. `has_concept`
is declared set-valued, so migration checks exact values rather than treating
any existing concept as the whole set. Single or unspecified predicates still
preserve an existing authored value. Unmarked legacy repair uses the same
cardinality rule and therefore does not append a catalog default beside an
existing single-valued state.

## Verification

- Server contract tests cover accepted damage, Rule-Block removal, target
  faction eligibility and bounded movement.
- Unity EditMode tests were added for the reusable preset, Beholder data projection,
  development package, manifest/profile animation repertoire, and absence of
  monster-name branches in the presentation adapter. They also guard against
  accidentally attaching monster meaning to an ordinary catalog object.
- Unity Core, Runtime, Editor, and EditMode test assemblies compile with no
  errors through Unity's generated Roslyn response files.
- Live Editor MCP verification passed the contract migration and related
  combat/Authority suite (53/53). A live Authority projection was normalized
  from `current_health={0,30}`, `is_alive={false,true}`, contract version 2 to
  `current_health=0`, `is_alive=false`, contract version 3. A healthy monster
  remained `current_health=30`, `is_alive=true` while advancing to version 3.
  The complete EditMode regression suite then passed 202/202.
