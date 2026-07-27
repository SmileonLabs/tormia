# Character Part Replacement Policy

## Summary

- Date: 2026-07-26
- Status: Implemented and verified
- Scope: account appearance data and Unity presentation

## Intent

Part thumbnails must behave as deterministic replacements and toggles. Body and
Face remain present, while every wearable part can be removed by clicking its
selected thumbnail again. Removing a covering costume must not silently restore
previous clothing.

## Data ownership

Selected part IDs remain account-owned character profile data. Canonical slots,
`required`, linked part IDs, and `conflicts_with_slot` facts define replacement
behavior. Unity owns renderer binding and preview presentation only.

## Decision

- Only canonical `Body` and `Face` definitions are required and reject direct
  unequip.
- Hair, upper/lower clothing, footwear, full-body costumes, outerwear,
  headwear, eyewear, gloves, facial hair, and accessories are optional.
- Selecting another part in the same slot replaces the current part. Selecting
  the equipped optional part again removes it.
- Covering-part conflicts remove the currently equipped conflicting parts
  without recording a restoration history. Removing or replacing the covering
  part leaves those wearable slots empty.
- Full-body definitions bind to the dedicated `Full_body` renderer.
- Variant application copies mesh, materials, and authored local bounds.
- Base definitions restore the cached template renderer state.
- The account preview clone synchronizes mesh and local bounds as well as
  enabled state and materials.
- The preview subscribes to completed part changes so covering-part visibility
  is synchronized in the same interaction, without waiting for a later frame.
- The single base-body choice and the placeholder upper-body mesh stay as
  internal bindings but are hidden from the customization category list.
- Conflict replacement disables only definitions that are actually equipped.
  Unequipped costume definitions cannot turn off a shared hat or clothing
  renderer through their linked parts.
- A regular hat replaces a linked costume hat without unequipping the
  full-body costume or restoring the costume's displaced clothing.
- The editor seed catalog uses the same `Full_body` renderer path and conflict
  policy as the runtime data asset so regeneration cannot restore the bug.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Body or Face clicked again | It remains equipped | `OntologyCharacterPartAdapterTests.ClothingAndFootwearCanBeRemovedWhileBodyAndFaceRemainRequired` |
| Clothing or footwear clicked again | It is removed | `OntologyCharacterPartAdapterTests.ClothingAndFootwearCanBeRemovedWhileBodyAndFaceRemainRequired` |
| Full body replaced by upper body | Previous lower body stays removed | `OntologyCharacterPartAdapterTests.ReplacingFullBodyWithUpperBodyDoesNotRestorePreviousLowerBody` |
| Linked costume removed | Costume, linked part, and displaced wearables stay removed | `OntologyCharacterPartAdapterTests.LinkedCostumePartsEquipAndUnequipAtomically` |
| Variant and base replacement | Authored bounds copy and base mesh restores | `OntologyCharacterPartAdapterTests.VariantCopiesAuthoredLocalBoundsAndBaseSelectionRestoresOriginalMesh` |
| Account preview variant | Preview clone mesh and bounds match source | `TormiaUiSceneSmokeTests.CompositionCreatesOntologyWorldAndUsesSingleUiScene` |
