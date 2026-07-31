# Player-facing localization contract

## Summary

- **Date:** 2026-07-30
- **Owner:** Codex / TOV
- **Status:** Verified
- **Related issue or request:** Full audit and repair of untranslated UI, including newly added content

## Intent

Make account entry, reusable catalog content, shipped quests, combat feedback,
and dynamic ontology summaries switch language consistently without storing
localized ontology identifiers.

## Data classification

- [x] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

CSV language packs own shipped player-facing translations. Workflow components
carry a localization key and arguments; raw Authority and transport status stays
diagnostic. Canonical ontology IDs and account/world data are not translated.
User-authored free names and prose remain pass-through unless an explicit key is
provided.

## Ontology expression

No gameplay meaning changes. Canonical rule, profile, concept, relation,
placeable, character-template, and part IDs are localized only when presented.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | English and Korean resolve for account status, catalogs, quests, combat, and dynamic summaries | `OntologyLanguagePackServiceTests`; project coverage validator |
| Disabled / removed | Missing keys and raw player-facing status assignments are reported | Source-boundary and coverage checks |
| Regression / edge case | User-authored prose passes through and canonical IDs stay unchanged | Default-quest fallback and canonical-resolution tests |

## Performance and multiplayer impact

Localization is resolved at UI refresh/status boundaries only. No new polling,
Authority commands, durable writes, or network fan-out are introduced.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Localization CSV / migration, if needed
- [x] Test scenario manifest, if needed
