# Authority action animation intent

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development
- **Status:** Implemented and verified
- **Request:** Make the accepted monster attack play animation without a
  controller-owned hard-coded clip or intent.

## Intent

An accepted versioned action selects a canonical animation intent from content
data. The actor's registered repertoire and the animation database then select
the compatible clip.

## Data classification

- Unity presentation
- World Authority content definition/projection
- Account/world actor repertoire projection

## Decision and boundaries

The published action definition owns `presentation.actorAnimationIntent`.
World Authority validates and projects that metadata. Unity input and combat
controllers submit identity-only action commands and do not select animation.
`OntologyAnimationAdapter` presents the accepted intent without persisting an
`animation_intent` Fact.

## Ontology representation

```text
published attack v5
  presentation.actorAnimationIntent = AttackLight

Player has_animation Anim_Sword_LightAttack
Anim_Sword_LightAttack intents AttackLight
```

The presentation intent is ephemeral command-result metadata. It is not a
durable world Fact and does not alter attack conditions or effects.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Exact accepted action version resolves `AttackLight`, and the player repertoire resolves the sword clip | Unity EditMode asset/resolver tests |
| Removed | Missing presentation metadata produces no animation | Unity EditMode resolver test |
| Rejected/replayed | Rejected or replayed commands produce no presentation | Resolver test |
| Server validation | Invalid canonical presentation intent is rejected | World Authority evaluator test |
| End-to-end Authority | Published intent is projected with the exact accepted action version; equip, attack, death, replay, disable, and resume still pass | `scripts/run-combat-authority-smoke.ps1` |

## Performance and multiplayer impact

No polling, per-frame Fact, or DB write was added. Resolution happens once per
completed Authority command and uses the already loaded action projection.

## 2026-07-31 actor-scope hardening

`CommandCompleted` is a client-wide event, so each animation adapter now checks
the canonical execute-action payload's `actorEntityId` before resolving or
queuing presentation readiness. An adapter ignores actions owned by another
actor; only the owning actor may retry an accepted presentation while its
projection or visual becomes ready. Malformed or actor-free payloads fail
closed. This removes duplicate `authority_animation_intent_unresolved`
expirations without adding an intent, clip, prefab, or actor-name fallback.

The EditMode regression evidence covers the owning-actor path, the
non-owning-actor path, and malformed payload rejection.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `docs/changes/2026-07-28-authority-action-animation-intent.ko.md`
