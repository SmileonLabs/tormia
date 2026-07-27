# Account character creation UI

## Decision

The development account loop now opens a scene-authored character creation panel when no account character is selected. The panel submits display name, the currently supported `player` template, and account-owned equipped parts through `OntologyWorldAuthorityAccountEntryFlow`.

## Boundaries

- Character name, template, and appearance remain account profile data.
- Creating a character does not write a shared-world Fact.
- `TormiaMain` owns the uGUI hierarchy; the binder only finds and connects authored controls.

## Verification

- Unity compilation completed with zero Console errors after import.
- Scene saved with `AccountCharacterCreationPanel` and its authored name input.
- The creation panel and `AccountCharacterAppearancePanel` now compose one Farm UI studio: category list, part grid, live equipped-avatar preview, selected-part detail, name input, and navigation remain editable scene objects.
- Clicking a part thumbnail now toggles equip/unequip immediately through `OntologyCharacterPartAdapter`; the created account profile still receives the adapter's equipped canonical part IDs. A part that the adapter refuses to remove stays equipped and reports the adapter failure instead of using a hidden UI fallback.
- Editor-only sample cards and mannequin show layout in Edit Mode and are disabled at runtime.
- The live preview camera is presentation-only and renders the same avatar changed by the part adapter; it does not create or own gameplay facts.
- Runtime verification replaced `Part_Hairstyle_Base` with `Part_Hairstyle_Male_005` and the center preview updated immediately.
- Thirteen approved character-style category icons are stored as individual transparent 256x256 sprites under `Assets/Art/UI/CharacterCategoryIcons`.
- `CharacterCategoryIconSet` maps canonical slot IDs to presentation sprites. Removing or omitting a mapping disables only that icon while the localized text category remains usable.
- PlayMode verification created 13 vertically scrollable category buttons and confirmed that all 13 received their mapped sprite, including the bottom `Accessory` category.
- The category rail now uses large centered circular icons only. Its rectangular button backgrounds are transparent, labels are hidden while an icon is present, and a localized label appears only as the missing-icon fallback.
- The approved flat character-creation concept is now authored as editable uGUI hierarchy objects rather than a background mockup. Header, category rail, part grid, live preview, selected-detail card, name input, and footer actions retain their existing runtime bindings.
- Project-owned sliced sprites under `Assets/Art/UI/CharacterCreationFlat` provide the warm canvas, surface, inset, input, button, selection-outline, and scrollbar treatments. Removing those presentation sprites removes only the visual treatment; account/profile and ontology behavior remain unchanged.
- Part cards are icon-only circular masked thumbnails. Category and part lists remain vertically scrollable, while their visible scrollbar objects and bindings are disabled.
- The live avatar is framed at 70% of its previous apparent size, preserves the render texture aspect ratio, and is isolated from the world background by the presentation-only preview camera.
- Runtime verification toggled `Part_Hairstyle_Base` from equipped to unequipped and back to equipped by invoking the same thumbnail button used by the UI. The five `OntologyCharacterPartAdapterTests` passed, Unity Console reported zero errors, and `scripts/verify-development.ps1` passed.
- The thumbnail mask and selection ring now use a project-owned 256×256 antialiased circle sprite instead of Unity's low-resolution built-in knob. The compact grid uses 144×144 cells with 6-pixel spacing.
- The composite character-creation panel is visually centered while preserving the authored offset between its appearance and account overlays. Back/Create use standard Unity `Button` components with centered stretch-anchored labels, removing the Farm UI `CustomButton` behavior that restored label Y to 8 after state changes.
- The two character-creation roots were trimmed from 108 hierarchy objects / 354 components to 76 objects / 237 components. Removed items were limited to unused icon-card text nodes, obsolete hidden scrollbar trees, disabled structural graphics, and legacy root/customize buttons; runtime inputs, navigation, scrolling, masks, preview rendering, and ontology adapters remain intact.
