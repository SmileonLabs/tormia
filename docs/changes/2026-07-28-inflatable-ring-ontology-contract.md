# Inflatable Ring Ontology Contract

## Summary

- **Date:** 2026-07-28
- **Owner:** Codex with Smileon Labs
- **Status:** Implemented; Unity runtime verification pending
- **Request:** Restore the inflatable-ring flow first, then use it as the
  Triple -> Rule Block -> Physical Meaning baseline for weapon migration.

## Intent

Restore tube attachment and temporary swimming without a Unity or object-name
fallback. Make the tube the first executable contract that proves both the
enabled behavior and removal of behavior when its Rule Block is removed.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decisions and boundaries

The registered world avatar owns an authored `Actor` world role. The tube owns
its semantic and physical profile Triples and versioned Rule Block bindings.
Proximity and interaction intent remain ephemeral observations. Rule inference
owns equipment-slot, attachment, and temporary-skill results. Unity adapters
only measure proximity and present the evaluated attachment/physics.

Development Rule definitions are published before object bindings. Rule
binding IDs are deterministic. A one-time generic catalog migration repairs
entities created before Rule publication and writes
`semantic_contract_version` only after every binding succeeds. The marker
prevents later intentional Rule-Block removal from being undone.

Weapon Authority actions are explicitly outside this completed slice. They
must be migrated to the proven Rule-Block-gated contract next.

## Ontology representation

```text
avatar has_concept Actor
tube has_concept Wearable
tube physical_profile LightBuoyant
tube attachment_profile WaistInflatableRing
tube has_slot Waist
tube pickup_behavior SelectThenEquip
tube grants_skill Swimming
tube has_rule_block AutoEquipNearbyWearable
tube has_rule_block EquippedItemGrantsTemporarySkill
```

Runtime inference produces and retracts:

```text
EquipmentSlot_{actor}_{Waist} equipped_item tube
tube equipped_by avatar
avatar has_temporary_skill Swimming
```

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Actor intent plus proximity and complete tube data attaches the tube and grants temporary Swimming | `InflatableRingVerticalSliceRequiresTripleRuleBlockAndPhysicalMeaning` |
| Disabled | Removing `AutoEquipNearbyWearable` retracts slot, attachment, and temporary skill | same PlayMode test |
| Authority avatar | New placement and repair preserve canonical Actor | `NewAvatarPlacementAuthorsCanonicalActorTriple`, `RuntimeFoundationDetectionUsesCanonicalFactsOnly` |
| Multiplayer actors | NPC Actors do not break local-player proximity resolution | `LocalPlayerRoleWinsWhenSeveralActorsExist` |
| Replay | Entity/rule/variable produces a stable binding ID | `RuleBindingIdentityIsDeterministicAndDataScoped` |

`scripts/verify-development.ps1 -SkipServerBuild` passes. Full Unity test and
Console verification remain pending until the Unity MCP instance reconnects.

## Performance and multiplayer impact

The repair runs only for editable legacy entities with catalog defaults and no
contract marker. Normal entry does no repair writes. Proximity remains a
throttled ephemeral Unity observation and creates no per-frame durable events.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Language pack / migration
- [x] Regression scenario list
