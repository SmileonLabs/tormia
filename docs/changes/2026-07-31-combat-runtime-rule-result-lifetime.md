# Combat Runtime and Rule-result Lifetime

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV team
- **Status:** Verified
- **Related issue or request:** Monsters stopped attacking after world-entry and ontology refactoring.

## Intent

Keep autonomous combat available for a player who has entered a Zone, make
action resolution unambiguous, and prevent Rule Block migration/removal from
rewinding accepted combat state.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

World Authority remains the sole owner of target, chase, attack, health, death,
respawn, and loot evaluation. Unity owns only authenticated runtime-presence
lease renewal, input/observation transport, and presentation after `InWorld`.
SignalR is not a gameplay availability dependency. Unity does not duplicate a
Rule Block or infer action identity from names.

## Ontology expression

- Action-reference relations use the `ActionRef` term kind and preserve exact
  immutable IDs.
- Single-cardinality meaning-package relations replace prior values atomically.
- Rule effects declare `RuleBound` or `DurableState`.
- Default `RuleBound` is canonicalized as the legacy omitted wire value so a
  schema extension cannot create a false immutable-version conflict.
- Explicit `DurableState` remains part of the immutable Rule payload.
- `MeleeAttackOnPrimaryIntent`, `AutonomousMeleeCombat`,
  `RespawnPlayerOnDeath`, and `CollectAvailableLoot` persist accepted state
  transitions.
- `EquipItemOnInteractionIntent` remains Rule-bound.
- Player semantic contract version 4 repairs vitality from existing semantic
  data and migrates enabled respawn bindings to their published version.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | HTTP Zone lease keeps autonomous scheduling active; exact action IDs resolve; accepted attack/respawn results are durable. | `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`; 61 passing World Authority tests; 14 focused Unity EditMode tests passed; live entry reached `InWorld` |
| Disabled / removed | Removing a required Rule Block stops future behavior; Rule-bound equipment is retracted; accepted health/death history remains. | `AuthoritativeActionEvaluatorTests`; `OntologyCombatVerticalSliceAssetTests` |
| Regression / edge case | Legacy action-produced facts are promoted only from immutable Rule content; removed player bindings are not recreated. | `ContentRuleCanonicalizationTests`; player semantic migration checks |
| Schema compatibility | A newly serialized default `RuleBound` is wire-equivalent to a legacy omitted field; explicit `DurableState` remains immutable content. | `DefaultRuleBoundLifetimePreservesLegacyImmutablePayload`; `DurableLifetimeRemainsImmutableRuleContent` |

## Performance and multiplayer impact

One ephemeral authenticated HTTP lease is renewed per active Unity Zone session
at the configured heartbeat interval. It does not advance world revision or
write per-frame Facts. Existing Redis Zone-session expiry and Authority
schedulers remain authoritative and multiplayer-safe.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] SQL migration
- [x] Test scenario manifest
