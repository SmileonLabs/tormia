# Authority Combat Vertical Slice

## Summary

- **Date:** 2026-07-27
- **Status:** Implemented and verified
- **Scope:** World Authority, ontology action definitions, Unity presentation,
  imported weapon/monster/animation/VFX adapters, harness evidence

## Intent

Establish the first reusable weapon and monster combat production line without
making Unity, prefab names, or imported asset names the owner of damage rules.

## Ownership decision

- Account character appearance remains account-owned.
- Equipped tool, target health, life state, and available world loot are
  revisioned world state.
- Aim input, click position, animation playback, and VFX lifetime are ephemeral.
- Unity submits only actor, target, tool, and immutable definition identity.
- World Authority evaluates conditions and applies all persistent effects in one
  idempotent transaction.

## Ontology and presentation

- `equip_weapon` sets `equipped_item`.
- `attack` requires an equipped `Sword` that grants `MeleeAttack`.
- Bounded numeric adjustment changes `current_health`.
- Generic post-effect guards set death and loot facts at zero health.
- Project wrapper prefabs refer to imported visuals. Animation and VFX are
  selected by semantic intent.

## Verification

- Combat rules publish as immutable package version `1.1.0` instead of trying
  to overwrite the previously published `social_village 1.0.0`.
- The complete package is republished with `help/talk` definition version 2,
  `attack` version 4, and `equip_weapon` version 2 so no definition remains
  stranded under the previous package version.
- Equip input prefers the aimed weapon but falls back to the nearest weapon
  within three metres, so a thin mesh or terrain collider cannot swallow `F`.
- Player combat FBXs import as Humanoid with an Avatar created from each
  downloaded With Skin source. Weapon presentation selects the first valid
  Humanoid child Animator instead of the empty controller Animator on the
  player root.
- Development-only content package publication uses an account-scoped package
  ID. Two local accounts no longer contend for ownership of the same
  `social_village` authoring package or receive a false `403 Forbidden`.
- Imported weapon and monster packages still own their original Built-in
  Standard materials. The project wrapper presentation adapter creates cached
  URP Lit equivalents and transfers base, normal, metallic, and emission maps,
  so previews and runtime objects do not render magenta. Serialized emission
  values are transferred only when the source material enabled `_EMISSION`;
  stale disabled values cannot wash the monster albedo solid white.
- Automatic placement publication is gated by actual world-entry readiness and
  edit permission, not by the unrelated `connectOnStart` convenience flag. If
  `F` targets a legacy or still-publishing local weapon, the bridge first
  publishes its entity and semantic facts, then retries the Authority equip
  action.

| Case | Expected | Evidence |
| --- | --- | --- |
| Enabled | Equip and three attacks produce 30 → 20 → 10 → 0, death, and loot | `scripts/run-combat-authority-smoke.ps1` |
| Disabled | Disabled package or defeated target rejects another attack | Authority smoke and evaluator tests |
| Replay | Reusing the final command ID does not apply damage twice | Authority smoke |
| Resume | A new login reads zero health and available loot | Authority smoke |
| Unity assets | Three swords, Beholder, animation intents, and VFX resolve | `OntologyCombatVerticalSliceAssetTests` |
| Humanoid mapping | Every player combat clip has a generated Humanoid source Avatar | `PlayerCombatClipsHaveHumanoidSourceMappings` |
| Package ownership | Different development accounts produce different immutable package IDs | `DevelopmentPackageIdentityIsScopedToItsPublishingAccount` |
| Material pipeline | Imported Standard materials preserve their base textures through the URP presentation bridge | `ImportedCombatMaterialsBridgeFromStandardToUrpLit` |
| Placement publication | Runtime-ready editors publish regardless of startup connection preference | `PlacementPublicationUsesRuntimeStateAndWorldPermission` |
| Scene composition | Combat input and pooled VFX adapters load with Bootstrap | `TormiaSceneCompositionSmokeTests` |

## Performance

VFX use a small reusable pool. Pointer targeting and animation state remain
local. Only explicit equip and attack commands create durable revisions; no
per-frame combat observation is written to PostgreSQL.
