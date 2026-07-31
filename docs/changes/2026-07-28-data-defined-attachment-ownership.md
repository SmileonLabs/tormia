# Data-defined attachment and transform ownership

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development
- **Status:** Implemented; Unity runtime verification pending
- **Request:** Remove combat attachment hard-coding and prevent equipped items
  from being pulled back to their durable ground transform.

## Intent

Player and NPC equipment must use one reusable ontology-driven attachment
pipeline. A weapon is not attached because combat code recognizes its prefab;
it is attached because an Authority relation matches its selected attachment
profile.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [ ] Runtime observation
- [x] Inferred/action state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundaries

World Authority owns the durable attachment relation. `OntologyAttachmentProfile`
owns the canonical relation predicate, direction, anchor and presentation
settings. `OntologyAttachmentAdapter` exclusively owns parenting and temporary
physics presentation while that relation exists.

The combat presenter owns only optional VFX metadata. It must not parent the
weapon, disable colliders, or retain a fallback equipment state. The durable
projection must not write a placed transform while an attachment adapter owns
the presented transform.

## Ontology representation

- `attachment_relation_predicate`
- `attachment_relation_direction`
- `ItemToActor`
- `ActorToItem`
- Starter right-hand weapon contract:
  `Actor equipped_item Item`

No mesh, prefab or instance name participates in the attachment decision.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | An arbitrary item with an Actor-to-Item profile attaches, disables physics and owns its transform | `ActorToItemRelationUsesGenericAttachmentAndPhysicsOwnership` |
| Disabled / removed | Removing the relation detaches the item and restores world transform and physics ownership | Same PlayMode test plus `DurableProjection_YieldsAndRestoresOwnershipForAttachmentPresentation` |
| Regression / exception | Combat presenter exposes no direct attachment fallback; ordinary placed objects still receive projected transforms | `RightHandWeaponUsesGenericDataDefinedAttachmentContract` and existing projection tests |

## Performance and multiplayer impact

Projection application becomes two-phase: entity existence and semantics are
resolved before durable transforms. Attachment adapters are synchronized once
per received projection, not per frame or per database event. No new durable
command or polling path is introduced.

## Updated documentation

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Ontology localization CSV
- [x] Relevant tests
